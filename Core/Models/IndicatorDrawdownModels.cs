using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record IndicatorDrawdownInstrument(
    string Key,
    string Category,
    string MarketType,
    string Code,
    string Name,
    string StrategyCodes,
    string PreferredHistorySource,
    string PreferredQuoteSource,
    int SortOrder)
{
    public string Symbol => Code;
}

public sealed record IndicatorDrawdownHistoryCandidate(
    long Id,
    string Symbol,
    string MarketType,
    string Source,
    string CacheDate,
    string UpdatedAt,
    string? RawPayload,
    int PayloadLength,
    string MetadataSignature);

public sealed record IndicatorDrawdownHistoryMetadata(
    long Id,
    string Symbol,
    string MarketType,
    string Source,
    string CacheDate,
    string UpdatedAt,
    int PayloadLength,
    string MetadataSignature);

public sealed class IndicatorDrawdownReadModel
{
    public IReadOnlyList<StrategyConfigRecord> Strategies { get; init; } = Array.Empty<StrategyConfigRecord>();
    public IReadOnlyList<IndicatorDrawdownInstrument> Instruments { get; init; } = Array.Empty<IndicatorDrawdownInstrument>();
    public IReadOnlyList<MarketQuoteRecord> Quotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public IReadOnlyList<MarketSourceStatusRecord> SourceStatuses { get; init; } = Array.Empty<MarketSourceStatusRecord>();
    public IReadOnlyList<IndicatorDrawdownHistoryCandidate> HistoryCandidates { get; init; } = Array.Empty<IndicatorDrawdownHistoryCandidate>();
    public IReadOnlyList<IndicatorDrawdownHistoryMetadata> HistoryMetadata { get; init; } = Array.Empty<IndicatorDrawdownHistoryMetadata>();
    public DateTimeOffset ReadAt { get; init; }
    public string? ReadError { get; init; }
}

public sealed class IndicatorDrawdownRealtimeReadModel
{
    public IReadOnlyList<StrategyConfigRecord> Strategies { get; init; } = Array.Empty<StrategyConfigRecord>();
    public IReadOnlyList<IndicatorDrawdownInstrument> Instruments { get; init; } = Array.Empty<IndicatorDrawdownInstrument>();
    public IReadOnlyList<MarketQuoteRecord> Quotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public IReadOnlyList<MarketSourceStatusRecord> SourceStatuses { get; init; } = Array.Empty<MarketSourceStatusRecord>();
    public IReadOnlyList<IndicatorDrawdownHistoryMetadata> HistoryMetadata { get; init; } = Array.Empty<IndicatorDrawdownHistoryMetadata>();
    public DateTimeOffset ReadAt { get; init; }
    public string? ReadError { get; init; }
}

public sealed record IndicatorDrawdownRow
{
    public string Key { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string StrategyCodes { get; init; } = string.Empty;
    public double? LatestPrice { get; init; }
    public double? HistoricalMaximumClose { get; init; }
    public DateTime? HistoricalMaximumDate { get; init; }
    public double? HistoricalHighClose => HistoricalMaximumClose;
    public DateTime? HistoricalHighDate => HistoricalMaximumDate;
    public double? CurrentDrawdown { get; init; }
    public double? Drawdown20 { get; init; }
    public double? Drawdown60 { get; init; }
    public double? Drawdown120 { get; init; }
    public double? Drawdown252 { get; init; }
    public double? YearToDateDrawdown { get; init; }
    public double? DrawdownYtd => YearToDateDrawdown;
    public double? MaximumDrawdown { get; init; }
    public DateTime? MaximumDrawdownPeakDate { get; init; }
    public DateTime? MaximumDrawdownTroughDate { get; init; }
    public int HistoricalPointCount { get; init; }
    public DateTime? HistoryStartDate { get; init; }
    public DateTime? HistoryEndDate { get; init; }
    public string HistorySource { get; init; } = string.Empty;
    public string QuoteSource { get; init; } = string.Empty;
    public string HistorySignature { get; init; } = string.Empty;
    public string HistoryMetadataSignature { get; init; } = string.Empty;
    public string HistoryState { get; init; } = "无历史";
    public string HistorySelectionNote { get; init; } = string.Empty;
    public bool IsHistoryStale { get; init; }
    public bool IsHistoryInsufficient { get; init; }
    public bool IsDataInsufficient => IsHistoryInsufficient;
    public string DataStatus { get; init; } = "无历史";
    public string DataStatusDetail { get; init; } = string.Empty;
    public string QuoteTime { get; init; } = string.Empty;
    public string ReceivedAt { get; init; } = string.Empty;
    public string QuoteReceivedAt => ReceivedAt;
    public string QuoteFreshnessStatus { get; init; } = "无数据";
    public bool IsNewHigh { get; init; }
    public bool HasValidHistory => HistoricalPointCount > 0 && HistoryState == "正常";
    public bool HasRealtimeQuote => LatestPrice.HasValue;
    public string LatestPriceText { get; init; } = "--";
    public string HistoricalMaximumCloseText { get; init; } = "--";
    public string HistoricalMaximumDateText { get; init; } = "--";
    public string CurrentDrawdownText { get; init; } = "--";
    public string Drawdown20Text { get; init; } = "--";
    public string Drawdown60Text { get; init; } = "--";
    public string Drawdown120Text { get; init; } = "--";
    public string Drawdown252Text { get; init; } = "--";
    public string YearToDateDrawdownText { get; init; } = "--";
    public string MaximumDrawdownText { get; init; } = "--";
    public string MaximumDrawdownIntervalText { get; init; } = "--";
    public string HistoricalDataText { get; init; } = "--";
    public string DataSourceText { get; init; } = "--";
    public string PeriodDataToolTip { get; init; } = string.Empty;
    public string HistoricalHighCloseText => HistoricalMaximumCloseText;
    public string HistoricalHighDateText => HistoricalMaximumDateText;
    public string DrawdownYtdText => YearToDateDrawdownText;
    public string HistoryRangeText => HistoryStartDate.HasValue && HistoryEndDate.HasValue
        ? $"{HistoryStartDate:yyyy-MM-dd} 至 {HistoryEndDate:yyyy-MM-dd}"
        : "--";
    public string HistoryPointCountText => HistoricalPointCount.ToString(CultureInfo.InvariantCulture);
    public string SourceText => DataSourceText;
    public string StatusText => DataStatus;
}

public sealed class IndicatorDrawdownSnapshot
{
    public IReadOnlyList<IndicatorDrawdownRow> Rows { get; init; } = Array.Empty<IndicatorDrawdownRow>();
    public IReadOnlyList<IndicatorDrawdownRow> FilteredRows { get; init; } = Array.Empty<IndicatorDrawdownRow>();
    public int TotalCount { get; init; }
    public int NormalCount { get; init; }
    public int InsufficientCount { get; init; }
    public int StaleCount { get; init; }
    public int MissingOrCorruptCount { get; init; }
    public int CorruptCount { get; init; }
    public int NoHistoryCount { get; init; }
    public int SourceErrorCount { get; init; }
    public int CooldownCount { get; init; }
    public int RateLimitCount { get; init; }
    public int MissingRealtimeCount { get; init; }
    public int AbnormalOrMissingCount { get; init; }
    public string AbnormalOrMissingToolTip { get; init; } = string.Empty;
    public string DeepestCurrentCode { get; init; } = string.Empty;
    public double? DeepestCurrentDrawdown { get; init; }
    public string DeepestMaximumCode { get; init; } = string.Empty;
    public double? DeepestMaximumDrawdown { get; init; }
    public string DeepestCurrentText { get; init; } = "--";
    public string DeepestMaximumText { get; init; } = "--";
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset HistoryCheckedAt { get; init; }
    public IReadOnlyDictionary<string, string> HistorySignatures { get; init; } = new Dictionary<string, string>();
    public string LocalReadTimeText => GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string HistoryCheckTimeText => HistoryCheckedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
