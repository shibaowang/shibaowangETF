using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.TradeLog;

public class TradeLogPreCheckTests
{
    [Fact]
    public void Deposit_NetCashImpact_Correct()
    {
        var entries = V8MockTradeLogFactory.CreateDepositOnly(1_000_000, 5);
        entries[0].NetCashImpact = 1_000_000 - 5;
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.True(result);
    }

    [Fact]
    public void Withdrawal_NetCashImpact_Correct()
    {
        var entries = V8MockTradeLogFactory.CreateWithdrawal(100_000, 5);
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.True(result);
    }

    [Fact]
    public void Deposit_NegativeAmount_Fails()
    {
        var entries = V8MockTradeLogFactory.CreateDepositOnly(1_000_000);
        entries[0].Amount = -1000;
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
        Assert.Contains(service.Errors, e => e.Message.Contains("金额不能小于0"));
    }

    [Fact]
    public void Withdrawal_PositiveNetCashImpact_Fails()
    {
        var entries = V8MockTradeLogFactory.CreateWithdrawal(100_000, 0);
        entries[1].NetCashImpact = 100_000; // should be negative
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
    }

    [Fact]
    public void NegativeFee_Fails()
    {
        var entries = V8MockTradeLogFactory.CreateDepositOnly(1_000_000);
        entries[0].Fee = -10;
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
    }

    [Fact]
    public void InvalidAction_Fails()
    {
        var entries = new List<TradeLogEntry>
        {
            new() { RowIndex = 1, StrategyCode = "159509", Action = "做空", Amount = 1000 }
        };
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
    }

    [Fact]
    public void Deposit_NotCASH_Fails()
    {
        var entries = V8MockTradeLogFactory.CreateDepositOnly(1_000_000);
        entries[0].StrategyCode = "159509"; // should be CASH
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
    }

    [Fact]
    public void NonNumericAmount_Fails()
    {
        var entries = new List<TradeLogEntry>
        {
            new() { RowIndex = 1, StrategyCode = "159509", Action = "买入", Amount = double.NaN }
        };
        var service = new TradeLogPreCheckService();
        bool result = service.PreCheck(entries);
        Assert.False(result);
    }

    [Fact]
    public void GetFundingNetImpact_Deposit_ReturnsCorrect()
    {
        double net = TradeLogPreCheckService.GetFundingNetImpact("入金", 1000, 5);
        Assert.Equal(995, net);
    }

    [Fact]
    public void GetFundingNetImpact_Withdrawal_ReturnsCorrect()
    {
        double net = TradeLogPreCheckService.GetFundingNetImpact("出金", 1000, 5);
        Assert.Equal(-1005, net);
    }

    [Fact]
    public void GetFundingNetImpact_NegativeFee_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TradeLogPreCheckService.GetFundingNetImpact("入金", 1000, -1));
    }
}
