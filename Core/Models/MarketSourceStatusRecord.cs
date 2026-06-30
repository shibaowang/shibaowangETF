namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketSourceStatusRecord
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LastSuccessAt { get; set; }
    public string? LastFailureAt { get; set; }
    public int FailureCount { get; set; }
    public string? CooldownUntil { get; set; }
    public string? LastError { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}
