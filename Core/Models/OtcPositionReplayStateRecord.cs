namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class OtcPositionReplayStateRecord
{
    public long Id { get; set; }
    public string CalculatedAt { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string ActualCode { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public double CostAmount { get; set; }
    public double AverageCost { get; set; }
    public double? Nav { get; set; }
    public double? MarketValue { get; set; }
    public double? DailyPnl { get; set; }
    public double? UnrealizedPnl { get; set; }
    public double? ReturnRate { get; set; }
    public string QuoteStatus { get; set; } = "未连接";
}
