namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class PositionStateRecord
{
    public long Id { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string ActualCode { get; set; } = string.Empty;
    public string Source { get; set; } = "场内ETF";
    public double Quantity { get; set; }
    public double CostAmount { get; set; }
    public double AdjFactor { get; set; } = 1;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
