using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 T1-T6 回撤狙击引擎。
/// 权重 1/2/4/8/16/32，总份额 63。
/// 按跟踪指数回撤触发，累计档位目标。
/// </summary>
public class TierEngine
{
    private readonly StrategySettings _settings;

    public TierEngine(StrategySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public int[] TierWeights => _settings.TierWeights;
    public int TotalShares => _settings.TotalTierShares;

    private static readonly (int Level, string Name, double Threshold)[] TierDefs =
    {
        (6, "狙击六档", -0.30),
        (5, "狙击五档", -0.25),
        (4, "狙击四档", -0.20),
        (3, "狙击三档", -0.15),
        (2, "狙击二档", -0.10),
        (1, "狙击一档", -0.05)
    };

    /// <summary>
    /// 从最深回撤向浅回撤判断当前触发档位。
    /// 返回触发的档位级别（1-6），0 表示未触发任何档位。
    /// </summary>
    public int GetTriggeredTierLevel(double indexDrawdown)
    {
        // 从最深回撤开始判断（-30%）
        for (int i = 0; i < TierDefs.Length; i++)
        {
            if (indexDrawdown <= TierDefs[i].Threshold)
                return TierDefs[i].Level;
        }
        return 0;
    }

    /// <summary>
    /// 计算累计档位目标金额。
    /// 例如触发三档时，目标 = 一档 + 二档 + 三档。
    /// </summary>
    public double GetCumulativeTarget(double poolBudget, int triggeredLevel)
    {
        if (triggeredLevel <= 0 || poolBudget <= 0) return 0;

        int cumulativeWeight = 0;
        for (int i = 0; i < triggeredLevel; i++)
        {
            cumulativeWeight += _settings.TierWeights[i];
        }
        return poolBudget * cumulativeWeight / TotalShares;
    }

    /// <summary>
    /// 获取各档位执行摘要。
    /// </summary>
    public List<TierExecutionSummary> GetExecutionSummaries(
        double poolBudget, int triggeredLevel, IEnumerable<TradeLogEntry> entries)
    {
        var summaries = new List<TierExecutionSummary>();
        var executedByTier = GetExecutedAmountByTier(entries);

        for (int i = 0; i < 6; i++)
        {
            double tierAllocation = poolBudget * _settings.TierWeights[i] / TotalShares;
            double executed = executedByTier.GetValueOrDefault(i + 1, 0);

            summaries.Add(new TierExecutionSummary
            {
                TierName = TierDefs[i].Name,
                TierLevel = i + 1,
                TierWeight = _settings.TierWeights[i],
                TargetAmount = tierAllocation,
                ExecutedAmount = executed
            });
        }
        return summaries;
    }

    /// <summary>
    /// V8.2 GetTierExecutedAmt：只统计 TradeLog 中"买入"金额，全局统计。
    /// 卖出不重置档位，分红/入金/出金不计入。
    /// </summary>
    public double GetTierExecutedAmt(IEnumerable<TradeLogEntry> entries, int? specificTierLevel = null)
    {
        if (entries == null) return 0;

        double total = 0;
        foreach (var e in entries)
        {
            if (!e.IsBuy) continue;

            int level = ParseTierLevel(e.Tier);
            if (level <= 0) continue;

            if (specificTierLevel.HasValue && level != specificTierLevel.Value)
                continue;

            total += e.Amount;
        }
        return total;
    }

    private Dictionary<int, double> GetExecutedAmountByTier(IEnumerable<TradeLogEntry> entries)
    {
        var map = new Dictionary<int, double>();
        if (entries == null) return map;

        foreach (var e in entries)
        {
            if (!e.IsBuy) continue;
            int level = ParseTierLevel(e.Tier);
            if (level <= 0) continue;

            if (!map.ContainsKey(level)) map[level] = 0;
            map[level] += e.Amount;
        }
        return map;
    }

    private static int ParseTierLevel(string tierName)
    {
        if (string.IsNullOrWhiteSpace(tierName)) return 0;
        for (int i = 0; i < TierDefs.Length; i++)
        {
            if (tierName.Contains(TierDefs[i].Name))
                return TierDefs[i].Level;
        }
        return 0;
    }

    /// <summary>
    /// 明确禁止 85% 宽容完成：只允许 0.01 元级别误差。
    /// </summary>
    public static bool IsTierCompleted(double target, double executed)
    {
        return Math.Abs(target - executed) < 0.01;
    }
}
