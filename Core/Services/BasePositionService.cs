using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 底仓服务：BaseTarget = PrincipalBase * BasePositionRatio。
/// 默认 BasePositionRatio = 0.2（可配置）。
/// 底仓保护按剩余持仓成本判断，不按实时市值。
/// </summary>
public class BasePositionService
{
    private readonly StrategySettings _settings;

    public BasePositionService(StrategySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public double BasePositionRatio
    {
        get => _settings.BasePositionRatio;
        set => _settings.BasePositionRatio = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// 底仓目标金额 = PrincipalBase * BasePositionRatio
    /// </summary>
    public double CalculateBaseTarget(double principalBase)
    {
        if (principalBase <= 0) return 0;
        return principalBase * _settings.BasePositionRatio;
    }

    /// <summary>
    /// 当前底仓完成度 = 持仓成本 / PrincipalBase
    /// </summary>
    public double CalculateCompletionRatio(double holdingCost, double principalBase)
    {
        if (principalBase <= 0) return 0;
        return holdingCost / principalBase;
    }

    /// <summary>
    /// 底仓不足额 = BaseTarget - 持仓成本（>= 0）
    /// </summary>
    public double CalculateBaseNeed(double holdingCost, double principalBase)
    {
        double target = CalculateBaseTarget(principalBase);
        return Math.Max(0, target - holdingCost);
    }

    /// <summary>
    /// V8.2 IsSellQDataBaseSafe 等价：
    /// 卖出后剩余持仓成本 >= 卖出后本金基准 * BasePositionRatio。
    /// 判断依据是成本，不是实时市值。
    /// </summary>
    public bool IsSellBaseSafe(
        double currentHoldingCost,
        double principalBase,
        double sellAmount,
        double sellFee,
        double sellCostPart)
    {
        if (sellAmount <= 0 || sellCostPart <= 0 || currentHoldingCost <= 0 || principalBase <= 0)
            return false;

        double estPnl = sellAmount - sellFee - sellCostPart;
        double postPrincipalBase = principalBase + estPnl;
        if (postPrincipalBase < 0) postPrincipalBase = 0;

        double postTarget = postPrincipalBase * _settings.BasePositionRatio;
        double postCost = currentHoldingCost - sellCostPart;
        if (postCost < 0) postCost = 0;

        // 0.01 元级别容差
        return postCost + 0.01 >= postTarget;
    }

    /// <summary>
    /// V8.2 GetSafeSellQData 等价：二分搜索安全卖出金额。
    /// </summary>
    public double FindSafeSellAmount(
        double currentHoldingCost,
        double principalBase,
        double desiredSellValue,
        double price,
        Func<double, double, (double shares, double amount, double costPart)> quantifySell)
    {
        if (desiredSellValue <= 0 || price <= 0 || currentHoldingCost <= 0 || principalBase <= 0)
            return 0;

        double fee = desiredSellValue * 0.00013;
        var (shares, amount, costPart) = quantifySell(desiredSellValue, fee);

        if (amount > 0 && IsSellBaseSafe(currentHoldingCost, principalBase, amount, fee, costPart))
            return amount;

        double low = 0, high = desiredSellValue, best = 0;
        for (int n = 0; n < 32; n++)
        {
            double mid = (low + high) / 2.0;
            fee = mid * 0.00013;
            (shares, amount, costPart) = quantifySell(mid, fee);

            if (amount > 0 && costPart > 0 &&
                IsSellBaseSafe(currentHoldingCost, principalBase, amount, fee, costPart))
            {
                best = amount;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return best;
    }
}
