using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Diagnostics;

public sealed class NaturalDayPnlEvaluationTests
{
    [Fact]
    public void EvaluationItems_SumMatchesExistingAggregate()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", 31.20, quantity: 3900),
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", null, quantity: 777.31),
            ReplayPosition("159509", "017091", "\u573a\u5916\u66ff\u4ee3", 128.88, quantity: 43.50)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, "2026-07-03 15:00:00", "2026-07-03 15:01:00"),
            SinaFundQuote("000834", price: 6.3084, lastClose: 6.4131, quoteTime: "2026-07-02", receivedAt: "2026-07-03 19:55:38"),
            SinaFundQuote("017091", price: 2.8409, lastClose: 2.8755, quoteTime: "2026-07-01", receivedAt: "2026-07-03 19:55:38")
        };

        IReadOnlyList<NaturalDayPnlEvaluationItem> items = EtfDecisionTableMetrics.EvaluateNaturalDayValuationItems(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 20, 5, 0));
        double? aggregate = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 20, 5, 0));
        double itemSum = items
            .Where(item => item.IncludedToday)
            .Sum(item => item.IncludedAmount!.Value);

        Assert.Equal(aggregate!.Value, itemSum, 6);
    }

    [Fact]
    public void EvaluationItems_ExplainValidAndStaleEtfQuotes()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", 31.20, quantity: 3900),
            ReplayPosition("159513", "159513", "\u573a\u5185ETF", 18.60, quantity: 3100)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, "2026-07-08 10:30:00", "2026-07-08 10:30:00"),
            EtfQuote("159513", price: 1.776, lastClose: 1.770, "2026-07-07 15:00:00", "2026-07-08 09:30:00")
        };

        IReadOnlyList<NaturalDayPnlEvaluationItem> items = EtfDecisionTableMetrics.EvaluateNaturalDayValuationItems(
            positions,
            quotes,
            new DateTime(2026, 7, 8, 10, 31, 0));

        NaturalDayPnlEvaluationItem valid = Assert.Single(items.Where(item => item.ActualCode == "159941"));
        Assert.True(valid.IncludedToday);
        Assert.Equal("VALID_ETF_TODAY_QUOTE", valid.ReasonCode);
        Assert.Contains("ETF", valid.ReasonText, StringComparison.OrdinalIgnoreCase);

        NaturalDayPnlEvaluationItem stale = Assert.Single(items.Where(item => item.ActualCode == "159513"));
        Assert.False(stale.IncludedToday);
        Assert.Equal("ETF_STALE_QUOTE_REFETCHED", stale.ReasonCode);
    }

    [Fact]
    public void EvaluationItems_ExplainMissingQuoteTimeAndWeekendSinaFundRefetch()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", 31.20, quantity: 3900),
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", null, quantity: 777.31)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = null,
                ReceivedAt = "2026-07-04 09:30:00"
            },
            SinaFundQuote("000834", price: 6.3084, lastClose: 6.4131, quoteTime: "2026-07-02", receivedAt: "2026-07-04 21:19:00")
        };

        IReadOnlyList<NaturalDayPnlEvaluationItem> items = EtfDecisionTableMetrics.EvaluateNaturalDayValuationItems(
            positions,
            quotes,
            new DateTime(2026, 7, 4, 21, 19, 0));

        Assert.Equal("QUOTE_TIME_MISSING", Assert.Single(items.Where(item => item.ActualCode == "159941")).ReasonCode);
        Assert.Equal("SINA_FUND_WEEKEND_OLD_NAV_REFETCH", Assert.Single(items.Where(item => item.ActualCode == "000834")).ReasonCode);
        Assert.All(items, item => Assert.False(item.IncludedToday));
    }

    [Fact]
    public void EvaluationItems_MergeOtcReplayStateWhenAggregateAlreadyRepresentsSameEvent()
    {
        var replayPositions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var otcPositions = new[]
        {
            OtcPosition("159941", "017091", 269.12)
        };
        var quotes = new[]
        {
            SinaFundQuote("017091", price: 2.8409, lastClose: 2.8755, quoteTime: "2026-07-02", receivedAt: "2026-07-02 20:00:00")
        };

        IReadOnlyList<NaturalDayPnlEvaluationItem> items = EtfDecisionTableMetrics.EvaluateNaturalDayValuationItems(
            replayPositions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        NaturalDayPnlEvaluationItem item = Assert.Single(items);
        Assert.True(item.IncludedToday);
        Assert.Equal(269.12, item.IncludedAmount);
        Assert.Equal("159941", item.StrategyCode);
        Assert.Equal("017091", item.ActualCode);
    }

    private static PositionReplayStateRecord ReplayPosition(
        string strategyCode,
        string actualCode,
        string source,
        double? dailyPnl,
        double quantity = 100)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = source,
            Quantity = quantity,
            DailyPnl = dailyPnl
        };

    private static OtcPositionReplayStateRecord OtcPosition(string strategyCode, string actualCode, double dailyPnl)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Quantity = 100,
            DailyPnl = dailyPnl
        };

    private static MarketQuoteRecord EtfQuote(
        string symbol,
        double price,
        double lastClose,
        string quoteTime,
        string receivedAt)
        => new()
        {
            Symbol = symbol,
            DisplayName = symbol,
            MarketType = "ETF",
            Source = "TENCENT_QT",
            Price = price,
            LastClose = lastClose,
            QuoteTime = quoteTime,
            ReceivedAt = receivedAt
        };

    private static MarketQuoteRecord SinaFundQuote(
        string symbol,
        double price,
        double lastClose,
        string quoteTime,
        string receivedAt)
        => new()
        {
            Symbol = symbol,
            DisplayName = symbol,
            MarketType = "OTC",
            Source = "SINA_FUND",
            Price = price,
            LastClose = lastClose,
            QuoteTime = quoteTime,
            ReceivedAt = receivedAt
        };
}
