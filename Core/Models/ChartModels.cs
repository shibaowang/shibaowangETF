namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public enum SecurityChartPeriod
{
    Intraday,
    Daily,
    Weekly,
    Monthly
}

public enum SecurityChartSubPanel
{
    Volume,
    Macd
}

public enum ChartInstrumentType
{
    Etf,
    Index
}

public sealed record ChartSecurityInfo(
    string StrategyCode,
    string ActualCode,
    string Name,
    string EastMoneySecId,
    ChartInstrumentType InstrumentType = ChartInstrumentType.Etf);

public sealed class IntradayPoint
{
    public DateTime Time { get; set; }
    public double Price { get; set; }
    public double? AveragePrice { get; set; }
    public double? Volume { get; set; }
    public double? Amount { get; set; }
    public bool IsQuoteTail { get; set; }
    public bool IsQuoteCloseDisplayPoint { get; set; }
    public string? PointSource { get; set; }
}

public sealed class KLinePoint
{
    public DateTime Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double? Volume { get; set; }
    public double? Amount { get; set; }
    public bool IsQuoteAdjusted { get; set; }
    public bool IsDisplayOnly { get; set; }
    public string? PointSource { get; set; }
}

public sealed record MacdPoint(
    DateTime Date,
    double Dif,
    double Dea,
    double Bar);

public sealed record ChartDataStatus(
    bool IsReady,
    string Message,
    bool IsUsingCache = false,
    bool IsRateLimited = false,
    bool IsCircuitOpen = false);

public sealed record SecurityChartSnapshot(
    ChartSecurityInfo Security,
    SecurityChartPeriod Period,
    SecurityChartSubPanel SubPanel,
    MarketQuoteRecord? Quote,
    IReadOnlyList<IntradayPoint> IntradayPoints,
    IReadOnlyList<KLinePoint> KLines,
    IReadOnlyList<MacdPoint> Macd,
    ChartDataStatus MainStatus,
    ChartDataStatus VolumeStatus,
    ChartDataStatus MacdStatus,
    double? ChangePercent,
    double? PreviousClose,
    DateTimeOffset UpdatedAt,
    bool HasQuoteTail);

public sealed record EastMoneyIntradayFetchResult(
    string SecId,
    string RawPayload,
    IReadOnlyList<IntradayPoint> Points,
    DateTimeOffset FetchedAt);
