namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class OtcChannelRecord
{
    public long Id { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string OtcCode { get; set; } = string.Empty;
    public string ClassType { get; set; } = "A类";
    public bool Enabled { get; set; } = true;
    public double DailyLimit { get; set; }
    public int Priority { get; set; } = 999;
    public double MinBuy { get; set; }
    public string? Memo { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
