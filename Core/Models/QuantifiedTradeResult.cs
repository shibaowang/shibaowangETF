namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 GetQuantifiedTrade 返回值：量化后的交易参数。
/// </summary>
public class QuantifiedTradeResult
{
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public double NetCashImpact { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public bool IsExecutable => string.IsNullOrEmpty(RejectReason) && Amount > 0;
    public string RejectReason { get; set; } = string.Empty;

    public static QuantifiedTradeResult NotExecutable(string reason)
    {
        return new QuantifiedTradeResult { RejectReason = reason };
    }
}
