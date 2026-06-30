namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class StrategyDecisionStateRecord
{
    public long Id { get; set; }
    public string CalculatedAt { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ActionInstruction { get; set; }
    public string? StrategyStatus { get; set; }
    public string? PreferredSource { get; set; }
    public string? TargetTier { get; set; }
    public double? TargetAmount { get; set; }
    public double? AvailableCash { get; set; }
    public double? SuggestedPrice { get; set; }
    public double? Premium { get; set; }
    public double? ReturnRate { get; set; }
    public double? EtfDrawdown { get; set; }
    public double? IndexDrawdown { get; set; }
    public string? BaseMode { get; set; }
    public double? BaseRatio { get; set; }
    public double? BaseFixedAmount { get; set; }
    public double? BaseTargetAmount { get; set; }
    public double? BaseCurrentCost { get; set; }
    public double? BaseCompletionRate { get; set; }
    public double? BaseGapAmount { get; set; }
    public bool BaseTargetCapped { get; set; }
    public double? RealSniperPool { get; set; }
    public double? TierTotalParts { get; set; }
    public double? TierCumulativeTarget { get; set; }
    public double? TierExecutedAmount { get; set; }
    public double? TierRemainAmount { get; set; }
    public string? PrerequisiteStatus { get; set; }
    public string? PrerequisiteMessage { get; set; }
    public bool IsActionable { get; set; }
}
