namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 持仓快照：区分场内ETF与场外基金。
/// </summary>
public class HoldingSnapshot
{
    public string StrategyCode { get; set; } = string.Empty;
    public double EtfQuantity { get; set; }
    public double EtfCost { get; set; }
    public double OtcQuantity { get; set; }
    public double OtcCost { get; set; }

    public double TotalQuantity => EtfQuantity + OtcQuantity;
    public double TotalCost => EtfCost + OtcCost;

    public double EtfAvgCost => EtfQuantity > 0 ? EtfCost / EtfQuantity : 0;
    public double OtcAvgCost => OtcQuantity > 0 ? OtcCost / OtcQuantity : 0;
    public double TotalAvgCost => TotalQuantity > 0 ? TotalCost / TotalQuantity : 0;

    public double RealizedPnl { get; set; }
    public double TotalBuyAmount { get; set; }
    public double TotalSellAmount { get; set; }
    public double AdjustmentFactor { get; set; } = 1.0;
}
