namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 场外多通道交易腿。
/// </summary>
public class OtcTradeLeg
{
    public string StrategyCode { get; set; } = string.Empty;
    public string ActualCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public double PriceOrNav { get; set; }
    public double Fee { get; set; }
    public double NetCashImpact { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public double CostPart { get; set; }
}
