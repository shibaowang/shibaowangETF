namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class RuntimeLogRecord
{
    public long Id { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
