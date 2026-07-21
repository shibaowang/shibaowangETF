using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.AccountReplay;

public sealed class BrokerHoldingPnlCalculatorTests
{
    [Fact]
    public void Existing159941Cycle_MatchesBrokerDilutedHoldingPnl()
    {
        TradeLogRecord[] logs =
        {
            Trade(7, "2026-04-23", "买入", 4500, 6372.00, 0.76, -6372.76),
            Trade(8, "2026-04-27", "买入", 3400, 4892.60, 0.59, -4893.19),
            Trade(32, "2026-05-19", "买入", 600, 909.60, 0.11, -909.71),
            Trade(35, "2026-05-25", "卖出", 6200, 10161.80, 1.22, 10160.58),
            Trade(37, "2026-06-08", "买入", 600, 943.80, 0.11, -943.91),
            Trade(38, "2026-06-08", "买入", 1000, 1571.00, 0.19, -1571.19),
            Trade(65, "2026-07-21 09:40:55", "买入", 32000, 49888.00, 5.99, -49893.99),
            Trade(66, "2026-07-21 13:27:08", "卖出", 31600, 49959.60, 6.00, 49953.60)
        };
        PositionReplayStateRecord position = Position("159941", "159941", 4300, 6832.70);

        BrokerHoldingPnlMetrics metrics = Assert.IsType<BrokerHoldingPnlMetrics>(
            BrokerHoldingPnlCalculator.Calculate(
                "159941",
                logs,
                new[] { position },
                Array.Empty<OtcPositionReplayStateRecord>(),
                Array.Empty<MarketQuoteRecord>()));

        Assert.Equal(4470.57, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(4470.57, metrics.DilutedCostAmount, 2);
        Assert.Equal(1.03966744186047, metrics.DilutedAverageCost!.Value, 12);
        Assert.Equal(2362.13, metrics.BrokerHoldingPnl!.Value, 2);
        Assert.Equal(2362.13 / 4470.57, metrics.BrokerHoldingReturnRate!.Value, 12);
        Assert.Equal(4300, metrics.TotalQuantity, 4);
    }

    [Fact]
    public void FullCloseAndRebuy_StartsANewHoldingCycle()
    {
        TradeLogRecord[] logs =
        {
            Trade(1, "2026-01-01", "买入", 100, 1000, 1, -1001),
            Trade(2, "2026-01-02", "卖出", 100, 1200, 1, 1199),
            Trade(3, "2026-01-03", "买入", 50, 400, 1, -401)
        };

        BrokerHoldingPnlMetrics metrics = Calculate(logs, Position("159941", "159941", 50, 450));

        Assert.Equal(401, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(8.02, metrics.DilutedAverageCost!.Value, 4);
        Assert.Equal(49, metrics.BrokerHoldingPnl!.Value, 2);
    }

    [Fact]
    public void DividendReducesInvestmentAndCorporateActionOnlyChangesQuantity()
    {
        TradeLogRecord[] logs =
        {
            Trade(1, "2026-01-01", "买入", 1000, 1000, 0, -1000),
            Trade(2, "2026-01-02", "分红", 0, 100, 0, 100),
            Trade(3, "2026-01-03", "送股", 100, 0, 0, 0)
        };

        BrokerHoldingPnlMetrics metrics = Calculate(logs, Position("159941", "159941", 1100, 1100));

        Assert.Equal(900, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(1100, metrics.TotalQuantity, 4);
        Assert.Equal(200, metrics.BrokerHoldingPnl!.Value, 2);
    }

    [Fact]
    public void NonPositiveInvestment_DoesNotDivideForReturnRate()
    {
        TradeLogRecord[] logs =
        {
            Trade(1, "2026-01-01", "买入", 100, 100, 0, -100),
            Trade(2, "2026-01-02", "分红", 0, 150, 0, 150)
        };

        BrokerHoldingPnlMetrics metrics = Calculate(logs, Position("159941", "159941", 100, 120));

        Assert.Equal(-50, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(170, metrics.BrokerHoldingPnl!.Value, 2);
        Assert.Null(metrics.BrokerHoldingReturnRate);
    }

    [Fact]
    public void ExchangeAndMultipleOtcPositions_CombineByAmountWithoutMirrorDoubleCount()
    {
        TradeLogRecord[] logs =
        {
            Trade(1, "2026-01-01", "买入", 100, 100, 1, -101, "159513", "场内ETF", "159513"),
            Trade(2, "2026-01-02", "买入", 10, 200, 2, -202, "000834", "场外替代", "159513"),
            Trade(3, "2026-01-03", "买入", 20, 300, 3, -303, "008971", "场外替代", "159513")
        };
        PositionReplayStateRecord[] replay =
        {
            Position("159513", "159513", 100, 120, "场内ETF"),
            Position("159513", "000834", 10, 220, "场外替代"),
            Position("159513", "008971", 20, 330, "场外替代")
        };
        OtcPositionReplayStateRecord[] otcMirrors =
        {
            OtcPosition("159513", "000834", 10, 220),
            OtcPosition("159513", "008971", 20, 330)
        };

        BrokerHoldingPnlMetrics metrics = Assert.IsType<BrokerHoldingPnlMetrics>(
            BrokerHoldingPnlCalculator.Calculate(
                "159513",
                logs,
                replay,
                otcMirrors,
                Array.Empty<MarketQuoteRecord>()));

        Assert.Equal(606, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(670, metrics.MarketValue!.Value, 2);
        Assert.Equal(64, metrics.BrokerHoldingPnl!.Value, 2);
        Assert.Equal(64d / 606d, metrics.BrokerHoldingReturnRate!.Value, 12);
        Assert.Equal(3, metrics.ActiveInstrumentCount);
    }

    [Fact]
    public void OtcFullRedemptionAndResubscription_StartsNewCycle()
    {
        TradeLogRecord[] logs =
        {
            Trade(1, "2026-01-01", "买入", 100, 100, 0, -100, "017091", "场外替代"),
            Trade(2, "2026-01-02", "卖出", 100, 150, 0, 150, "017091", "场外替代"),
            Trade(3, "2026-01-03", "买入", 50, 60, 0, -60, "017091", "场外替代")
        };
        PositionReplayStateRecord replay = Position("159941", "017091", 50, 70, "场外替代");
        OtcPositionReplayStateRecord mirror = OtcPosition("159941", "017091", 50, 70);

        BrokerHoldingPnlMetrics metrics = Assert.IsType<BrokerHoldingPnlMetrics>(
            BrokerHoldingPnlCalculator.Calculate(
                "159941",
                logs,
                new[] { replay },
                new[] { mirror },
                Array.Empty<MarketQuoteRecord>()));

        Assert.Equal(60, metrics.OpenCycleNetInvestment, 2);
        Assert.Equal(10, metrics.BrokerHoldingPnl!.Value, 2);
        Assert.Equal(1, metrics.ActiveInstrumentCount);
    }

    private static BrokerHoldingPnlMetrics Calculate(
        IEnumerable<TradeLogRecord> logs,
        params PositionReplayStateRecord[] positions)
        => Assert.IsType<BrokerHoldingPnlMetrics>(BrokerHoldingPnlCalculator.Calculate(
            "159941",
            logs,
            positions,
            Array.Empty<OtcPositionReplayStateRecord>(),
            Array.Empty<MarketQuoteRecord>()));

    private static TradeLogRecord Trade(
        long id,
        string time,
        string action,
        double quantity,
        double amount,
        double fee,
        double netCashImpact,
        string actualCode = "159941",
        string source = "场内ETF",
        string strategyCode = "159941")
        => new()
        {
            Id = id,
            Time = time,
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Action = action,
            Source = source,
            Quantity = quantity,
            Amount = amount,
            Fee = fee,
            NetCashImpact = netCashImpact
        };

    private static PositionReplayStateRecord Position(
        string strategyCode,
        string actualCode,
        double quantity,
        double marketValue,
        string source = "场内ETF")
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = source,
            Quantity = quantity,
            MarketValue = marketValue
        };

    private static OtcPositionReplayStateRecord OtcPosition(
        string strategyCode,
        string actualCode,
        double quantity,
        double marketValue)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Quantity = quantity,
            MarketValue = marketValue
        };
}
