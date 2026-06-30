using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 GetRealSniperPoolBudget 等价实现。
/// 第一轮：PrincipalBase - 实际战略底仓买入金额。
/// 周期结束后：使用最后一次周期结束行的现金余额。
/// 仅作为 T1-T6 内部档位预算基准，不作为主界面狙击资金池显示值。
/// 禁止使用 totalAssets * 0.8。
/// </summary>
public class RealSniperPoolBudgetService
{
    /// <summary>
    /// 第一轮狙击资金池 = PrincipalBase - 实际战略底仓买入金额。
    /// </summary>
    public double CalculateFirstRound(double principalBase, IEnumerable<TradeLogEntry> entries)
    {
        if (principalBase <= 0) return 0;

        double strategicBuyTotal = 0;
        if (entries != null)
        {
            foreach (var e in entries)
            {
                if (e.IsBuy && e.IsStrategicBase)
                {
                    strategicBuyTotal += e.Amount;
                }
            }
        }

        return Math.Max(0, principalBase - strategicBuyTotal);
    }

    /// <summary>
    /// 周期结束后：使用最后一个 tier=周期结束 行的现金余额。
    /// </summary>
    public double CalculateFromCycleEnd(IEnumerable<TradeLogEntry> entries)
    {
        if (entries == null) return 0;

        TradeLogEntry? lastCycleEnd = null;
        foreach (var e in entries)
        {
            if (string.Equals(e.Tier?.Trim(), "周期结束", StringComparison.OrdinalIgnoreCase))
            {
                lastCycleEnd = e;
            }
        }

        return lastCycleEnd?.CashBalance ?? 0;
    }

    /// <summary>
    /// 按 T1-T6 63 份拆分资金池。
    /// </summary>
    public List<double> SplitByTierWeights(double poolBudget, int[] tierWeights)
    {
        var result = new List<double>();
        int totalShares = 0;
        foreach (int w in tierWeights) totalShares += w;
        if (totalShares <= 0) return result;

        foreach (int w in tierWeights)
        {
            result.Add(poolBudget * w / totalShares);
        }
        return result;
    }

    /// <summary>
    /// 明确禁止 totalAssets * 0.8 计算资金池的校验。
    /// </summary>
    public static bool IsInvalidPoolCalculation(double poolBudget, double totalAssets)
    {
        if (totalAssets <= 0 || poolBudget <= 0) return false;
        double ratio = poolBudget / totalAssets;
        // 如果恰好等于 0.8（允许 0.005 误差），标记为可疑
        return Math.Abs(ratio - 0.8) < 0.005;
    }
}
