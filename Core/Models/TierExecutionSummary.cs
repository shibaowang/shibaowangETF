namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// T1-T6 档位执行摘要。
/// </summary>
public class TierExecutionSummary
{
    public string TierName { get; set; } = string.Empty;
    public int TierLevel { get; set; }
    public int TierWeight { get; set; }
    public double TargetAmount { get; set; }
    public double ExecutedAmount { get; set; }
    public double RemainingAmount => Math.Max(0, TargetAmount - ExecutedAmount);
    public bool IsCompleted => Math.Abs(RemainingAmount) <= 0.01;
}
