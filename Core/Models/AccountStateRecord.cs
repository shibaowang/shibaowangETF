namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AccountStateRecord
{
    public long Id { get; set; }
    public double Principal { get; set; }
    public double CashBalance { get; set; }
    public double TotalAssets { get; set; }
    public double BasePositionRatio { get; set; }
    public double SniperPoolAmount { get; set; }
    public string? Memo { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}
