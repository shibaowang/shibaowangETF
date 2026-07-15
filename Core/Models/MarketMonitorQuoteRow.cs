namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketMonitorQuoteRow
{
    public string Category { get; init; } = string.Empty;
    public string FilterGroup { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string StrategyCodes { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public double? Price { get; init; }
    public double? ChangeValue { get; init; }
    public double? ChangePercent { get; init; }
    public double? LastClose { get; init; }
    public double? OpenValue { get; init; }
    public double? HighValue { get; init; }
    public double? LowValue { get; init; }
    public double? Volume { get; init; }
    public double? Amount { get; init; }
    public double? Iopv { get; init; }
    public string PriceText { get; init; } = "--";
    public string ChangeValueText { get; init; } = "--";
    public string ChangePercentText { get; init; } = "--";
    public string LastCloseText { get; init; } = "--";
    public string OpenText { get; init; } = "--";
    public string HighText { get; init; } = "--";
    public string LowText { get; init; } = "--";
    public string VolumeText { get; init; } = "--";
    public string VolumeFullText { get; init; } = "--";
    public string AmountText { get; init; } = "--";
    public string AmountFullText { get; init; } = "--";
    public string IopvText { get; init; } = "--";
    public string QuoteTime { get; init; } = "--";
    public string ReceivedAt { get; init; } = "--";
    public string FreshnessStatus { get; init; } = "无数据";
    public string CacheAge { get; init; } = "--";
    public string TrendStatus { get; init; } = "未知";
}
