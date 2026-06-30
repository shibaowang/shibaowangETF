namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class OrderDraftStateRecord
{
    public long Id { get; set; }
    public string DraftKey { get; set; } = string.Empty;
    public string CalculatedAt { get; set; } = string.Empty;
    public string SnapshotKey { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ActionInstruction { get; set; }
    public string Side { get; set; } = "NONE";
    public string Source { get; set; } = string.Empty;
    public string? TargetTier { get; set; }
    public double? TargetAmount { get; set; }
    public double? Price { get; set; }
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public string DraftStatus { get; set; } = "不可执行";
    public string? Reason { get; set; }
    public bool IsExecutable { get; set; }
}

public sealed class OrderDraftLegStateRecord
{
    public long Id { get; set; }
    public long DraftId { get; set; }
    public string DraftKey { get; set; } = string.Empty;
    public string CalculatedAt { get; set; } = string.Empty;
    public string SnapshotKey { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string? ActualCode { get; set; }
    public string Side { get; set; } = "NONE";
    public string Source { get; set; } = string.Empty;
    public string? ChannelClass { get; set; }
    public int? Priority { get; set; }
    public double? Price { get; set; }
    public double? Nav { get; set; }
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public string LegStatus { get; set; } = "不可执行";
    public string? Reason { get; set; }
}

public sealed class OrderFinalizationStateRecord
{
    public long Id { get; set; }
    public string FinalizedAt { get; set; } = string.Empty;
    public string DraftCalculatedAt { get; set; } = string.Empty;
    public string DraftKey { get; set; } = string.Empty;
    public string SnapshotKey { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ActionInstruction { get; set; }
    public string Side { get; set; } = "NONE";
    public string Source { get; set; } = string.Empty;
    public string? TargetTier { get; set; }
    public double? TargetAmount { get; set; }
    public double? Price { get; set; }
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public string FinalizationStatus { get; set; } = "已定稿";
    public string? Reason { get; set; }
    public string? Memo { get; set; }
}

public sealed class OrderFinalizationLegStateRecord
{
    public long Id { get; set; }
    public long FinalizationId { get; set; }
    public string FinalizedAt { get; set; } = string.Empty;
    public string DraftKey { get; set; } = string.Empty;
    public string SnapshotKey { get; set; } = string.Empty;
    public string StrategyCode { get; set; } = string.Empty;
    public string? ActualCode { get; set; }
    public string Side { get; set; } = "NONE";
    public string Source { get; set; } = string.Empty;
    public string? ChannelClass { get; set; }
    public int? Priority { get; set; }
    public double? Price { get; set; }
    public double? Nav { get; set; }
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public string LegStatus { get; set; } = "已定稿";
    public string? Reason { get; set; }
}
