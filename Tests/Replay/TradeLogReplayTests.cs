using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Replay;

public class TradeLogReplayTests
{
    [Fact]
    public void Buy_ReducesCash_AndIncreasesHoldingCost()
    {
        var entries = V8MockTradeLogFactory.CreateEtfBuy("159509", 1000, 1.284, "狙击一档");
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.True(result.CashBalance < 1_000_000);
        Assert.True(result.Holdings.ContainsKey("159509"));
        Assert.Equal(1000, result.Holdings["159509"].EtfQuantity);
        Assert.Equal(1284, result.Holdings["159509"].EtfCost, 0.01);
    }

    [Fact]
    public void Sell_IncreasesCash_ReducesCost_GeneratesPnL()
    {
        var entries = V8MockTradeLogFactory.CreateSellWithRealizedPnl();
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.True(result.RealizedPnl > 0);
        Assert.True(result.CashBalance > 1_000_000 - 5000 * 1.20);
    }

    [Fact]
    public void Dividend_IncreasesCash()
    {
        var entries = V8MockTradeLogFactory.CreateDividend("159509", 5000);
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.Equal(1_005_000, result.CashBalance, 0.01);
        Assert.Equal(5000, result.TotalDividend, 0.01);
    }

    [Fact]
    public void BonusShares_OnlyIncreasesQty_NotCost()
    {
        var entries = V8MockTradeLogFactory.CreateBonusShares("159509", 500);
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.True(result.Holdings.ContainsKey("159509"));
        Assert.Equal(500, result.Holdings["159509"].EtfQuantity);
        Assert.Equal(0, result.Holdings["159509"].EtfCost);
    }

    [Fact]
    public void Split_OnlyIncreasesQty_NotCost()
    {
        var entries = V8MockTradeLogFactory.CreateSplit("159509", 2.0, 5000);
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.Equal(5000, result.Holdings["159509"].EtfQuantity);
        Assert.Equal(0, result.Holdings["159509"].EtfCost);
    }

    [Fact]
    public void Merge_DecreasesQty()
    {
        // First buy, then merge
        var buyEntries = V8MockTradeLogFactory.CreateEtfBuy("159509", 5000, 1.20);
        buyEntries.Add(new TradeLogEntry
        {
            RowIndex = 3, Time = new DateTime(2025, 7, 1, 9, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "合并", Quantity = 2500, Amount = 0,
            Source = "场内直投", Fee = 0, NetCashImpact = 0,
            CashBalance = buyEntries.Last().CashBalance, TotalAssets = buyEntries.Last().TotalAssets
        });
        var service = new TradeLogReplayService();
        var result = service.Replay(buyEntries);

        Assert.True(result.OverallSuccess);
        Assert.True(result.Holdings["159509"].EtfQuantity >= 0);
        Assert.Equal(2500, result.Holdings["159509"].EtfQuantity, 0.01);
    }

    [Fact]
    public void Adjustment_RecordsFactor()
    {
        var entries = V8MockTradeLogFactory.CreateAdjustment("159509", 1.05);
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.Equal(1.05, result.Holdings["159509"].AdjustmentFactor);
    }

    [Fact]
    public void SellMoreThanHolding_ReportsError()
    {
        var entries = V8MockTradeLogFactory.CreateEtfBuy("159509", 1000, 1.20);
        entries.Add(new TradeLogEntry
        {
            RowIndex = 3, Time = new DateTime(2025, 3, 1, 14, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "卖出", Price = 1.50, Quantity = 5000, Amount = 7500,
            Source = "场内直投", Fee = 0.975
        });
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.False(result.OverallSuccess);
        Assert.NotEmpty(result.ReplayErrors);
    }

    [Fact]
    public void CashBalance_MatchAfterReplay()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        // Final cash from last entry
        double expectedCash = entries.Last().CashBalance;
        double diff = Math.Abs(result.CashBalance - expectedCash);
        Assert.True(diff < 0.02, $"Expected ~{expectedCash}, got {result.CashBalance}, diff={diff}");
    }

    [Fact]
    public void OtcBuy_TracksSeparateFromEtf()
    {
        var entries = V8MockTradeLogFactory.CreateOtcBuy("159509", 50000, 1.284);
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.True(result.Holdings["159509"].OtcQuantity > 0);
        Assert.True(result.Holdings["159509"].EtfQuantity == 0);
    }

    [Fact]
    public void FullScenario_PrincipalBase_ComputedCorrectly()
    {
        var entries = V8MockTradeLogFactory.CreateFullScenario();
        var service = new TradeLogReplayService();
        var result = service.Replay(entries);

        Assert.True(result.OverallSuccess);
        Assert.Equal("Replay", result.PrincipalBaseSource);
        Assert.True(result.PrincipalBase > 0);
    }
}
