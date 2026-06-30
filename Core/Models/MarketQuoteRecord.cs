namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketQuoteRecord
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string MarketType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double? Price { get; set; }
    public double? LastClose { get; set; }
    public double? ChangeValue { get; set; }
    public double? ChangePercent { get; set; }
    public double? HighValue { get; set; }
    public double? LowValue { get; set; }
    public double? OpenValue { get; set; }
    public double? Volume { get; set; }
    public double? Amount { get; set; }
    public double? Iopv { get; set; }
    public string? QuoteTime { get; set; }
    public string ReceivedAt { get; set; } = string.Empty;
    public string? RawCode { get; set; }
    public string? RawPayload { get; set; }
}
