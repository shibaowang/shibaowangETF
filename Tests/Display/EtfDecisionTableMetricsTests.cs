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
}
