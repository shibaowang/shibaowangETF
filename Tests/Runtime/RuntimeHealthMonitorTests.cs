using System.Collections.Concurrent;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Runtime;

public sealed class RuntimeHealthMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Sample_ContainsRequiredProcessAndRefreshFields()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();
        monitor.NotifyUiRefreshStarted();
        monitor.NotifyUiRefreshCompleted(true, TimeSpan.FromMilliseconds(123));

        await monitor.SampleNowForTestAsync();

        RuntimeHealthSnapshot snapshot = Assert.IsType<RuntimeHealthSnapshot>(monitor.CurrentSnapshot);
        Assert.Equal("V8.4.0", snapshot.ApplicationVersion);
        Assert.Equal(4242, snapshot.ProcessId);
        Assert.Equal(200, snapshot.WorkingSetBytes);
        Assert.Equal(180, snapshot.PrivateMemoryBytes);
        Assert.Equal(80, snapshot.ManagedHeapBytes);
        Assert.Equal(12, snapshot.ThreadCount);
        Assert.Equal(120, snapshot.HandleCount);
        Assert.Equal(123, snapshot.LastUiRefreshDurationMilliseconds);
        Assert.True(snapshot.LastUiRefreshSucceeded);
    }

    [Fact]
    public async Task ProcessMetricFailure_DegradesSafelyWithoutThrowing()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(metrics: new ThrowingMetricsProvider());

        await monitor.SampleNowForTestAsync();

        RuntimeHealthSnapshot snapshot = Assert.IsType<RuntimeHealthSnapshot>(monitor.CurrentSnapshot);
        Assert.Equal(0, snapshot.PrivateMemoryBytes);
        Assert.NotNull(snapshot.MonitoringError);
        Assert.NotEqual(RuntimeHealthStatus.Critical, snapshot.HealthStatus);
    }

    [Fact]
    public async Task RefreshFailureCounter_ResetsAfterSuccessfulRefresh()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();
        monitor.NotifyUiRefreshStarted();
        monitor.NotifyUiRefreshCompleted(false, TimeSpan.FromMilliseconds(10));
        monitor.NotifyUiRefreshStarted();
        monitor.NotifyUiRefreshCompleted(true, TimeSpan.FromMilliseconds(20));

        await monitor.SampleNowForTestAsync();

        Assert.Equal(0, monitor.CurrentSnapshot!.ConsecutiveUiRefreshFailures);
        Assert.True(monitor.CurrentSnapshot.LastUiRefreshSucceeded);
    }

    [Fact]
    public async Task CurrentRefresh_ReportsRunningDuration()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();
        monitor.NotifyUiRefreshStarted();
        environment.Advance(TimeSpan.FromSeconds(35));

        await monitor.SampleNowForTestAsync();

        Assert.True(monitor.CurrentSnapshot!.UiRefreshCurrentlyRunning);
        Assert.Equal(35, monitor.CurrentSnapshot.UiRefreshRunningSeconds);
    }

    [Fact]
    public async Task RepeatedStart_CreatesOnlyOneSamplingAndProbeLoop()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(
            sampleInterval: TimeSpan.FromMilliseconds(25),
            probeInterval: TimeSpan.FromMilliseconds(20));

        Assert.True(monitor.Start());
        Assert.False(monitor.Start());
        await Task.Delay(70);
        await monitor.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, monitor.SamplingLoopStartCount);
        Assert.Equal(1, monitor.ProbeLoopStartCount);
    }

    [Fact]
    public async Task ConcurrentSamples_DoNotOverlapOrQueue()
    {
        using var environment = new MonitorEnvironment();
        var metrics = new BlockingMetricsProvider();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(metrics: metrics);
        Task first = Task.Run(() => monitor.SampleNowForTestAsync());
        Assert.True(metrics.Entered.Wait(TimeSpan.FromSeconds(2)));

        await monitor.SampleNowForTestAsync();
        metrics.Release.Set();
        await first;

        Assert.Equal(1, metrics.CaptureCount);
        Assert.Equal(1, monitor.SampleAttemptCount);
    }

    [Fact]
    public async Task Stop_WritesShutdownMarkerAndReturnsWithinTimeout()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(
            sampleInterval: TimeSpan.FromMinutes(1),
            probeInterval: TimeSpan.FromMinutes(1));
        monitor.Start();
        await Task.Delay(30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await monitor.StopAsync(TimeSpan.FromMilliseconds(500));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(monitor.CurrentSnapshot!.ApplicationShutdownRequested);
        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndPreventsRestart()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();

        monitor.Dispose();
        monitor.Dispose();

        Assert.False(monitor.Start());
        await monitor.SampleNowForTestAsync();
        Assert.Null(monitor.CurrentSnapshot);
    }

    [Fact]
    public async Task NoSamplesContinueAfterStopCompletes()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(
            sampleInterval: TimeSpan.FromMilliseconds(15),
            probeInterval: TimeSpan.FromMilliseconds(15));
        monitor.Start();
        await Task.Delay(60);
        await monitor.StopAsync(TimeSpan.FromSeconds(1));
        int attemptsAfterStop = monitor.SampleAttemptCount;

        await Task.Delay(60);

        Assert.Equal(attemptsAfterStop, monitor.SampleAttemptCount);
    }

    [Fact]
    public async Task ClosedDispatcherProbe_IsHandledWithoutFailure()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor(dispatcher: new NullDispatcherProbe());

        await monitor.SampleNowForTestAsync();

        Assert.Equal("Unknown", monitor.CurrentSnapshot!.MainWindowState);
        Assert.Equal(0, monitor.CurrentSnapshot.OpenChartWindowCount);
    }

    [Fact]
    public async Task SubscriberFailure_DoesNotStopSampling()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();
        int successfulNotifications = 0;
        monitor.SnapshotAvailable += (_, _) => throw new InvalidOperationException("subscriber failed");
        monitor.SnapshotAvailable += (_, _) => successfulNotifications++;

        await monitor.SampleNowForTestAsync();

        Assert.Equal(1, successfulNotifications);
        Assert.NotNull(monitor.CurrentSnapshot);
    }

    [Fact]
    public async Task ExportFailure_DoesNotStopSubsequentSampling()
    {
        using var environment = new MonitorEnvironment(blockReportsDirectory: true);
        RuntimeHealthMonitor monitor = environment.CreateMonitor();

        RuntimeHealthReportExportResult report = await monitor.ExportLast24HoursAsync();
        await monitor.SampleNowForTestAsync();

        Assert.False(report.Success);
        Assert.NotNull(monitor.CurrentSnapshot);
    }

    [Fact]
    public async Task SnapshotEventContainsSameCurrentSnapshot()
    {
        using var environment = new MonitorEnvironment();
        RuntimeHealthMonitor monitor = environment.CreateMonitor();
        RuntimeHealthSnapshot? notified = null;
        monitor.SnapshotAvailable += (_, args) => notified = args.Snapshot;

        await monitor.SampleNowForTestAsync();

        Assert.Same(monitor.CurrentSnapshot, notified);
    }

    [Fact]
    public void ProcessProvider_DoesNotForceGarbageCollection()
    {
        string source = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs");

        Assert.Contains("GC.GetTotalMemory(forceFullCollection: false)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForPendingFinalizers", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", source, StringComparison.Ordinal);
    }

    private sealed class MonitorEnvironment : IDisposable
    {
        private DateTimeOffset _now = Now;
        private readonly string _directory;
        private readonly RuntimeHealthFileStore _store;

        public MonitorEnvironment(bool blockReportsDirectory = false)
        {
            _directory = Path.Combine(Path.GetTempPath(), "crossetf-runtime-monitor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _store = new RuntimeHealthFileStore(_directory);
            if (blockReportsDirectory)
            {
                File.WriteAllText(_store.ReportsDirectory, "blocked");
            }
        }

        public RuntimeHealthMonitor CreateMonitor(
            IRuntimeHealthMetricsProvider? metrics = null,
            IRuntimeHealthDispatcherProbe? dispatcher = null,
            TimeSpan? sampleInterval = null,
            TimeSpan? probeInterval = null)
            => new(
                "V8.4.0",
                _store,
                new RuntimeHealthReportExporter(_store, () => _now),
                new RuntimeHealthEvaluator(),
                metrics ?? new FakeMetricsProvider(),
                dispatcher ?? new FakeDispatcherProbe(),
                () => _now,
                sampleInterval ?? TimeSpan.FromMinutes(1),
                probeInterval ?? TimeSpan.FromMinutes(1),
                processId: 4242);

        public void Advance(TimeSpan elapsed) => _now += elapsed;

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeMetricsProvider : IRuntimeHealthMetricsProvider
    {
        public RuntimeHealthProcessMetrics Capture()
            => new(Now.AddHours(-1), 200, 180, 80, 12, 120, 1, 2, 3, 500);
    }

    private sealed class ThrowingMetricsProvider : IRuntimeHealthMetricsProvider
    {
        public RuntimeHealthProcessMetrics Capture() => throw new InvalidOperationException("metrics unavailable");
    }

    private sealed class BlockingMetricsProvider : IRuntimeHealthMetricsProvider
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public int CaptureCount { get; private set; }

        public RuntimeHealthProcessMetrics Capture()
        {
            CaptureCount++;
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(2));
            return new FakeMetricsProvider().Capture();
        }
    }

    private sealed class FakeDispatcherProbe : IRuntimeHealthDispatcherProbe
    {
        public Task<RuntimeHealthUiProbeResult?> ProbeAsync(CancellationToken cancellationToken)
            => Task.FromResult<RuntimeHealthUiProbeResult?>(new(5, "Normal", true, true, 2, 1, 1));
    }

    private sealed class NullDispatcherProbe : IRuntimeHealthDispatcherProbe
    {
        public Task<RuntimeHealthUiProbeResult?> ProbeAsync(CancellationToken cancellationToken)
            => Task.FromResult<RuntimeHealthUiProbeResult?>(null);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
