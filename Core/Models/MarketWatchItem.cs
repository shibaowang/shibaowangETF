namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketWatchItem
{
    public string Symbol { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MarketType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string RawCode { get; set; } = string.Empty;
}
