using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class GlobalMarketRequestSchedulerTests
{
    [Fact]
    public void QuoteLanes_DoNotConsumeNonQuoteTickBudget()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfQuote, null, now, out _));
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, now, out _));
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayActive, "159941", now, out _));
        Assert.Equal(1, scheduler.NonQuoteRequestsThisTick);
    }

    [Fact]
    public void NonQuoteRequests_AreLimitedToOnePerGlobalTick()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayActive, "159941", now, out _));
        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "100.NDX100", now, out MarketRequestDecision denied));
        Assert.Equal("non_quote_tick_budget_exhausted", denied.Reason);
    }

    [Fact]
    public void NewTick_AllowsAnotherNonQuoteRequestAfterSymbolCooldown()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "100.NDX100", now, out _));

        DateTimeOffset later = now.AddMinutes(2);
        scheduler.BeginTick(later);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", later, out _));
    }

    [Fact]
    public void TencentDailyKLine_IsCooledForOneDayPerSymbol()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "159941", now, out _));

        DateTimeOffset oneHourLater = now.AddHours(1);
        scheduler.BeginTick(oneHourLater);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "159941", oneHourLater, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
    }

    [Fact]
    public void EtfQuote_AshareTradingSessionUsesTwoToFourSecondLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfQuote, null, now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void EtfQuote_LunchOrClosedSessionIsNotTwoToFourSecondLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T12:00:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfQuote, null, now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 60, 300.1);
    }

    [Fact]
    public void IndexQuote_UsesUsEasternTradingSessionForHighFrequencyLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void IndexQuote_ReleasesNonTradingThrottleAtUsOpen()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:28:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, preOpen, out MarketRequestDecision preOpenDecision));
        Assert.Equal(preOpen.AddMinutes(5), preOpenDecision.NextAllowedAt);

        DateTimeOffset usOpen = DateTimeOffset.Parse("2026-06-25T21:30:00+08:00");
        scheduler.BeginTick(usOpen);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, usOpen, out MarketRequestDecision openDecision));
        TimeSpan interval = openDecision.NextAllowedAt!.Value - usOpen;
        Assert.InRange(interval.TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void IndexQuote_KeepsNonTradingThrottleBeforeUsOpen()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:28:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, preOpen, out _));

        DateTimeOffset stillPreOpen = DateTimeOffset.Parse("2026-06-25T21:29:00+08:00");
        scheduler.BeginTick(stillPreOpen);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, stillPreOpen, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
        Assert.Equal(preOpen.AddMinutes(5), denied.NextAllowedAt);
    }

    [Fact]
    public void IndexQuote_AfterUsOpenReleaseStillRateLimitsItself()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:28:00+08:00");
        DateTimeOffset usOpen = DateTimeOffset.Parse("2026-06-25T21:30:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, preOpen, out _));
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, usOpen, out _));

        DateTimeOffset oneSecondLater = usOpen.AddSeconds(1);
        scheduler.BeginTick(oneSecondLater);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, oneSecondLater, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
        Assert.InRange((denied.NextAllowedAt!.Value - usOpen).TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void IndexQuote_UsOpenReleaseDoesNotReleaseFailureCooldown()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:28:00+08:00");
        DateTimeOffset? cooldown = scheduler.RecordFailure(
            MarketRequestKind.IndexQuote,
            null,
            "ResponseEnded",
            preOpen);
        Assert.NotNull(cooldown);

        DateTimeOffset usOpen = DateTimeOffset.Parse("2026-06-25T21:30:00+08:00");
        scheduler.BeginTick(usOpen);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, usOpen, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
        Assert.True(denied.NextAllowedAt >= preOpen.AddMinutes(10));
    }

    [Fact]
    public void IndexQuote_UsOpenReleaseDoesNotReleaseIndexIntradayThrottle()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:29:30+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", preOpen, out _));

        DateTimeOffset usOpen = DateTimeOffset.Parse("2026-06-25T21:30:00+08:00");
        scheduler.BeginTick(usOpen);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", usOpen, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
        Assert.True(denied.NextAllowedAt > usOpen);
    }

    [Fact]
    public void IndexQuote_UsOpenReleaseDoesNotReleaseIndexDailyHistoryThrottle()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset preOpen = DateTimeOffset.Parse("2026-06-25T21:29:30+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexDailyHistory, "251.NDXTMC", preOpen, out _));

        DateTimeOffset usOpen = DateTimeOffset.Parse("2026-06-25T21:30:00+08:00");
        scheduler.BeginTick(usOpen);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexDailyHistory, "251.NDXTMC", usOpen, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
        Assert.True(denied.NextAllowedAt > usOpen);
    }

    [Fact]
    public void IndexTrends2Lane_DoesNotBlockIndexQuoteLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", now, out MarketRequestDecision intraday));
        Assert.InRange((intraday.NextAllowedAt!.Value - now).TotalSeconds, 60, 120.1);

        DateTimeOffset quoteTime = now.AddSeconds(5);
        scheduler.BeginTick(quoteTime);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, quoteTime, out MarketRequestDecision quote));
        Assert.InRange((quote.NextAllowedAt!.Value - quoteTime).TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void IndexHistoryCooldown_DoesNotBlockIndexQuoteLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        DateTimeOffset? cooldown = scheduler.RecordFailure(
            MarketRequestKind.IndexDailyHistory,
            "251.NDXTMC",
            "ResponseEnded",
            now);
        Assert.NotNull(cooldown);

        DateTimeOffset quoteTime = now.AddMinutes(1);
        scheduler.BeginTick(quoteTime);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, quoteTime, out MarketRequestDecision quote));
        Assert.InRange((quote.NextAllowedAt!.Value - quoteTime).TotalSeconds, 2, 4.1);
    }

    [Fact]
    public void IndexQuoteLane_StillRateLimitsItself()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, now, out _));

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexQuote, null, now.AddSeconds(1), out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
    }

    [Fact]
    public void IndexTrends2Lane_StillRateLimitsItself()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", now, out _));

        DateTimeOffset nextTick = now.AddSeconds(5);
        scheduler.BeginTick(nextTick);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", nextTick, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
    }

    [Fact]
    public void SinaFundNav_EveningWindowUsesSixtyToOneTwentySecondLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T20:00:00+08:00");

        Assert.True(scheduler.TryAcquire(MarketRequestKind.SinaFundNav, null, now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 60, 120.1);
    }

    [Fact]
    public void EtfMinuteQuery_UsesThirtyToSixtySecondLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayActive, "159941", now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 30, 60.1);
    }

    [Fact]
    public void EtfMinuteQuery_RollsDifferentSymbolsAcrossTicksWithoutSixtySecondEndpointBlock()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayBackground, "159941", now, out _));

        DateTimeOffset nextTick = now.AddSeconds(5);
        scheduler.BeginTick(nextTick);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayBackground, "513300", nextTick, out _));
    }

    [Fact]
    public void EtfMinuteQuery_KeepsSameSymbolAtSixtySecondBackgroundCooldown()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfIntradayBackground, "159941", now, out _));

        DateTimeOffset nextTick = now.AddSeconds(5);
        scheduler.BeginTick(nextTick);

        Assert.False(scheduler.TryAcquire(MarketRequestKind.EtfIntradayBackground, "159941", nextTick, out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
    }

    [Fact]
    public void TencentDailyKLine_DifferentSymbolCanRunAfterEndpointCooldown()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "159941", now, out _));

        DateTimeOffset later = now.AddSeconds(31);
        scheduler.BeginTick(later);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "513300", later, out _));
    }

    [Fact]
    public void IndexTrends2_UsesSixtyToOneTwentySecondLane()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "251.NDXTMC", now, out MarketRequestDecision decision));

        TimeSpan interval = decision.NextAllowedAt!.Value - now;
        Assert.InRange(interval.TotalSeconds, 60, 120.1);
    }

    [Fact]
    public void ResponseEndedFailure_OpensLongCooldown()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        DateTimeOffset? until = scheduler.RecordFailure(MarketRequestKind.IndexIntraday, "100.NDX100", "ResponseEnded", now);

        Assert.NotNull(until);
        scheduler.BeginTick(now.AddMinutes(1));
        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "100.NDX100", now.AddMinutes(1), out MarketRequestDecision denied));
        Assert.True(denied.NextAllowedAt >= now.AddMinutes(10));
    }

    [Theory]
    [InlineData("ResponseEnded")]
    [InlineData("HTTP 403")]
    [InlineData("HTTP 429")]
    public void BlockingErrors_StillOpenCooldownWithinTheirLane(string error)
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-25T22:00:00+08:00");

        DateTimeOffset? until = scheduler.RecordFailure(MarketRequestKind.IndexIntraday, "100.NDX100", error, now);

        Assert.NotNull(until);
        scheduler.BeginTick(now.AddMinutes(1));
        Assert.False(scheduler.TryAcquire(MarketRequestKind.IndexIntraday, "100.NDX100", now.AddMinutes(1), out MarketRequestDecision denied));
        Assert.True(denied.NextAllowedAt >= now.AddMinutes(10));
    }

    [Fact]
    public void RawHostLimiter_BlocksSecondRequestOnSameHostEvenForDifferentSymbol()
    {
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00");

        Assert.True(scheduler.TryAcquireRaw(
            "push2his.eastmoney.com",
            "kline/get",
            "100.NDX100",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            now,
            countsAgainstNonQuoteBudget: false,
            out _));

        Assert.False(scheduler.TryAcquireRaw(
            "push2his.eastmoney.com",
            "kline/get",
            "251.NDXTMC",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            now.AddSeconds(1),
            countsAgainstNonQuoteBudget: false,
            out MarketRequestDecision denied));
        Assert.Equal("rate_limited", denied.Reason);
    }

    [Fact]
    public void SmokeMode_IsGuardedInMainWindowBackgroundQueues()
    {
        string source = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml.cs"));

        Assert.Contains("RuntimeMode.IsSmokeMode()", source, StringComparison.Ordinal);
        Assert.Contains("QueueMarketRefresh", source, StringComparison.Ordinal);
        Assert.Contains("QueueChartRefresh", source, StringComparison.Ordinal);
        Assert.Contains("QueueAlertDeliveryIfNeeded", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void RuntimeMode_ParsesSmokeModeFlag(string? value, bool expected)
        => Assert.Equal(expected, RuntimeMode.IsSmokeMode(value));

    private static string FindWorkspaceFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Workspace file not found: " + Path.Combine(parts));
    }
}
