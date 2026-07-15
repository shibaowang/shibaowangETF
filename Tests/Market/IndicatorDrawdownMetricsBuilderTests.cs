using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class IndicatorDrawdownMetricsBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly IndicatorDrawdownMetricsBuilder _builder = new();

    [Fact]
    public void CurrentDrawdown_UsesLatestQuoteAgainstHigherHistoricalMaximum()
        => Assert.Equal(-0.2, IndicatorDrawdownMetricsBuilder.CalculateCurrentDrawdown(80, 100)!.Value, 10);

    [Fact]
    public void CurrentDrawdown_NewHighUsesLatestQuoteAsDenominator()
        => Assert.Equal(0, IndicatorDrawdownMetricsBuilder.CalculateCurrentDrawdown(120, 100)!.Value, 10);

    [Fact]
    public void CurrentDrawdown_EqualHighIsZero()
        => Assert.Equal(0, IndicatorDrawdownMetricsBuilder.CalculateCurrentDrawdown(100, 100)!.Value, 10);

    [Theory]
    [InlineData(null, 100d)]
    [InlineData(80d, null)]
    [InlineData(0d, 100d)]
    [InlineData(double.NaN, 100d)]
    public void CurrentDrawdown_InvalidInputReturnsNull(double? quote, double? maximum)
        => Assert.Null(IndicatorDrawdownMetricsBuilder.CalculateCurrentDrawdown(quote, maximum));

    [Fact]
    public void MaximumDrawdown_UsesRunningClosePeakAndFirstStrictPeak()
    {
        MarketHistoryPoint[] points = Points((1, 100d), (2, 100d), (3, 75d), (4, 120d));

        var result = IndicatorDrawdownMetricsBuilder.CalculateMaximumDrawdown(points);

        Assert.Equal(-0.25, result.Drawdown!.Value, 10);
        Assert.Equal(new DateTime(2026, 1, 1), result.PeakDate);
        Assert.Equal(new DateTime(2026, 1, 3), result.TroughDate);
    }

    [Fact]
    public void MaximumDrawdown_EqualDeepestDrawdownKeepsFirstTrough()
    {
        MarketHistoryPoint[] points = Points((1, 100d), (2, 80d), (3, 100d), (4, 80d));

        var result = IndicatorDrawdownMetricsBuilder.CalculateMaximumDrawdown(points);

        Assert.Equal(-0.2, result.Drawdown!.Value, 10);
        Assert.Equal(new DateTime(2026, 1, 2), result.TroughDate);
    }

    [Fact]
    public void MaximumDrawdown_MonotonicIncreaseReturnsZeroWithoutInterval()
    {
        var result = IndicatorDrawdownMetricsBuilder.CalculateMaximumDrawdown(Points((1, 10d), (2, 11d), (3, 12d)));

        Assert.Equal(0, result.Drawdown);
        Assert.Null(result.PeakDate);
        Assert.Null(result.TroughDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MaximumDrawdown_FewerThanTwoPointsReturnsNull(int count)
    {
        var result = IndicatorDrawdownMetricsBuilder.CalculateMaximumDrawdown(GeneratePoints(count));

        Assert.Null(result.Drawdown);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(252)]
    public void PeriodDrawdown_UsesLastNValidHistoricalCloses(int period)
    {
        MarketHistoryPoint[] points = GeneratePoints(300, index => index == 299 ? 50 : 100 + index % 10);

        double? drawdown = IndicatorDrawdownMetricsBuilder.CalculatePeriodDrawdown(points, period);

        Assert.NotNull(drawdown);
        Assert.True(drawdown < 0);
    }

    [Fact]
    public void PeriodDrawdown_InsufficientHistoryUsesAllAvailableRealPoints()
    {
        double? drawdown = IndicatorDrawdownMetricsBuilder.CalculatePeriodDrawdown(Points((1, 10d), (2, 8d)), 252);

        Assert.Equal(-0.2, drawdown!.Value, 10);
    }

    [Fact]
    public void PeriodDrawdown_DoesNotUseRealtimeQuote()
    {
        IndicatorDrawdownRow row = BuildRow(history: GeneratePoints(260, _ => 100), quotePrice: 50);

        Assert.Equal(0, row.Drawdown252);
        Assert.Equal(-0.5, row.CurrentDrawdown);
    }

    [Fact]
    public void YearToDateDrawdown_UsesYearOfLastHistoryPoint()
    {
        MarketHistoryPoint[] points =
        {
            Point(new DateTime(2025, 12, 31), 200),
            Point(new DateTime(2026, 1, 2), 100),
            Point(new DateTime(2026, 2, 2), 80)
        };

        Assert.Equal(-0.2, IndicatorDrawdownMetricsBuilder.CalculateYearToDateDrawdown(points)!.Value, 10);
    }

    [Fact]
    public void YearToDateDrawdown_OnePointIsZero()
        => Assert.Equal(0, IndicatorDrawdownMetricsBuilder.CalculateYearToDateDrawdown(new[] { Point(new DateTime(2026, 1, 2), 100) }));

    [Fact]
    public void NormalizePoints_SortsDeduplicatesAndKeepsLastValidCloseForDate()
    {
        MarketHistoryPoint[] source =
        {
            Point(new DateTime(2026, 1, 2), 20),
            Point(new DateTime(2026, 1, 1), 10),
            Point(new DateTime(2026, 1, 2), 30),
            Point(new DateTime(2026, 1, 3), double.NaN)
        };

        MarketHistoryPoint[] result = IndicatorDrawdownMetricsBuilder.NormalizePoints(source);

        Assert.Equal(2, result.Length);
        Assert.Equal(10, result[0].Close);
        Assert.Equal(30, result[1].Close);
    }

    [Fact]
    public void Build_HistoricalMaximumUsesCloseNotIntradayHigh()
    {
        MarketHistoryPoint[] points = GeneratePoints(252, _ => 100);
        points[20].High = 999;

        IndicatorDrawdownRow row = BuildRow(points, 90);

        Assert.Equal(100, row.HistoricalMaximumClose);
    }

    [Fact]
    public void Build_NewHighTextIsExplicitAndDoesNotChangeHistoricalMaximum()
    {
        IndicatorDrawdownRow row = BuildRow(GeneratePoints(252, _ => 100), 120);

        Assert.Equal("0.00%（创新高）", row.CurrentDrawdownText);
        Assert.Equal(100, row.HistoricalMaximumClose);
    }

    [Fact]
    public void Build_NoQuoteLeavesLatestAndCurrentDrawdownEmpty()
    {
        IndicatorDrawdownRow row = BuildRow(GeneratePoints(252, _ => 100), null);

        Assert.Equal("--", row.LatestPriceText);
        Assert.Equal("--", row.CurrentDrawdownText);
        Assert.Equal("实时行情缺失", row.DataStatus);
    }

    [Fact]
    public void Build_InsufficientHistoryUsesSafeFallbackAndReportsStatus()
    {
        IndicatorDrawdownRow row = BuildRow(GeneratePoints(25, startDate: Now.Date.AddDays(-24)), 25);

        Assert.Equal("数据不足", row.DataStatus);
        Assert.Contains("25 / 252", row.PeriodDataToolTip, StringComparison.Ordinal);
        Assert.NotEqual("--", row.Drawdown252Text);
    }

    [Fact]
    public void Build_StaleHistoryUsesQuoteDateAsReference()
    {
        DateTime lastDate = Now.Date.AddDays(-8);
        IndicatorDrawdownRow row = BuildRow(
            GeneratePoints(252, index => 100 + index, lastDate.AddDays(-251)),
            400,
            quoteTime: Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        Assert.Equal("历史滞后", row.DataStatus);
    }

    [Fact]
    public void Build_SevenDayHistoryGapIsNotStale()
    {
        DateTime lastDate = Now.Date.AddDays(-7);
        IndicatorDrawdownRow row = BuildRow(
            GeneratePoints(252, index => 100 + index, lastDate.AddDays(-251)),
            400,
            quoteTime: Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        Assert.NotEqual("历史滞后", row.DataStatus);
    }

    [Theory]
    [InlineData("ERROR", "数据源异常")]
    [InlineData("COOLDOWN", "熔断冷却")]
    [InlineData("RATE_LIMIT", "限频")]
    public void Build_SourceStateUsesLockedPriorityLabels(string sourceState, string expected)
    {
        IndicatorDrawdownRow row = BuildRow(
            GeneratePoints(252),
            252,
            statuses: new[] { Status(MarketSources.TencentHistory, sourceState) });

        Assert.Equal(expected, row.DataStatus);
    }

    [Fact]
    public void Build_CorruptHistoryOutranksSourceError()
    {
        var corrupt = Candidate("159941", "ETF", MarketSources.TencentHistory, "not-json", 2, "2026-07-15 09:00:00");
        IndicatorDrawdownRow row = _builder.Build(
            Etf(),
            new[] { corrupt },
            new[] { Quote("159941", "ETF", MarketSources.Tencent, 1.5) },
            new[] { Status(MarketSources.TencentHistory, "ERROR") },
            Now);

        Assert.Equal("数据损坏", row.DataStatus);
    }

    [Fact]
    public void Build_NoHistoryOutranksMissingRealtimeQuote()
    {
        IndicatorDrawdownRow row = _builder.Build(Etf(), Array.Empty<IndicatorDrawdownHistoryCandidate>(), Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketSourceStatusRecord>(), Now);

        Assert.Equal("无历史", row.DataStatus);
    }

    [Fact]
    public void Build_NewerCorruptCandidateFallsBackToOlderCompleteCandidate()
    {
        IndicatorDrawdownHistoryCandidate newer = Candidate("159941", "ETF", MarketSources.TencentHistory, "broken", 9, "2026-07-15 10:00:00");
        IndicatorDrawdownHistoryCandidate older = Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252)), 8, "2026-07-15 09:00:00");

        IndicatorDrawdownRow row = _builder.Build(Etf(), new[] { newer, older }, new[] { Quote("159941", "ETF", MarketSources.Tencent, 252) }, Array.Empty<MarketSourceStatusRecord>(), Now);

        Assert.Equal(252, row.HistoricalPointCount);
        Assert.Contains("回退", row.HistorySelectionNote, StringComparison.Ordinal);
        Assert.Contains("|8|", row.HistorySignature, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PreferredHistorySourceWinsOverNewerFallbackSource()
    {
        IndicatorDrawdownHistoryCandidate preferred = Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252, _ => 100)), 1, "2026-07-14 10:00:00");
        IndicatorDrawdownHistoryCandidate fallback = Candidate("159941", "ETF", MarketSources.EastMoneyHistory, Payload(GeneratePoints(252, _ => 200)), 2, "2026-07-15 10:00:00");

        IndicatorDrawdownRow row = _builder.Build(Etf(), new[] { fallback, preferred }, new[] { Quote("159941", "ETF", MarketSources.Tencent, 100) }, Array.Empty<MarketSourceStatusRecord>(), Now);

        Assert.Equal(MarketSources.TencentHistory, row.HistorySource);
        Assert.Equal(100, row.HistoricalMaximumClose);
    }

    [Fact]
    public void Build_CorruptPreferredHistoryFallsBackToOneCompleteAlternateRealSourceWithoutMerging()
    {
        IndicatorDrawdownHistoryCandidate corruptPreferred = Candidate(
            "159941", "ETF", MarketSources.TencentHistory, "broken", 2, "2026-07-15 10:00:00");
        MarketHistoryPoint[] fallbackPoints = Points((1, 100d), (2, 80d));
        IndicatorDrawdownHistoryCandidate completeFallback = Candidate(
            "159941", "ETF", MarketSources.EastMoneyHistory, Payload(fallbackPoints), 1, "2026-07-15 09:00:00");

        IndicatorDrawdownRow row = _builder.Build(
            Etf(),
            new[] { corruptPreferred, completeFallback },
            new[] { Quote("159941", "ETF", MarketSources.Tencent, 80) },
            Array.Empty<MarketSourceStatusRecord>(),
            Now);

        Assert.Equal(MarketSources.EastMoneyHistory, row.HistorySource);
        Assert.Equal(2, row.HistoricalPointCount);
        Assert.Equal(-0.2, row.MaximumDrawdown!.Value, 10);
    }

    [Fact]
    public void Build_PreferredQuoteSourceWinsAndFieldsAreNotMixed()
    {
        MarketQuoteRecord preferred = Quote("159941", "ETF", MarketSources.Tencent, 100, receivedAt: "2026-07-15 09:00:00");
        preferred.LastClose = null;
        MarketQuoteRecord fallback = Quote("159941", "ETF", MarketSources.EastMoney, 200, receivedAt: "2026-07-15 10:00:00");
        fallback.LastClose = 150;

        IndicatorDrawdownRow row = _builder.Build(Etf(), new[] { Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252, _ => 100))) }, new[] { fallback, preferred }, Array.Empty<MarketSourceStatusRecord>(), Now);

        Assert.Equal(MarketSources.Tencent, row.QuoteSource);
        Assert.Equal(100, row.LatestPrice);
    }

    [Fact]
    public void Build_InvalidPreferredQuoteFallsBackToAnotherCompleteRealCacheRecord()
    {
        MarketQuoteRecord invalidPreferred = Quote("159941", "ETF", MarketSources.Tencent, double.NaN);
        MarketQuoteRecord validFallback = Quote("159941", "ETF", MarketSources.EastMoney, 88);

        IndicatorDrawdownRow row = _builder.Build(
            Etf(),
            new[] { Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252, _ => 100))) },
            new[] { invalidPreferred, validFallback },
            Array.Empty<MarketSourceStatusRecord>(),
            Now);

        Assert.Equal(88, row.LatestPrice);
        Assert.Equal(MarketSources.EastMoney, row.QuoteSource);
    }

    [Fact]
    public void Build_EtfNamePrefersSelectedRealQuoteDisplayName()
    {
        MarketQuoteRecord quote = Quote("159941", "ETF", MarketSources.Tencent, 100);
        quote.DisplayName = "真实行情名称";

        IndicatorDrawdownRow row = _builder.Build(
            Etf(),
            new[] { Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252, _ => 100))) },
            new[] { quote },
            Array.Empty<MarketSourceStatusRecord>(),
            Now);

        Assert.Equal("真实行情名称", row.Name);
    }

    [Fact]
    public void Build_EtfNameFallsBackToStrategyNameWhenQuoteNameIsMissing()
    {
        IndicatorDrawdownRow row = BuildRow(GeneratePoints(252, _ => 100), 100);

        Assert.Equal("纳指ETF广发", row.Name);
    }

    [Fact]
    public void Build_EtfWithoutQuoteOrStrategyNameUsesLockedPlaceholder()
    {
        IndicatorDrawdownInstrument instrument = Etf() with { Name = "--" };

        IndicatorDrawdownRow row = _builder.Build(
            instrument,
            new[] { Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(GeneratePoints(252, _ => 100))) },
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketSourceStatusRecord>(),
            Now);

        Assert.Equal("--", row.Name);
    }

    [Fact]
    public void Build_QuoteFreshnessIsExposedWithoutChangingHistoricalMetrics()
    {
        IndicatorDrawdownRow row = BuildRow(GeneratePoints(252, _ => 100), 100);

        Assert.Equal("正常", row.QuoteFreshnessStatus);
        Assert.True(row.HasRealtimeQuote);
        Assert.True(row.HasValidHistory);
    }

    [Fact]
    public void Build_StatusDetailRetainsSourceFailureCooldownAndLastErrorAuditFields()
    {
        var sourceStatus = new MarketSourceStatusRecord
        {
            Id = 2,
            Source = MarketSources.TencentHistory,
            Status = "COOLDOWN",
            FailureCount = 3,
            CooldownUntil = "2026-07-15 10:10:00",
            LastError = "HTTP 429",
            UpdatedAt = "2026-07-15 10:00:00"
        };

        IndicatorDrawdownRow row = BuildRow(GeneratePoints(252), 252, statuses: new[] { sourceStatus });

        Assert.Contains("FailureCount=3", row.DataStatusDetail, StringComparison.Ordinal);
        Assert.Contains("CooldownUntil=2026-07-15 10:10:00", row.DataStatusDetail, StringComparison.Ordinal);
        Assert.Contains("LastError=HTTP 429", row.DataStatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SourceErrorOutranksCooldownAndRateLimitAcrossRelevantSources()
    {
        IndicatorDrawdownRow row = BuildRow(
            GeneratePoints(252),
            252,
            statuses: new[]
            {
                Status(MarketSources.TencentHistory, "ERROR"),
                Status(MarketSources.Tencent, "COOLDOWN"),
                Status(MarketSources.EastMoney, "RATE_LIMIT")
            });

        Assert.Equal("数据源异常", row.DataStatus);
    }

    [Fact]
    public void BuildFullHistorySignature_ContainsIdentityLengthAndSha256()
    {
        IndicatorDrawdownHistoryCandidate candidate = Candidate("159941", "ETF", MarketSources.TencentHistory, "payload", 42, "2026-07-15 10:00:00");

        string signature = IndicatorDrawdownMetricsBuilder.BuildFullHistorySignature(candidate);

        Assert.Contains("TENCENT_DAILY_QFQ|ETF|159941|42|2026-07-15 10:00:00|7|", signature, StringComparison.Ordinal);
        Assert.Equal(64, signature.Split('|')[^1].Length);
    }

    [Fact]
    public void BuildFullHistorySignature_ChangesForSameLengthPayloadContent()
    {
        IndicatorDrawdownHistoryCandidate first = Candidate("159941", "ETF", MarketSources.TencentHistory, "payload-a", 42, "2026-07-15 10:00:00");
        IndicatorDrawdownHistoryCandidate second = Candidate("159941", "ETF", MarketSources.TencentHistory, "payload-b", 42, "2026-07-15 10:00:00");

        Assert.Equal(first.PayloadLength, second.PayloadLength);
        Assert.NotEqual(
            IndicatorDrawdownMetricsBuilder.BuildFullHistorySignature(first),
            IndicatorDrawdownMetricsBuilder.BuildFullHistorySignature(second));
    }

    [Fact]
    public void BuildFullHistorySignature_IsStableForIdenticalRecord()
    {
        IndicatorDrawdownHistoryCandidate candidate = Candidate("159941", "ETF", MarketSources.TencentHistory, "payload", 42, "2026-07-15 10:00:00");

        Assert.Equal(
            IndicatorDrawdownMetricsBuilder.BuildFullHistorySignature(candidate),
            IndicatorDrawdownMetricsBuilder.BuildFullHistorySignature(candidate));
    }

    [Fact]
    public void RefreshRealtime_ChangesOnlyRealtimeFieldsAndPreservesHistoricalMetrics()
    {
        IndicatorDrawdownRow original = BuildRow(GeneratePoints(252, _ => 100), 90);

        IndicatorDrawdownRow refreshed = _builder.RefreshRealtime(
            original,
            Etf(),
            new[] { Quote("159941", "ETF", MarketSources.Tencent, 80) },
            Array.Empty<MarketSourceStatusRecord>(),
            Now.AddSeconds(2));

        Assert.Equal(80, refreshed.LatestPrice);
        Assert.Equal(-0.2, refreshed.CurrentDrawdown!.Value, 10);
        Assert.Equal(original.MaximumDrawdown, refreshed.MaximumDrawdown);
        Assert.Equal(original.Drawdown252, refreshed.Drawdown252);
        Assert.Equal(original.HistorySignature, refreshed.HistorySignature);
    }

    private IndicatorDrawdownRow BuildRow(
        MarketHistoryPoint[] history,
        double? quotePrice,
        string? quoteTime = null,
        IReadOnlyList<MarketSourceStatusRecord>? statuses = null)
    {
        IndicatorDrawdownHistoryCandidate candidate = Candidate("159941", "ETF", MarketSources.TencentHistory, Payload(history));
        MarketQuoteRecord[] quotes = quotePrice.HasValue
            ? new[] { Quote("159941", "ETF", MarketSources.Tencent, quotePrice.Value, quoteTime: quoteTime) }
            : Array.Empty<MarketQuoteRecord>();
        return _builder.Build(Etf(), new[] { candidate }, quotes, statuses ?? Array.Empty<MarketSourceStatusRecord>(), Now);
    }

    private static IndicatorDrawdownInstrument Etf()
        => new(
            "ETF|159941", "场内 ETF", "ETF", "159941", "纳指ETF广发", "159941",
            MarketSources.TencentHistory, MarketSources.Tencent, 2);

    private static IndicatorDrawdownHistoryCandidate Candidate(
        string symbol,
        string marketType,
        string source,
        string payload,
        long id = 1,
        string updatedAt = "2026-07-15 09:00:00")
        => new(id, symbol, marketType, source, "2026-07-15", updatedAt, payload, payload.Length, $"{source}|{marketType}|{symbol}|{id}|{updatedAt}|{payload.Length}");

    private static MarketQuoteRecord Quote(
        string symbol,
        string marketType,
        string source,
        double price,
        string? quoteTime = null,
        string receivedAt = "2026-07-15 10:00:00")
        => new()
        {
            Id = 1,
            Symbol = symbol,
            MarketType = marketType,
            Source = source,
            Price = price,
            QuoteTime = quoteTime ?? "2026-07-15 10:00:00",
            ReceivedAt = receivedAt
        };

    private static MarketSourceStatusRecord Status(string source, string status)
        => new() { Id = 1, Source = source, Status = status, UpdatedAt = "2026-07-15 10:00:00" };

    private static MarketHistoryPoint[] Points(params (int Day, double Close)[] values)
        => values.Select(value => Point(new DateTime(2026, 1, value.Day), value.Close)).ToArray();

    private static MarketHistoryPoint[] GeneratePoints(
        int count,
        Func<int, double>? closeFactory = null,
        DateTime? startDate = null)
        => Enumerable.Range(0, count)
            .Select(index => Point((startDate ?? new DateTime(2025, 11, 6)).AddDays(index), closeFactory?.Invoke(index) ?? index + 1))
            .ToArray();

    private static MarketHistoryPoint Point(DateTime date, double close)
        => new() { Date = date, Open = close, Close = close, High = close, Low = close };

    private static string Payload(IEnumerable<MarketHistoryPoint> points)
        => "{\"data\":{\"klines\":[" + string.Join(",", points.Select(point =>
            $"\"{point.Date:yyyy-MM-dd},{point.Open.ToString(CultureInfo.InvariantCulture)},{point.Close.ToString(CultureInfo.InvariantCulture)},{point.High.ToString(CultureInfo.InvariantCulture)},{point.Low.ToString(CultureInfo.InvariantCulture)},0,0\"")) + "]}}";
}
