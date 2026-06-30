namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class StrategyConfigRecord
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? IndexSecId { get; set; }
    public double? EtfHigh { get; set; }
    public double? IndexHigh { get; set; }
    public double? ExtraPrice { get; set; }
    public double? TakeProfitPrice { get; set; }
    public double? SellRatio { get; set; }
    public double? AddPremiumLimit { get; set; }
    public double? T1Weight { get; set; }
    public double? T2Weight { get; set; }
    public double? T3Weight { get; set; }
    public double? T4Weight { get; set; }
    public double? T5Weight { get; set; }
    public double? T6Weight { get; set; }
    public double? AdjFactor { get; set; }
    public bool Enabled { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
