namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AccountReplaySnapshotRecord
{
    public long Id { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public double? TotalAssets { get; set; }
    public double? TotalPnl { get; set; }
    public double? TotalUnrealizedPnl { get; set; }
    public double? CashBalance { get; set; }
    public double? Principal { get; set; }
    public bool MarketValueComplete { get; set; }
}
