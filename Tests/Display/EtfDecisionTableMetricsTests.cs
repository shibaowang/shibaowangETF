using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class EtfDecisionTableMetricsTests
{
    [Fact]
    public void CalculatePremiumRate_UsesPriceAndIopv()
    {
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.05,
            Iopv = 1.00
        };

        Assert.Equal(0.05, EtfDecisionTableMetrics.CalculatePremiumRate(quote)!.Value, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculatePremiumRate_ReturnsEmptyWhenIopvMissingOrInvalid(double iopv)
    {
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.05,
            Iopv = iopv
        };

        Assert.Null(EtfDecisionTableMetrics.CalculatePremiumRate(quote));
    }

    [Fact]
    public void CalculateCompositeCost_UsesMarketReplayAndOtcReplayCost()
    {
        var replayPositions = new[]
        {
            new PositionReplayStateRecord { StrategyCode = "159941", Source = "场内ETF", CostAmount = 1000 },
            new PositionReplayStateRecord { StrategyCode = "159941", Source = "场外替代", CostAmount = 5000 }
        };
        var otcPositions = new[]
        {
            new OtcPositionReplayStateRecord { StrategyCode = "159941", CostAmount = 600 }
        };

        Assert.Equal(1600, EtfDecisionTableMetrics.CalculateCompositeCost(replayPositions, otcPositions), 2);
    }

    [Fact]
    public void CalculatePositionCostMetrics_UsesAverageCostForCompositeCostDisplay()
    {
        var replayPositions = new[]
        {
            new PositionReplayStateRecord
            {
                StrategyCode = "159941",
                Source = "场内ETF",
                Quantity = 3900,
                CostAmount = 5809,
                AverageCost = 5809.0 / 3900.0
            }
        };

        EtfPositionCostMetrics metrics = EtfDecisionTableMetrics.CalculatePositionCostMetrics(
            replayPositions,
            Array.Empty<OtcPositionReplayStateRecord>());

        Assert.Equal(3900, metrics.TotalQuantity, 4);
        Assert.Equal(5809, metrics.TotalCostAmount, 2);
        Assert.Equal(5809.0 / 3900.0, metrics.AverageCost, 6);
        Assert.NotEqual(metrics.TotalCostAmount, metrics.AverageCost);
    }

    [Fact]
    public void CalculatePrincipalRatio_UsesTotalCostAmountInsteadOfAverageCost()
    {
        double? ratio = EtfDecisionTableMetrics.CalculatePrincipalRatio(5809, 100000);

        Assert.Equal(5809.0 / 100000.0, ratio!.Value, 8);
        Assert.NotEqual(1.489 / 100000.0, ratio.Value, 8);
    }

    [Fact]
    public void CalculateHoldingPnlAndReturn_UseTotalCostAmountInsteadOfAverageCost()
    {
        double? pnl = EtfDecisionTableMetrics.CalculateHoldingPnl(6500, 5809);
        double? returnRate = EtfDecisionTableMetrics.CalculateHoldingReturnRate(pnl, 5809);

        Assert.Equal(691, pnl!.Value, 4);
        Assert.Equal(691.0 / 5809.0, returnRate!.Value, 8);
        Assert.NotEqual(6500 - 1.489, pnl.Value, 4);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesEtfUpdatedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -297.00)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 15, 1, 0));

        Assert.Equal(-297.00, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesOtcUpdatedYesterday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("017091", "OTC", "2026-07-01 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 14, 22, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesOtcUpdatedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -213.80),
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(55.32, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotCarryTodayOtcIntoTomorrow()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 9, 0, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_RequiresValuationUpdateTime()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                ReceivedAt = string.Empty,
                QuoteTime = null
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_UsesReceivedAtBeforeQuoteTime()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                ReceivedAt = "2026-07-01 20:00:00",
                QuoteTime = "2026-07-02 00:00:00"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_UsesOtcActualFundCodeNotStrategyEtfQuote()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-01 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesOtcReplayStateWhenAggregateReplayMissing()
    {
        var replayPositions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -213.80)
        };
        var otcPositions = new[]
        {
            OtcPosition("159941", "017091", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            replayPositions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(55.32, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotDoubleCountOtcReplayState()
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
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            replayPositions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(269.12, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_MatchesSinaFundQuoteBySource()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "FUND",
                Source = "SINA_FUND",
                ReceivedAt = "2026-07-02 20:00:00",
                QuoteTime = "2026-07-01"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(269.12, dailyPnl!.Value, 2);
    }

    private static PositionReplayStateRecord ReplayPosition(string strategyCode, string actualCode, string source, double dailyPnl)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = source,
            Quantity = 100,
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

    private static MarketQuoteRecord Quote(string symbol, string marketType, string receivedAt)
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            ReceivedAt = receivedAt,
            QuoteTime = receivedAt
        };
}
