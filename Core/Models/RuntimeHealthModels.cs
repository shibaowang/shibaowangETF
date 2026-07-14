namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public enum RuntimeHealthStatus
{
    Normal,
    Warning,
    Critical
}

public static class RuntimeHealthThresholds
{
    public const double DispatcherWarningMilliseconds = 2_000;
    public const double DispatcherCriticalMilliseconds = 8_000;
    public const long PrivateMemoryWarningBytes = 1_500L * 1024 * 1024;
    public const long PrivateMemoryCriticalBytes = 3_000L * 1024 * 1024;
    public const long MemoryGrowthWarningBytes = 512L * 1024 * 1024;
    public const long MemoryGrowthCriticalBytes = 1_024L * 1024 * 1024;
    public const int ThreadWarningCount = 250;
    public const int ThreadCriticalCount = 500;
    public const int HandleWarningCount = 10_000;
    public const int HandleCriticalCount = 20_000;
    public const double UiRefreshWarningSeconds = 30;
    public const double UiRefreshCriticalSeconds = 90;
    public const int WarningConfirmationSamples = 2;
    public const int NormalRecoverySamples = 3;
}

public sealed record RuntimeHealthProcessMetrics(
    DateTimeOffset ProcessStartTime,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedHeapBytes,
    int ThreadCount,
    int HandleCount,
    int Gen0CollectionCount,
    int Gen1CollectionCount,
    int Gen2CollectionCount,
    double TotalProcessorTimeMilliseconds,
    string? Error = null);

public sealed record RuntimeHealthUiProbeResult(
    double DispatcherLagMilliseconds,
    string MainWindowState,
    bool MainWindowIsVisible,
    bool MainWindowIsActive,
    int OpenChartWindowCount,
    int OpenManualEntryWindowCount,
    int OpenRiskCenterWindowCount);

public sealed record RuntimeHealthRefreshState(
    DateTimeOffset? LastUiRefreshStartedAt,
    DateTimeOffset? LastUiRefreshCompletedAt,
    double? LastUiRefreshDurationMilliseconds,
    bool LastUiRefreshSucceeded,
    int ConsecutiveUiRefreshFailures,
    bool UiRefreshCurrentlyRunning,
    double UiRefreshRunningSeconds);

public sealed record RuntimeHealthEvaluationInput(
    double DispatcherLagMilliseconds,
    double MaximumDispatcherLagSinceLastSample,
    long PrivateMemoryBytes,
    long? PrivateMemoryChange30MinutesBytes,
    int ThreadCount,
    int HandleCount,
    bool UiRefreshCurrentlyRunning,
    double UiRefreshRunningSeconds,
    bool MonitoringError = false);

public sealed record RuntimeHealthEvaluationState(
    RuntimeHealthStatus CurrentStatus,
    int ConsecutiveWarningSamples,
    int ConsecutiveNormalSamples)
{
    public static RuntimeHealthEvaluationState Initial { get; } = new(RuntimeHealthStatus.Normal, 0, 0);
}

public sealed record RuntimeHealthStatusTransition(
    DateTimeOffset Timestamp,
    RuntimeHealthStatus From,
    RuntimeHealthStatus To,
    string[] Reasons);

public sealed record RuntimeHealthEvaluationResult(
    RuntimeHealthStatus Status,
    string[] Reasons,
    RuntimeHealthEvaluationState NextState,
    RuntimeHealthStatusTransition? Transition);

public sealed record RuntimeHealthMemoryTrend(
    long StartPrivateMemoryBytes,
    long CurrentPrivateMemoryBytes,
    long MinimumPrivateMemoryBytes,
    long MaximumPrivateMemoryBytes,
    long? Change30MinutesBytes,
    long? Change60MinutesBytes);

public sealed record RuntimeHealthSnapshot
{
    public DateTimeOffset Timestamp { get; init; }

    public string ApplicationVersion { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public DateTimeOffset ProcessStartTime { get; init; }

    public double UptimeSeconds { get; init; }

    public RuntimeHealthStatus HealthStatus { get; init; }

    public string[] HealthReasons { get; init; } = Array.Empty<string>();

    public string? StatusTransition { get; init; }

    public long WorkingSetBytes { get; init; }

    public long PrivateMemoryBytes { get; init; }

    public long ManagedHeapBytes { get; init; }

    public long? PrivateMemoryChange30MinutesBytes { get; init; }

    public long? PrivateMemoryChange60MinutesBytes { get; init; }

    public int ThreadCount { get; init; }

    public int HandleCount { get; init; }

    public int Gen0CollectionCount { get; init; }

    public int Gen1CollectionCount { get; init; }

    public int Gen2CollectionCount { get; init; }

    public double TotalProcessorTimeMilliseconds { get; init; }

    public double DispatcherLagMilliseconds { get; init; }

    public double MaximumDispatcherLagSinceLastSample { get; init; }

    public string MainWindowState { get; init; } = "Unknown";

    public bool MainWindowIsVisible { get; init; }

    public bool MainWindowIsActive { get; init; }

    public DateTimeOffset? LastUiRefreshStartedAt { get; init; }

    public DateTimeOffset? LastUiRefreshCompletedAt { get; init; }

    public double? LastUiRefreshDurationMilliseconds { get; init; }

    public bool LastUiRefreshSucceeded { get; init; }

    public int ConsecutiveUiRefreshFailures { get; init; }

    public bool UiRefreshCurrentlyRunning { get; init; }

    public double UiRefreshRunningSeconds { get; init; }

    public int OpenChartWindowCount { get; init; }

    public int OpenManualEntryWindowCount { get; init; }

    public int OpenRiskCenterWindowCount { get; init; }

    public bool HealthSamplingInProgress { get; init; }

    public bool ApplicationShutdownRequested { get; init; }

    public int SamplingWriteErrorCount { get; init; }

    public double SampleDurationMilliseconds { get; init; }

    public string? MonitoringError { get; init; }
}

public sealed record RuntimeHealthFileWriteResult(
    bool Success,
    string? FilePath,
    string? Error);

public sealed record RuntimeHealthReport
{
    public string ApplicationVersion { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; }

    public DateTimeOffset RangeStart { get; init; }

    public DateTimeOffset RangeEnd { get; init; }

    public DateTimeOffset? SampleStartTime { get; init; }

    public DateTimeOffset? SampleEndTime { get; init; }

    public int SampleCount { get; init; }

    public int NormalSampleCount { get; init; }

    public int WarningSampleCount { get; init; }

    public int CriticalSampleCount { get; init; }

    public long MaximumWorkingSetBytes { get; init; }

    public long MaximumPrivateMemoryBytes { get; init; }

    public long StartingPrivateMemoryBytes { get; init; }

    public long CurrentPrivateMemoryBytes { get; init; }

    public long MinimumPrivateMemoryBytes { get; init; }

    public long MaximumManagedHeapBytes { get; init; }

    public int MaximumThreadCount { get; init; }

    public int MaximumHandleCount { get; init; }

    public double MaximumDispatcherLagMilliseconds { get; init; }

    public double AverageDispatcherLagMilliseconds { get; init; }

    public double MaximumUiRefreshDurationMilliseconds { get; init; }

    public long? PrivateMemoryChange30MinutesBytes { get; init; }

    public long? PrivateMemoryChange60MinutesBytes { get; init; }

    public string[] StatusTransitions { get; init; } = Array.Empty<string>();

    public int OpenChartWindowCount { get; init; }

    public int OpenManualEntryWindowCount { get; init; }

    public int OpenRiskCenterWindowCount { get; init; }

    public double UptimeSeconds { get; init; }

    public int SamplingWriteErrorCount { get; init; }

    public bool DetectedAbnormalExitEvidence { get; init; }
}

public sealed record RuntimeHealthReportExportResult(
    bool Success,
    string Message,
    string? JsonPath,
    string? TextPath,
    RuntimeHealthReport? Report);
