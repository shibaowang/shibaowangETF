namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AccountReplayStateRecord
{
    public long Id { get; set; }
    public string CalculatedAt { get; set; } = string.Empty;
    public string ReplayStatus { get; set; } = "未回放";
    public string? ReplayError { get; set; }
    public double? CashBalance { get; set; }
    public double? Principal { get; set; }
    public double? TotalPositionCost { get; set; }
    public double? KnownMarketValue { get; set; }
    public double? TotalAssets { get; set; }
    public double? TotalRealizedPnl { get; set; }
    public double? TotalUnrealizedPnl { get; set; }
    public double? TotalPnl { get; set; }
    public double? TotalReturnRate { get; set; }
    public double? CashRatio { get; set; }
    public double? PositionRatio { get; set; }
    public double? BasePositionRatio { get; set; }
    public bool MarketValueComplete { get; set; }
    public long? LastTradeLogId { get; set; }
}
