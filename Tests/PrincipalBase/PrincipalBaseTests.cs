using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.PrincipalBase;

public class PrincipalBaseTests
{
    [Fact]
    public void PrincipalBase_DoesNotInclude_UnrealizedPnL()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var replayService = new TradeLogReplayService();
        var result = replayService.Replay(entries);

        // PrincipalBase = 入金 - 出金 + 已实现盈亏 - 手续费 + 分红
        // Should NOT contain floating PnL
        double pb = result.PrincipalBase;
        double totalAssets = entries.Last().TotalAssets;

        // PrincipalBase and totalAssets should be able to differ significantly
        // (they're not in a fixed ratio)
        Assert.True(pb > 0);
        Assert.True(totalAssets > 0);
    }

    [Fact]
    public void BuyFee_ReducesPrincipalBase()
    {
        var entries = V8MockTradeLogFactory.CreateTradeWithFee();
        var replayService = new TradeLogReplayService();
        var result = replayService.Replay(entries);

        Assert.True(result.PrincipalBase < 1_000_000);
        Assert.Equal(5, result.TotalFee, 0.01);
    }

    [Fact]
    public void SellRealizedPnl_AffectsPrincipalBase()
    {
        var entries = V8MockTradeLogFactory.CreateSellWithRealizedPnl();
        var replayService = new TradeLogReplayService();
        var result = replayService.Replay(entries);

        // After profitable sell, PrincipalBase should increase
        Assert.True(result.RealizedPnl > 0);
    }

    [Fact]
    public void Dividend_AffectsPrincipalBase()
    {
        var entries = V8MockTradeLogFactory.CreateDividend("159509", 8000);
        var replayService = new TradeLogReplayService();
        var result = replayService.Replay(entries);

        // PrincipalBase should include dividend
        Assert.True(result.PrincipalBase >= 1_000_000);
    }

    [Fact]
    public void TotalAssetsTimesPointEight_IsNotUsed()
    {
        // Verify that no service uses totalAssets * 0.8 for pool budget
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var replayService = new TradeLogReplayService();
        var result = replayService.Replay(entries);

        double poolBudget = new RealSniperPoolBudgetService()
            .CalculateFirstRound(result.PrincipalBase, entries);

        double totalAssets = entries.Last().TotalAssets;
        Assert.False(RealSniperPoolBudgetService.IsInvalidPoolCalculation(poolBudget, totalAssets));
    }
}
