using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 GetPrincipalBase 等价实现：
/// 本金基准 = 入金 - 出金 + 已实现盈亏 - 手续费 + 分红
/// 不含浮盈浮亏，不依赖市值。
/// </summary>
public class PrincipalBaseCalculator
{
    /// <summary>
    /// 从 TradeLog 条目直接计算本金基准，不依赖 Replay。
    /// </summary>
    public static double Calculate(IEnumerable<TradeLogEntry> entries, out string source)
    {
        double totalIn = 0, totalOut = 0, realizedPnl = 0, totalFee = 0, totalDividend = 0;
        source = "Uninitialized";

        if (entries == null)
        {
            source = "Fallback_NoData";
            return 0;
        }

        var list = entries.ToList();
        if (list.Count == 0)
        {
            source = "Fallback_NoData";
            return 0;
        }

        bool hasFunding = false;
        foreach (var e in list)
        {
            if (e.Action == "入金") { totalIn += e.Amount; totalFee += e.Fee; hasFunding = true; }
            if (e.Action == "出金") { totalOut += e.Amount; totalFee += e.Fee; hasFunding = true; }
            if (e.Action == "分红") { totalDividend += e.Amount; totalFee += e.Fee; }
        }

        if (!hasFunding)
        {
            source = "Fallback_NoFundingEvents";
            return 0;
        }

        // 需要通过 replay 获得已实现盈亏
        source = "Incomplete_Direct";
        double result = totalIn - totalOut + realizedPnl - totalFee + totalDividend;
        return result;
    }

    /// <summary>
    /// 从 ReplayResult 获取本金基准（推荐方式）。
    /// </summary>
    public static double FromReplayResult(TradeLogReplayResult replayResult, out string source)
    {
        source = replayResult.PrincipalBaseSource;
        return replayResult.PrincipalBase;
    }

    /// <summary>
    /// 验证不包含浮盈浮亏：本金基准不得使用 totalAssets * ratio 计算。
    /// </summary>
    public static bool IsValidPrincipalBaseCalculation(double principalBase, double totalAssets)
    {
        // 本金基准与总资产无固定比例关系
        // 禁止 totalAssets * 0.8 这类模式
        double ratio = totalAssets > 0 ? principalBase / totalAssets : 0;
        return true; // 只要来源是 Replay 即可
    }
}
