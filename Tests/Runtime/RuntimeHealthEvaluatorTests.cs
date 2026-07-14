using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Runtime;

public sealed class RuntimeHealthEvaluatorTests
{
    private readonly RuntimeHealthEvaluator _evaluator = new();
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void NormalMetrics_ReturnNormal()
    {
        RuntimeHealthEvaluationResult result = Evaluate(NormalInput(), RuntimeHealthEvaluationState.Initial);

        Assert.Equal(RuntimeHealthStatus.Normal, result.Status);
        Assert.Empty(result.Reasons);
        Assert.Null(result.Transition);
    }

    [Theory]
    [InlineData(2_000, 0)]
    [InlineData(2_500, 0)]
    [InlineData(0, 2_000)]
    [InlineData(0, 7_999)]
    public void DispatcherWarning_RequiresTwoConsecutiveSamples(double latestLag, double maximumLag)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with
        {
            DispatcherLagMilliseconds = latestLag,
            MaximumDispatcherLagSinceLastSample = maximumLag
        };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult second = Evaluate(input, first.NextState);

        Assert.Equal(RuntimeHealthStatus.Normal, first.Status);
        Assert.Equal(RuntimeHealthStatus.Warning, second.Status);
        Assert.NotNull(second.Transition);
    }

    [Theory]
    [InlineData(8_000, 0)]
    [InlineData(9_000, 0)]
    [InlineData(0, 8_000)]
    public void DispatcherCritical_IsImmediate(double latestLag, double maximumLag)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with
        {
            DispatcherLagMilliseconds = latestLag,
            MaximumDispatcherLagSinceLastSample = maximumLag
        };

        RuntimeHealthEvaluationResult result = Evaluate(input, RuntimeHealthEvaluationState.Initial);

        Assert.Equal(RuntimeHealthStatus.Critical, result.Status);
        Assert.NotNull(result.Transition);
    }

    [Theory]
    [InlineData(1_500L * 1024 * 1024, RuntimeHealthStatus.Warning)]
    [InlineData(3_000L * 1024 * 1024, RuntimeHealthStatus.Critical)]
    public void PrivateMemoryThresholds_AreApplied(long bytes, RuntimeHealthStatus expected)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { PrivateMemoryBytes = bytes };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult result = expected == RuntimeHealthStatus.Warning
            ? Evaluate(input, first.NextState)
            : first;

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(512L * 1024 * 1024, RuntimeHealthStatus.Warning)]
    [InlineData(1_024L * 1024 * 1024, RuntimeHealthStatus.Critical)]
    public void MemoryGrowthThresholds_AreApplied(long bytes, RuntimeHealthStatus expected)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { PrivateMemoryChange30MinutesBytes = bytes };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult result = expected == RuntimeHealthStatus.Warning
            ? Evaluate(input, first.NextState)
            : first;

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(250, RuntimeHealthStatus.Warning)]
    [InlineData(500, RuntimeHealthStatus.Critical)]
    public void ThreadThresholds_AreApplied(int count, RuntimeHealthStatus expected)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { ThreadCount = count };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult result = expected == RuntimeHealthStatus.Warning
            ? Evaluate(input, first.NextState)
            : first;

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(10_000, RuntimeHealthStatus.Warning)]
    [InlineData(20_000, RuntimeHealthStatus.Critical)]
    public void HandleThresholds_AreApplied(int count, RuntimeHealthStatus expected)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { HandleCount = count };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult result = expected == RuntimeHealthStatus.Warning
            ? Evaluate(input, first.NextState)
            : first;

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(30.001, RuntimeHealthStatus.Warning)]
    [InlineData(90.001, RuntimeHealthStatus.Critical)]
    public void RefreshRunningThresholds_AreApplied(double seconds, RuntimeHealthStatus expected)
    {
        RuntimeHealthEvaluationInput input = NormalInput() with
        {
            UiRefreshCurrentlyRunning = true,
            UiRefreshRunningSeconds = seconds
        };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult result = expected == RuntimeHealthStatus.Warning
            ? Evaluate(input, first.NextState)
            : first;

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void RefreshAtExactlyThirtySeconds_DoesNotWarn()
    {
        RuntimeHealthEvaluationInput input = NormalInput() with
        {
            UiRefreshCurrentlyRunning = true,
            UiRefreshRunningSeconds = 30
        };

        Assert.Equal(RuntimeHealthStatus.Normal, Evaluate(input, RuntimeHealthEvaluationState.Initial).Status);
    }

    [Fact]
    public void MonitoringError_DoesNotDirectlyBecomeCritical()
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { MonitoringError = true };

        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult second = Evaluate(input, first.NextState);

        Assert.Equal(RuntimeHealthStatus.Normal, first.Status);
        Assert.Equal(RuntimeHealthStatus.Warning, second.Status);
        Assert.NotEqual(RuntimeHealthStatus.Critical, second.Status);
    }

    [Fact]
    public void ThreeNormalSamples_AreRequiredToRecoverFromCritical()
    {
        var state = new RuntimeHealthEvaluationState(RuntimeHealthStatus.Critical, 0, 0);

        RuntimeHealthEvaluationResult first = Evaluate(NormalInput(), state);
        RuntimeHealthEvaluationResult second = Evaluate(NormalInput(), first.NextState);
        RuntimeHealthEvaluationResult third = Evaluate(NormalInput(), second.NextState);

        Assert.Equal(RuntimeHealthStatus.Critical, first.Status);
        Assert.Equal(RuntimeHealthStatus.Critical, second.Status);
        Assert.Equal(RuntimeHealthStatus.Normal, third.Status);
        Assert.NotNull(third.Transition);
    }

    [Fact]
    public void SameStatus_DoesNotRepeatTransition()
    {
        RuntimeHealthEvaluationInput input = NormalInput() with { PrivateMemoryBytes = 2_000L * 1024 * 1024 };
        RuntimeHealthEvaluationResult first = Evaluate(input, RuntimeHealthEvaluationState.Initial);
        RuntimeHealthEvaluationResult warning = Evaluate(input, first.NextState);
        RuntimeHealthEvaluationResult stillWarning = Evaluate(input, warning.NextState);

        Assert.NotNull(warning.Transition);
        Assert.Null(stillWarning.Transition);
    }

    [Fact]
    public void MissingThirtyMinuteHistory_DoesNotCalculateGrowth()
    {
        RuntimeHealthSnapshot sample = Snapshot(Now - TimeSpan.FromMinutes(20), 100);

        RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(new[] { sample }, Now, 150);

        Assert.Null(trend.Change30MinutesBytes);
        Assert.Null(trend.Change60MinutesBytes);
    }

    [Fact]
    public void ThirtyAndSixtyMinuteGrowth_UseTimeWindowBaselines()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now - TimeSpan.FromMinutes(70), 100),
            Snapshot(Now - TimeSpan.FromMinutes(35), 130),
            Snapshot(Now - TimeSpan.FromMinutes(10), 170)
        };

        RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(samples, Now, 200);

        Assert.Equal(70, trend.Change30MinutesBytes);
        Assert.Equal(100, trend.Change60MinutesBytes);
    }

    [Fact]
    public void MemoryDecrease_IsReportedAsNegativeGrowth()
    {
        RuntimeHealthSnapshot sample = Snapshot(Now - TimeSpan.FromMinutes(31), 500);

        RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(new[] { sample }, Now, 300);

        Assert.Equal(-200, trend.Change30MinutesBytes);
    }

    [Fact]
    public void TrendIncludesCurrentMinimumAndMaximum()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now - TimeSpan.FromMinutes(40), 300),
            Snapshot(Now - TimeSpan.FromMinutes(20), 500)
        };

        RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(samples, Now, 100);

        Assert.Equal(100, trend.MinimumPrivateMemoryBytes);
        Assert.Equal(500, trend.MaximumPrivateMemoryBytes);
    }

    [Fact]
    public void EvaluatorSource_DoesNotForceGarbageCollection()
    {
        string source = ReadRepositoryFile("Core", "Services", "RuntimeHealthEvaluator.cs");

        Assert.DoesNotContain("GC.Collect", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForPendingFinalizers", source, StringComparison.Ordinal);
    }

    private RuntimeHealthEvaluationResult Evaluate(
        RuntimeHealthEvaluationInput input,
        RuntimeHealthEvaluationState state)
        => _evaluator.Evaluate(input, state, Now);

    private static RuntimeHealthEvaluationInput NormalInput()
        => new(0, 0, 100 * 1024 * 1024, null, 20, 200, false, 0);

    private static RuntimeHealthSnapshot Snapshot(DateTimeOffset timestamp, long privateMemory)
        => new() { Timestamp = timestamp, PrivateMemoryBytes = privateMemory };

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
