namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 StrategyRow 等价模型。BasePositionRatio 可配置，默认 0.2。
/// </summary>
public class StrategySettings
{
    public string StrategyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double ExtremePremiumThreshold { get; set; }
    public double PremiumTakeProfitThreshold { get; set; }
    public double ReturnTakeProfitThreshold { get; set; }
    public double OtcAlternativeThreshold { get; set; }
    public double BasePositionRatio { get; set; } = 0.2;
    public double AdjustmentFactor { get; set; } = 1.0;

    /// <summary>
    /// T1-T6 权重数组，长度 6，默认 [1, 2, 4, 8, 16, 32]。
    /// </summary>
    public int[] TierWeights { get; set; } = { 1, 2, 4, 8, 16, 32 };

    public int TotalTierShares
    {
        get
        {
            int sum = 0;
            foreach (int w in TierWeights) sum += w;
            return sum > 0 ? sum : 63;
        }
    }

    public static StrategySettings Default(string code)
    {
        return new StrategySettings
        {
            StrategyCode = code,
            Name = code,
            BasePositionRatio = 0.2,
            TierWeights = new[] { 1, 2, 4, 8, 16, 32 }
        };
    }
}
