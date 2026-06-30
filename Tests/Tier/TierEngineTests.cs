using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Tier;

public class TierEngineTests
{
    [Fact]
    public void TierWeights_Default_SumTo63()
    {
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        Assert.Equal(63, engine.TotalShares);
        Assert.Equal(1, engine.TierWeights[0]);
        Assert.Equal(2, engine.TierWeights[1]);
        Assert.Equal(4, engine.TierWeights[2]);
        Assert.Equal(8, engine.TierWeights[3]);
        Assert.Equal(16, engine.TierWeights[4]);
        Assert.Equal(32, engine.TierWeights[5]);
    }

    [Theory]
    [InlineData(-0.04, 0)]  // not triggered
    [InlineData(-0.05, 1)]  // tier 1
    [InlineData(-0.10, 2)]  // tier 2
    [InlineData(-0.15, 3)]  // tier 3
    [InlineData(-0.20, 4)]  // tier 4
    [InlineData(-0.25, 5)]  // tier 5
    [InlineData(-0.30, 6)]  // tier 6
    [InlineData(-0.35, 6)]  // tier 6 (deepest)
    public void IndexDrawdown_Triggers_CorrectTier(double drawdown, int expectedLevel)
    {
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        int level = engine.GetTriggeredTierLevel(drawdown);
        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public void DeepestDrawdown_JudgedFirst()
    {
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        // -35% should trigger tier 6, not tier 1
        int level = engine.GetTriggeredTierLevel(-0.35);
        Assert.Equal(6, level);
    }

    [Fact]
    public void CumulativeTarget_Correct()
    {
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        // Tier 3 triggered: target = (1+2+4)/63 * poolBudget = 7/63
        double poolBudget = 630_000;
        double target = engine.GetCumulativeTarget(poolBudget, 3);
        Assert.Equal(70_000, target, 0.01);
    }

    [Fact]
    public void GetTierExecutedAmt_OnlyCounts_Buy()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        double executed = engine.GetTierExecutedAmt(entries);

        // Only tier 1 and tier 2 buys should be counted
        // FullScenario has tier1=30000, tier2=60000
        Assert.Equal(90_000, executed, 0.01);
    }

    [Fact]
    public void Sell_DoesNotReset_Tier()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        double beforeSell = engine.GetTierExecutedAmt(entries.Take(4));
        double afterAll = engine.GetTierExecutedAmt(entries);

        // Sell (entry 5) should not reduce executed amount
        Assert.True(afterAll >= beforeSell);
    }

    [Fact]
    public void Dividend_Funding_NotCounted_InTier()
    {
        var entries = V8MockTradeLogFactory.CreateDividend("159509", 5000);
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);

        double executed = engine.GetTierExecutedAmt(entries);
        Assert.Equal(0, executed, 0.01);
    }

    [Fact]
    public void No85Percent_Tolerance()
    {
        // 85k / 100k is NOT completed (only 85%)
        Assert.False(TierEngine.IsTierCompleted(100_000, 85_000));

        // Diff = 0.02 > 0.01 => NOT completed
        Assert.False(TierEngine.IsTierCompleted(1000, 999.98));

        // Exact match IS completed
        Assert.True(TierEngine.IsTierCompleted(100_000, 100_000));

        // Diff = 0.005 < 0.01 => IS completed
        Assert.True(TierEngine.IsTierCompleted(1000, 999.995));
    }

    [Fact]
    public void ExecutionSummaries_All6TiersPresent()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var settings = StrategySettings.Default("159509");
        var engine = new TierEngine(settings);
        var replayService = new TradeLogReplayService();
        var replayResult = replayService.Replay(entries);
        var poolService = new RealSniperPoolBudgetService();
        double pool = poolService.CalculateFirstRound(replayResult.PrincipalBase, entries);

        var summaries = engine.GetExecutionSummaries(pool, 2, entries);
        Assert.Equal(6, summaries.Count);
        Assert.Contains(summaries, s => s.TierName == "狙击一档");
        Assert.Contains(summaries, s => s.TierName == "狙击六档");
    }
}
