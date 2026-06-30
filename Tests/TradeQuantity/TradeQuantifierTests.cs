using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.TradeQuantity;

public class TradeQuantifierTests
{
    private readonly TradeQuantifier _quantifier = new();

    [Fact]
    public void EtfBuy_RoundsUp_To100Shares()
    {
        // Need to buy 950 shares worth -> rounds up to 1000
        var result = _quantifier.Quantify("买入", "场内直投", 950 * 1.20, 1.20, 0, 1_000_000);

        Assert.True(result.IsExecutable);
        Assert.Equal(1000, result.Quantity, 0.01);
        Assert.True(result.Amount >= 950 * 1.20);
    }

    [Fact]
    public void EtfSell_RoundsDown_To100Shares()
    {
        var result = _quantifier.Quantify("卖出", "场内直投", 950 * 1.20, 1.20);

        Assert.True(result.IsExecutable);
        Assert.Equal(900, result.Quantity, 0.01);
    }

    [Fact]
    public void OtcBuy_RoundsUp_ToCent()
    {
        var result = _quantifier.Quantify("买入", "场外替代", 100.001, 1.284, 0, 1_000_000);

        Assert.True(result.IsExecutable);
        Assert.Equal(100.01, result.Amount, 0.01); // Round up to cent
    }

    [Fact]
    public void OtcSell_RoundsDown_ToShares()
    {
        var result = _quantifier.Quantify("卖出", "场外替代", 100.001, 1.284);

        Assert.True(result.IsExecutable);
        // 100.001 / 1.284 = 77.882... -> floor to 0.0001
        double expectedQty = Math.Floor((100.001 / 1.284) * 10000) / 10000.0;
        Assert.Equal(expectedQty, result.Quantity, 0.0001);
    }

    [Fact]
    public void InsufficientCash_Returns_NotExecutable()
    {
        var result = _quantifier.Quantify("买入", "场内直投", 1_000_000, 1.20, 0, 500);

        Assert.False(result.IsExecutable);
        Assert.NotEmpty(result.RejectReason);
    }

    [Fact]
    public void Buy_NetCashImpact_IsNegative()
    {
        var result = _quantifier.Quantify("买入", "场内直投", 1000, 1.20, 0, 1_000_000);

        Assert.True(result.IsExecutable);
        Assert.True(result.NetCashImpact < 0);
    }

    [Fact]
    public void Sell_NetCashImpact_IsPositive()
    {
        var result = _quantifier.Quantify("卖出", "场内直投", 1000, 1.20, 5);

        Assert.True(result.IsExecutable);
        Assert.True(result.NetCashImpact > 0);
    }

    [Fact]
    public void BuyWithFee_ReducesNetCashImpact()
    {
        var noFee = _quantifier.Quantify("买入", "场内直投", 1000, 1.20, 0, 1_000_000);
        var withFee = _quantifier.Quantify("买入", "场内直投", 1000, 1.20, 5, 1_000_000);

        Assert.True(noFee.IsExecutable);
        Assert.True(withFee.IsExecutable);
        Assert.True(withFee.NetCashImpact < noFee.NetCashImpact);
    }

    [Fact]
    public void NonBuySell_Action_ReturnsBasicQuant()
    {
        var result = _quantifier.Quantify("分红", "", 1000, 1.0);

        Assert.True(result.Quantity > 0);
        Assert.Equal(1000, result.Amount);
    }
}
