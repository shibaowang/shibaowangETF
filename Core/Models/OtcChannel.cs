namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 OTCMap 8列模型。
/// 列：策略代码、场外代码、类别、是否启用、单日限额、优先级、最小申购、备注
/// </summary>
public class OtcChannel
{
    public string StrategyCode { get; set; } = string.Empty;
    public string OtcCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public double DailyLimit { get; set; }
    public int Priority { get; set; }
    public double MinBuyAmount { get; set; }
    public string Memo { get; set; } = string.Empty;

    public bool IsCClass => string.Equals(ClassName, "C类", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ClassName, "C", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// C类优先：Priority == 2 的通道优先卖出。
    /// </summary>
    public bool IsSellPriority => Priority == 2;
}
