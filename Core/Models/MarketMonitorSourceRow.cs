namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketMonitorSourceRow
{
    public string Source { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string RawStatus { get; init; } = string.Empty;
    public string Status { get; init; } = "未记录";
    public string LastSuccessAt { get; init; } = "--";
    public string LastFailureAt { get; init; } = "--";
    public int? FailureCount { get; init; }
    public string FailureCountText { get; init; } = "--";
    public string CooldownUntil { get; init; } = "--";
    public string LastError { get; init; } = "--";
    public string UpdatedAt { get; init; } = "--";
    public bool IsNormal { get; init; }
}
