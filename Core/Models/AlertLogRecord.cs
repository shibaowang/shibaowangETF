namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AlertLogRecord
{
    public long Id { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? StrategyCode { get; set; }
    public string? ActualCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public bool WechatEnabled { get; set; }
    public string? WechatStatus { get; set; }
    public string? WechatError { get; set; }
    public string? WechatSentAt { get; set; }
    public bool VoiceEnabled { get; set; }
    public string? VoiceStatus { get; set; }
    public string? VoiceError { get; set; }
    public string? VoicePlayedAt { get; set; }
    public string? Source { get; set; }
    public string? ContentHash { get; set; }
}
