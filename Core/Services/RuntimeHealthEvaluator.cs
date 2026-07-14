using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class RuntimeHealthEvaluator
{
    public RuntimeHealthEvaluationResult Evaluate(
        RuntimeHealthEvaluationInput input,
        RuntimeHealthEvaluationState previousState,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previousState);

        string[] criticalReasons = BuildCriticalReasons(input);
        string[] warningReasons = BuildWarningReasons(input);
        RuntimeHealthStatus rawStatus = criticalReasons.Length > 0
            ? RuntimeHealthStatus.Critical
            : warningReasons.Length > 0 || input.MonitoringError
                ? RuntimeHealthStatus.Warning
                : RuntimeHealthStatus.Normal;

        RuntimeHealthStatus nextStatus;
        int warningSamples;
        int normalSamples;
        string[] reasons;

        if (rawStatus == RuntimeHealthStatus.Critical)
        {
            nextStatus = RuntimeHealthStatus.Critical;
            warningSamples = 0;
            normalSamples = 0;
            reasons = criticalReasons;
        }
        else if (rawStatus == RuntimeHealthStatus.Warning)
        {
            warningSamples = previousState.ConsecutiveWarningSamples + 1;
            normalSamples = 0;
            reasons = warningReasons.Length > 0
                ? warningReasons
                : new[] { "运行健康监测自身发生异常" };

            if (previousState.CurrentStatus == RuntimeHealthStatus.Warning)
            {
                nextStatus = RuntimeHealthStatus.Warning;
            }
            else if (warningSamples >= RuntimeHealthThresholds.WarningConfirmationSamples)
            {
                nextStatus = RuntimeHealthStatus.Warning;
            }
            else
            {
                nextStatus = previousState.CurrentStatus;
                reasons = reasons
                    .Append($"等待连续 {RuntimeHealthThresholds.WarningConfirmationSamples} 次采样确认")
                    .ToArray();
            }
        }
        else
        {
            warningSamples = 0;
            normalSamples = previousState.ConsecutiveNormalSamples + 1;
            if (previousState.CurrentStatus == RuntimeHealthStatus.Normal
                || normalSamples >= RuntimeHealthThresholds.NormalRecoverySamples)
            {
                nextStatus = RuntimeHealthStatus.Normal;
                reasons = Array.Empty<string>();
            }
            else
            {
                nextStatus = previousState.CurrentStatus;
                reasons = new[]
                {
                    $"等待连续 {RuntimeHealthThresholds.NormalRecoverySamples} 次正常采样恢复，当前 {normalSamples} 次"
                };
            }
        }

        var nextState = new RuntimeHealthEvaluationState(nextStatus, warningSamples, normalSamples);
        RuntimeHealthStatusTransition? transition = nextStatus == previousState.CurrentStatus
            ? null
            : new RuntimeHealthStatusTransition(
                timestamp,
                previousState.CurrentStatus,
                nextStatus,
                reasons);
        return new RuntimeHealthEvaluationResult(nextStatus, reasons, nextState, transition);
    }

    public static RuntimeHealthMemoryTrend CalculateMemoryTrend(
        IReadOnlyList<RuntimeHealthSnapshot> samples,
        DateTimeOffset currentTime,
        long currentPrivateMemoryBytes)
    {
        ArgumentNullException.ThrowIfNull(samples);
        RuntimeHealthSnapshot[] ordered = samples
            .Where(sample => sample.Timestamp <= currentTime)
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        long start = ordered.FirstOrDefault()?.PrivateMemoryBytes ?? currentPrivateMemoryBytes;
        long minimum = ordered.Length == 0
            ? currentPrivateMemoryBytes
            : Math.Min(currentPrivateMemoryBytes, ordered.Min(sample => sample.PrivateMemoryBytes));
        long maximum = ordered.Length == 0
            ? currentPrivateMemoryBytes
            : Math.Max(currentPrivateMemoryBytes, ordered.Max(sample => sample.PrivateMemoryBytes));

        return new RuntimeHealthMemoryTrend(
            start,
            currentPrivateMemoryBytes,
            minimum,
            maximum,
            CalculateWindowChange(ordered, currentTime, currentPrivateMemoryBytes, TimeSpan.FromMinutes(30)),
            CalculateWindowChange(ordered, currentTime, currentPrivateMemoryBytes, TimeSpan.FromMinutes(60)));
    }

    private static long? CalculateWindowChange(
        IReadOnlyList<RuntimeHealthSnapshot> samples,
        DateTimeOffset currentTime,
        long currentPrivateMemoryBytes,
        TimeSpan window)
    {
        DateTimeOffset cutoff = currentTime - window;
        RuntimeHealthSnapshot? baseline = samples
            .Where(sample => sample.Timestamp <= cutoff)
            .OrderByDescending(sample => sample.Timestamp)
            .FirstOrDefault();
        return baseline is null ? null : currentPrivateMemoryBytes - baseline.PrivateMemoryBytes;
    }

    private static string[] BuildCriticalReasons(RuntimeHealthEvaluationInput input)
    {
        var reasons = new List<string>();
        double dispatcherLag = Math.Max(input.DispatcherLagMilliseconds, input.MaximumDispatcherLagSinceLastSample);
        if (dispatcherLag >= RuntimeHealthThresholds.DispatcherCriticalMilliseconds)
        {
            reasons.Add($"Dispatcher 延迟达到 {dispatcherLag:F0} ms");
        }

        if (input.PrivateMemoryBytes >= RuntimeHealthThresholds.PrivateMemoryCriticalBytes)
        {
            reasons.Add("私有内存达到 3 GB 严重阈值");
        }

        if (input.PrivateMemoryChange30MinutesBytes >= RuntimeHealthThresholds.MemoryGrowthCriticalBytes)
        {
            reasons.Add("30 分钟私有内存增长达到 1 GB 严重阈值");
        }

        if (input.ThreadCount >= RuntimeHealthThresholds.ThreadCriticalCount)
        {
            reasons.Add($"线程数达到 {input.ThreadCount}");
        }

        if (input.HandleCount >= RuntimeHealthThresholds.HandleCriticalCount)
        {
            reasons.Add($"句柄数达到 {input.HandleCount}");
        }

        if (input.UiRefreshCurrentlyRunning
            && input.UiRefreshRunningSeconds > RuntimeHealthThresholds.UiRefreshCriticalSeconds)
        {
            reasons.Add($"主刷新已持续 {input.UiRefreshRunningSeconds:F0} 秒");
        }

        return reasons.ToArray();
    }

    private static string[] BuildWarningReasons(RuntimeHealthEvaluationInput input)
    {
        var reasons = new List<string>();
        double dispatcherLag = Math.Max(input.DispatcherLagMilliseconds, input.MaximumDispatcherLagSinceLastSample);
        if (dispatcherLag >= RuntimeHealthThresholds.DispatcherWarningMilliseconds
            && dispatcherLag < RuntimeHealthThresholds.DispatcherCriticalMilliseconds)
        {
            reasons.Add($"Dispatcher 延迟达到 {dispatcherLag:F0} ms");
        }

        if (input.PrivateMemoryBytes >= RuntimeHealthThresholds.PrivateMemoryWarningBytes
            && input.PrivateMemoryBytes < RuntimeHealthThresholds.PrivateMemoryCriticalBytes)
        {
            reasons.Add("私有内存达到 1.5 GB 警告阈值");
        }

        if (input.PrivateMemoryChange30MinutesBytes >= RuntimeHealthThresholds.MemoryGrowthWarningBytes
            && input.PrivateMemoryChange30MinutesBytes < RuntimeHealthThresholds.MemoryGrowthCriticalBytes)
        {
            reasons.Add("30 分钟私有内存增长达到 512 MB 警告阈值");
        }

        if (input.ThreadCount >= RuntimeHealthThresholds.ThreadWarningCount
            && input.ThreadCount < RuntimeHealthThresholds.ThreadCriticalCount)
        {
            reasons.Add($"线程数达到 {input.ThreadCount}");
        }

        if (input.HandleCount >= RuntimeHealthThresholds.HandleWarningCount
            && input.HandleCount < RuntimeHealthThresholds.HandleCriticalCount)
        {
            reasons.Add($"句柄数达到 {input.HandleCount}");
        }

        if (input.UiRefreshCurrentlyRunning
            && input.UiRefreshRunningSeconds > RuntimeHealthThresholds.UiRefreshWarningSeconds
            && input.UiRefreshRunningSeconds <= RuntimeHealthThresholds.UiRefreshCriticalSeconds)
        {
            reasons.Add($"主刷新已持续 {input.UiRefreshRunningSeconds:F0} 秒");
        }

        return reasons.ToArray();
    }
}
