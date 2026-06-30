using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.BasePosition;

public class BasePositionTests
{
    [Fact]
    public void DefaultRatio_Is20Percent()
    {
        var settings = StrategySettings.Default("159509");
        var service = new BasePositionService(settings);

        double baseTarget = service.CalculateBaseTarget(1_000_000);
        Assert.Equal(200_000, baseTarget, 0.01);
    }

    [Fact]
    public void Ratio_CanBeChanged_To50Percent()
    {
        var settings = StrategySettings.Default("159509");
        var service = new BasePositionService(settings);
        service.BasePositionRatio = 0.5;

        double baseTarget = service.CalculateBaseTarget(1_000_000);
        Assert.Equal(500_000, baseTarget, 0.01);
    }

    [Fact]
    public void BaseNeed_WhenInsufficient()
    {
        var settings = StrategySettings.Default("159509");
        var service = new BasePositionService(settings);

        double holdingCost = 50_000;
        double principalBase = 1_000_000;
        double need = service.CalculateBaseNeed(holdingCost, principalBase);

        Assert.Equal(150_000, need, 0.01);
    }

    [Fact]
    public void Sell_DoesNotBreak_BaseProtection()
    {
        var settings = StrategySettings.Default("159509");
        var service = new BasePositionService(settings);

        double hc = 400_000;
        double pb = 1_000_000;
        double sa = 10_000;
        double f = sa * 0.00013;
        double cp = 5000; // small cost part
        bool safe = service.IsSellBaseSafe(hc, pb, sa, f, cp);
        // postBase = 1_000_000 + (10000 - 1.3 - 5000) = 1_004_998.7
        // postTarget = 1_004_998.7 * 0.2 = 200_999.74
        // postCost = 400000 - 5000 = 395000 > 200999.74 -> SAFE
        Assert.True(safe);
    }

    [Fact]
    public void BaseProtection_By_Cost_Not_MarketValue()
    {
        var settings = StrategySettings.Default("159509");
        var service = new BasePositionService(settings);

        // Even if market value drops dramatically, cost-based protection should hold
        double holdingCost = 200_000;
        double principalBase = 1_000_000;

        // A large sell should be caught by cost-based check
        double sellAmt = 180_000;
        double fee = sellAmt * 0.00013;
        double costPart = 180_000; // nearly all cost
        bool safe = service.IsSellBaseSafe(holdingCost, principalBase, sellAmt, fee, costPart);
        // postCost = 20000, postPrincipalBase ~= 1000000, postTarget = 200000
        // 20000 < 200000 => NOT safe
        Assert.False(safe);
    }
}
