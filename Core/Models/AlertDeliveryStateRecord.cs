namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AlertDeliveryStateRecord
{
    public string DedupeKey { get; set; } = string.Empty;
    public string? LastAlertType { get; set; }
    public string? LastStrategyCode { get; set; }
    public string? LastAction { get; set; }
    public string? LastReason { get; set; }
    public string? LastContentHash { get; set; }
    public string? LastSentAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastTitle { get; set; }
    public string? LastContent { get; set; }
}
