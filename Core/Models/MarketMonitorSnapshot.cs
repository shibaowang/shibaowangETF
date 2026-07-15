namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class MarketMonitorSnapshot
{
    public IReadOnlyList<MarketMonitorQuoteRow> QuoteRows { get; init; } = Array.Empty<MarketMonitorQuoteRow>();
    public IReadOnlyList<MarketMonitorSourceRow> SourceRows { get; init; } = Array.Empty<MarketMonitorSourceRow>();
    public int TotalCount { get; init; }
    public int NormalCount { get; init; }
    public int DelayedCount { get; init; }
    public int ExpiredCount { get; init; }
    public int NoDataCount { get; init; }
    public int InvalidTimeCount { get; init; }
    public int NormalSourceCount { get; init; }
    public int AbnormalSourceCount { get; init; }
    public string SourceSummaryText => $"正常 {NormalSourceCount} / 异常 {AbnormalSourceCount}";
    public int ExpiredOrMissingCount => ExpiredCount + NoDataCount + InvalidTimeCount;
    public DateTimeOffset GeneratedAt { get; init; }
}
