using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 底仓保护服务：确保卖出不破坏20%底仓。
/// 保护依据是剩余持仓成本，不按实时市值。
/// </summary>
public class PositionProtectionService
{
    private readonly BasePositionService _basePosition;
    private readonly TradeQuantifier _quantifier;

    public PositionProtectionService(BasePositionService basePosition, TradeQuantifier quantifier)
    {
        _basePosition = basePosition ?? throw new ArgumentNullException(nameof(basePosition));
        _quantifier = quantifier ?? throw new ArgumentNullException(nameof(quantifier));
    }

    /// <summary>
    /// V8.2 IsSellQDataBaseSafe 等价：卖出后底仓是否安全。
    /// </summary>
    public bool IsSellSafe(
        string symbol,
        string tradeSource,
        double currentHoldingCost,
        double principalBase,
        double sellAmount,
        double price)
    {
        if (sellAmount <= 0 || price <= 0 || currentHoldingCost <= 0 || principalBase <= 0)
            return false;

        double fee = sellAmount * 0.00013;
        var qResult = _quantifier.Quantify("卖出", tradeSource, sellAmount, price, fee);

        if (!qResult.IsExecutable) return false;

        double costPart = GetCostPart(symbol, tradeSource, currentHoldingCost, qResult.Quantity, price);

        return _basePosition.IsSellBaseSafe(currentHoldingCost, principalBase, sellAmount, fee, costPart);
    }

    /// <summary>
    /// V8.2 GetSafeSellQData 等价：二分搜索最大安全卖出金额。
    /// </summary>
    public double FindSafeSellAmount(
        string symbol,
        string tradeSource,
        double currentHoldingCost,
        double principalBase,
        double desiredSellValue,
        double price)
    {
        return _basePosition.FindSafeSellAmount(
            currentHoldingCost,
            principalBase,
            desiredSellValue,
            price,
            (targetValue, fee) =>
            {
                var qResult = _quantifier.Quantify("卖出", tradeSource, targetValue, price, fee);
                if (!qResult.IsExecutable) return (0, 0, 0);
                double costPart = GetCostPart(symbol, tradeSource, currentHoldingCost, qResult.Quantity, price);
                return (qResult.Quantity, qResult.Amount, costPart);
            });
    }

    private static double GetCostPart(string symbol, string tradeSource, double totalCost, double sellQty, double price)
    {
        // 简化：按比例扣减
        double totalQty = totalCost / Math.Max(price, 0.0001);
        if (totalQty <= 0) return 0;
        return (totalCost / totalQty) * sellQty;
    }
}
