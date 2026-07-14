using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

public interface IRuntimeHealthMetricsProvider
{
    RuntimeHealthProcessMetrics Capture();
}

public interface IRuntimeHealthDispatcherProbe
{
    Task<RuntimeHealthUiProbeResult?> ProbeAsync(CancellationToken cancellationToken);
}

public sealed class RuntimeHealthSnapshotEventArgs : EventArgs
{
    public RuntimeHealthSnapshotEventArgs(RuntimeHealthSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public RuntimeHealthSnapshot Snapshot { get; }
}

public sealed class ProcessRuntimeHealthMetricsProvider : IRuntimeHealthMetricsProvider
{
    public RuntimeHealthProcessMetrics Capture()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new RuntimeHealthProcessMetrics(
            new DateTimeOffset(process.StartTime),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            process.Threads.Count,
            process.HandleCount,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            process.TotalProcessorTime.TotalMilliseconds);
    }
}

public sealed class WpfRuntimeHealthDispatcherProbe : IRuntimeHealthDispatcherProbe
{
    private readonly Dispatcher _dispatcher;

    public WpfRuntimeHealthDispatcherProbe(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<RuntimeHealthUiProbeResult?> ProbeAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return null;
        }

        var completion = new TaskCompletionSource<RuntimeHealthUiProbeResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        long startedAt = Stopwatch.GetTimestamp();
        DispatcherOperation? operation = null;
        try
        {
            operation = _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        Application? application = Application.Current;
                        Window? mainWindow = application?.MainWindow;
                        Window[] windows = application?.Windows.Cast<Window>().ToArray() ?? Array.Empty<Window>();
                        completion.TrySetResult(new RuntimeHealthUiProbeResult(
                            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                            mainWindow?.WindowState.ToString() ?? "Unknown",
                            mainWindow?.IsVisible ?? false,
                            mainWindow?.IsActive ?? false,
                            windows.Count(window => window is SecurityChartWindow),
                            windows.Count(window => window is ManualDataEntryWindow),
                            windows.Count(window => window is RiskCenterWindow)));
                    }
                    catch
                    {
                        completion.TrySetResult(null);
                    }
                }));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                operation?.Abort();
            }
            catch
            {
            }

            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

public sealed class RuntimeHealthMonitor : IDisposable
{
    public static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultDispatcherProbeInterval = TimeSpan.FromSeconds(5);

    private readonly string _applicationVersion;
    private readonly RuntimeHealthFileStore _fileStore;
    private readonly RuntimeHealthReportExporter _reportExporter;
    private readonly RuntimeHealthEvaluator _evaluator;
    private readonly IRuntimeHealthMetricsProvider _metricsProvider;
    private readonly IRuntimeHealthDispatcherProbe _dispatcherProbe;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _probeInterval;
    private readonly int _processId;
    private readonly object _lifecycleGate = new();
    private readonly object _stateGate = new();
    private readonly List<RuntimeHealthSnapshot> _memoryHistory = new();

    private CancellationTokenSource? _cancellation;
    private Task? _samplingLoop;
    private Task? _probeLoop;
    private RuntimeHealthEvaluationState _evaluationState = RuntimeHealthEvaluationState.Initial;
    private RuntimeHealthSnapshot? _currentSnapshot;
    private RuntimeHealthUiProbeResult? _latestProbe;
    private double _maximumDispatcherLagSinceLastSample;
    private DateTimeOffset? _lastRefreshStartedAt;
    private DateTimeOffset? _lastRefreshCompletedAt;
    private double? _lastRefreshDurationMilliseconds;
    private bool _lastRefreshSucceeded = true;
    private int _consecutiveRefreshFailures;
    private bool _refreshRunning;
    private bool _started;
    private volatile bool _shutdownRequested;
    private bool _disposed;
    private int _samplingInProgress;
    private int _samplingWriteErrorCount;
    private int _samplingLoopStartCount;
    private int _probeLoopStartCount;
    private int _sampleAttemptCount;

    public RuntimeHealthMonitor(
        string applicationVersion,
        RuntimeHealthFileStore fileStore,
        RuntimeHealthReportExporter reportExporter,
        RuntimeHealthEvaluator evaluator,
        IRuntimeHealthMetricsProvider metricsProvider,
        IRuntimeHealthDispatcherProbe dispatcherProbe,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? sampleInterval = null,
        TimeSpan? probeInterval = null,
        int? processId = null)
    {
        _applicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _reportExporter = reportExporter ?? throw new ArgumentNullException(nameof(reportExporter));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _metricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));
        _dispatcherProbe = dispatcherProbe ?? throw new ArgumentNullException(nameof(dispatcherProbe));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
        _probeInterval = probeInterval ?? DefaultDispatcherProbeInterval;
        _processId = processId ?? Environment.ProcessId;
        if (_sampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        }

        if (_probeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(probeInterval));
        }
    }

    public event EventHandler<RuntimeHealthSnapshotEventArgs>? SnapshotAvailable;

    public string HealthDirectory => _fileStore.HealthDirectory;

    public RuntimeHealthSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _currentSnapshot;
            }
        }
    }

    internal int SamplingLoopStartCount => Volatile.Read(ref _samplingLoopStartCount);

    internal int ProbeLoopStartCount => Volatile.Read(ref _probeLoopStartCount);

    internal int SampleAttemptCount => Volatile.Read(ref _sampleAttemptCount);

    internal bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _started && !_shutdownRequested && !_disposed;
            }
        }
    }

    public static RuntimeHealthMonitor CreateDefault(string applicationVersion, Dispatcher dispatcher)
    {
        string healthDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalDatabase.AppFolderName,
            "health");
        var fileStore = new RuntimeHealthFileStore(healthDirectory);
        return new RuntimeHealthMonitor(
            applicationVersion,
            fileStore,
            new RuntimeHealthReportExporter(fileStore),
            new RuntimeHealthEvaluator(),
            new ProcessRuntimeHealthMetricsProvider(),
            new WpfRuntimeHealthDispatcherProbe(dispatcher));
    }

    public bool Start()
    {
        lock (_lifecycleGate)
        {
            if (_started || _shutdownRequested || _disposed)
            {
                return false;
            }

            _started = true;
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            Interlocked.Increment(ref _samplingLoopStartCount);
            Interlocked.Increment(ref _probeLoopStartCount);
            _samplingLoop = Task.Run(() => RunSamplingLoopAsync(token), CancellationToken.None);
            _probeLoop = Task.Run(() => RunProbeLoopAsync(token), CancellationToken.None);
            return true;
        }
    }

    public void NotifyUiRefreshStarted()
    {
        try
        {
            lock (_stateGate)
            {
                _lastRefreshStartedAt = SafeNow();
                _refreshRunning = true;
            }
        }
        catch
        {
            // Health observation must not alter the existing refresh behavior.
        }
    }

    public void NotifyUiRefreshCompleted(bool succeeded, TimeSpan elapsed)
    {
        try
        {
            lock (_stateGate)
            {
                _lastRefreshCompletedAt = SafeNow();
                _lastRefreshDurationMilliseconds = Math.Max(0, elapsed.TotalMilliseconds);
                _lastRefreshSucceeded = succeeded;
                _consecutiveRefreshFailures = succeeded ? 0 : _consecutiveRefreshFailures + 1;
                _refreshRunning = false;
            }
        }
        catch
        {
            // Health observation must not alter the existing refresh behavior.
        }
    }

    public Task<RuntimeHealthReportExportResult> ExportLast24HoursAsync(CancellationToken cancellationToken = default)
        => _reportExporter.ExportLast24HoursAsync(_applicationVersion, _processId, cancellationToken);

    public async Task StopAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        CancellationTokenSource? cancellation;
        Task[] loops;
        lock (_lifecycleGate)
        {
            if (_shutdownRequested)
            {
                return;
            }

            _shutdownRequested = true;
            if (!_started)
            {
                return;
            }

            cancellation = _cancellation;
            loops = new[] { _samplingLoop, _probeLoop }.Where(task => task is not null).Cast<Task>().ToArray();
        }

        var stopwatch = Stopwatch.StartNew();
        cancellation?.Cancel();
        if (loops.Length > 0)
        {
            TimeSpan wait = timeout - stopwatch.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.WhenAny(Task.WhenAll(loops), Task.Delay(wait)).ConfigureAwait(false);
            }
        }

        TimeSpan remaining = timeout - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            using var finalCancellation = new CancellationTokenSource(remaining);
            await TrySampleOnceAsync(finalCancellation.Token, allowDuringShutdown: true).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownRequested = true;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }

        GC.SuppressFinalize(this);
    }

    internal Task SampleNowForTestAsync(CancellationToken cancellationToken = default)
        => TrySampleOnceAsync(cancellationToken, allowDuringShutdown: false);

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TrySampleOnceAsync(cancellationToken, allowDuringShutdown: false).ConfigureAwait(false);
                await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _fileStore.TryWriteError("SAMPLING_LOOP", ex.Message, SafeNow());
                await DelayAfterFailureAsync(_sampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunProbeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RuntimeHealthUiProbeResult? probe = await _dispatcherProbe
                    .ProbeAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (probe is not null)
                {
                    lock (_stateGate)
                    {
                        _latestProbe = probe;
                        _maximumDispatcherLagSinceLastSample = Math.Max(
                            _maximumDispatcherLagSinceLastSample,
                            probe.DispatcherLagMilliseconds);
                    }
                }

                await Task.Delay(_probeInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _fileStore.TryWriteError("DISPATCHER_PROBE", ex.Message, SafeNow());
                await DelayAfterFailureAsync(_probeInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TrySampleOnceAsync(CancellationToken cancellationToken, bool allowDuringShutdown)
    {
        if ((!allowDuringShutdown && _shutdownRequested)
            || Interlocked.CompareExchange(ref _samplingInProgress, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Increment(ref _sampleAttemptCount);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            DateTimeOffset timestamp = SafeNow();
            RuntimeHealthProcessMetrics processMetrics;
            string? monitoringError = null;
            try
            {
                processMetrics = _metricsProvider.Capture();
            }
            catch (Exception ex)
            {
                monitoringError = "进程资源指标读取失败";
                _fileStore.TryWriteError("PROCESS_METRICS", ex.Message, timestamp);
                processMetrics = new RuntimeHealthProcessMetrics(
                    timestamp,
                    0,
                    0,
                    0,
                    0,
                    0,
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2),
                    0,
                    monitoringError);
            }

            RuntimeHealthUiProbeResult probe;
            double maximumDispatcherLag;
            RuntimeHealthRefreshState refresh;
            RuntimeHealthSnapshot[] history;
            lock (_stateGate)
            {
                probe = _latestProbe ?? new RuntimeHealthUiProbeResult(0, "Unknown", false, false, 0, 0, 0);
                maximumDispatcherLag = Math.Max(probe.DispatcherLagMilliseconds, _maximumDispatcherLagSinceLastSample);
                _maximumDispatcherLagSinceLastSample = 0;
                double runningSeconds = _refreshRunning && _lastRefreshStartedAt.HasValue
                    ? Math.Max(0, (timestamp - _lastRefreshStartedAt.Value).TotalSeconds)
                    : 0;
                refresh = new RuntimeHealthRefreshState(
                    _lastRefreshStartedAt,
                    _lastRefreshCompletedAt,
                    _lastRefreshDurationMilliseconds,
                    _lastRefreshSucceeded,
                    _consecutiveRefreshFailures,
                    _refreshRunning,
                    runningSeconds);
                DateTimeOffset historyStart = timestamp - TimeSpan.FromHours(2);
                _memoryHistory.RemoveAll(sample => sample.Timestamp < historyStart);
                history = _memoryHistory.ToArray();
            }

            RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(
                history,
                timestamp,
                processMetrics.PrivateMemoryBytes);
            RuntimeHealthEvaluationResult evaluation = _evaluator.Evaluate(
                new RuntimeHealthEvaluationInput(
                    probe.DispatcherLagMilliseconds,
                    maximumDispatcherLag,
                    processMetrics.PrivateMemoryBytes,
                    trend.Change30MinutesBytes,
                    processMetrics.ThreadCount,
                    processMetrics.HandleCount,
                    refresh.UiRefreshCurrentlyRunning,
                    refresh.UiRefreshRunningSeconds,
                    monitoringError is not null),
                _evaluationState,
                timestamp);
            _evaluationState = evaluation.NextState;

            var snapshot = new RuntimeHealthSnapshot
            {
                Timestamp = timestamp,
                ApplicationVersion = _applicationVersion,
                ProcessId = _processId,
                ProcessStartTime = processMetrics.ProcessStartTime,
                UptimeSeconds = Math.Max(0, (timestamp - processMetrics.ProcessStartTime).TotalSeconds),
                HealthStatus = evaluation.Status,
                HealthReasons = evaluation.Reasons,
                StatusTransition = evaluation.Transition is null
                    ? null
                    : $"{evaluation.Transition.From} -> {evaluation.Transition.To}",
                WorkingSetBytes = processMetrics.WorkingSetBytes,
                PrivateMemoryBytes = processMetrics.PrivateMemoryBytes,
                ManagedHeapBytes = processMetrics.ManagedHeapBytes,
                PrivateMemoryChange30MinutesBytes = trend.Change30MinutesBytes,
                PrivateMemoryChange60MinutesBytes = trend.Change60MinutesBytes,
                ThreadCount = processMetrics.ThreadCount,
                HandleCount = processMetrics.HandleCount,
                Gen0CollectionCount = processMetrics.Gen0CollectionCount,
                Gen1CollectionCount = processMetrics.Gen1CollectionCount,
                Gen2CollectionCount = processMetrics.Gen2CollectionCount,
                TotalProcessorTimeMilliseconds = processMetrics.TotalProcessorTimeMilliseconds,
                DispatcherLagMilliseconds = probe.DispatcherLagMilliseconds,
                MaximumDispatcherLagSinceLastSample = maximumDispatcherLag,
                MainWindowState = probe.MainWindowState,
                MainWindowIsVisible = probe.MainWindowIsVisible,
                MainWindowIsActive = probe.MainWindowIsActive,
                LastUiRefreshStartedAt = refresh.LastUiRefreshStartedAt,
                LastUiRefreshCompletedAt = refresh.LastUiRefreshCompletedAt,
                LastUiRefreshDurationMilliseconds = refresh.LastUiRefreshDurationMilliseconds,
                LastUiRefreshSucceeded = refresh.LastUiRefreshSucceeded,
                ConsecutiveUiRefreshFailures = refresh.ConsecutiveUiRefreshFailures,
                UiRefreshCurrentlyRunning = refresh.UiRefreshCurrentlyRunning,
                UiRefreshRunningSeconds = refresh.UiRefreshRunningSeconds,
                OpenChartWindowCount = probe.OpenChartWindowCount,
                OpenManualEntryWindowCount = probe.OpenManualEntryWindowCount,
                OpenRiskCenterWindowCount = probe.OpenRiskCenterWindowCount,
                HealthSamplingInProgress = true,
                ApplicationShutdownRequested = _shutdownRequested,
                SamplingWriteErrorCount = Volatile.Read(ref _samplingWriteErrorCount),
                SampleDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                MonitoringError = monitoringError
            };

            RuntimeHealthFileWriteResult writeResult = await _fileStore
                .AppendSnapshotAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
            if (!writeResult.Success)
            {
                Interlocked.Increment(ref _samplingWriteErrorCount);
                snapshot = snapshot with
                {
                    HealthReasons = snapshot.HealthReasons.Append("健康采样文件写入失败").Distinct().ToArray(),
                    SamplingWriteErrorCount = Volatile.Read(ref _samplingWriteErrorCount),
                    MonitoringError = "健康采样文件写入失败"
                };
            }

            stopwatch.Stop();
            snapshot = snapshot with { SampleDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds };
            lock (_stateGate)
            {
                _currentSnapshot = snapshot;
                _memoryHistory.Add(snapshot);
            }

            RaiseSnapshotAvailable(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _fileStore.TryWriteError("SAMPLE", ex.Message, SafeNow());
        }
        finally
        {
            Volatile.Write(ref _samplingInProgress, 0);
        }
    }

    private void RaiseSnapshotAvailable(RuntimeHealthSnapshot snapshot)
    {
        EventHandler<RuntimeHealthSnapshotEventArgs>? handlers = SnapshotAvailable;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<RuntimeHealthSnapshotEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, new RuntimeHealthSnapshotEventArgs(snapshot));
            }
            catch (Exception ex)
            {
                _fileStore.TryWriteError("SNAPSHOT_SUBSCRIBER", ex.Message, snapshot.Timestamp);
            }
        }
    }

    private DateTimeOffset SafeNow()
    {
        try
        {
            return _clock();
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }

    private static async Task DelayAfterFailureAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
