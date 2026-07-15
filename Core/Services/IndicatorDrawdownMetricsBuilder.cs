using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class IndicatorDrawdownMetricsBuilder
{
    private const int FullHistoryTargetCount = 252;

    public IndicatorDrawdownRow Build(
        IndicatorDrawdownInstrument instrument,
        IReadOnlyList<IndicatorDrawdownHistoryCandidate> allHistoryCandidates,
        IReadOnlyList<MarketQuoteRecord> allQuotes,
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        HistorySelection selection = SelectHistory(instrument, allHistoryCandidates);
        MarketQuoteRecord? quote = SelectQuote(instrument, allQuotes, now.Offset);
        string displayName = ResolveInstrumentName(instrument, quote);
        MarketHistoryPoint[] points = selection.Points;
        double? maximumClose = points.Length == 0 ? null : points.Max(point => point.Close);
        DateTime? maximumDate = maximumClose.HasValue
            ? points.First(point => point.Close.Equals(maximumClose.Value)).Date.Date
            : null;
        double? currentDrawdown = CalculateCurrentDrawdown(quote?.Price, maximumClose);
        (double? maximumDrawdown, DateTime? peakDate, DateTime? troughDate) = CalculateMaximumDrawdown(points);
        DateTime? historyEnd = points.LastOrDefault()?.Date.Date;
        bool stale = IsHistoryStale(historyEnd, quote, now);
        bool insufficient = points.Length > 0 && points.Length < FullHistoryTargetCount;
        string relevantHistorySource = string.IsNullOrWhiteSpace(selection.Source)
            ? instrument.PreferredHistorySource
            : selection.Source;
        string relevantQuoteSource = string.IsNullOrWhiteSpace(quote?.Source)
            ? instrument.PreferredQuoteSource
            : quote.Source;
        string status = ResolveDataStatus(
            selection.State,
            stale,
            insufficient,
            quote,
            relevantHistorySource,
            relevantQuoteSource,
            sourceStatuses);
        string statusDetail = BuildStatusDetail(
            status,
            selection,
            points.Length,
            historyEnd,
            quote,
            relevantHistorySource,
            relevantQuoteSource,
            sourceStatuses,
            now);

        return new IndicatorDrawdownRow
        {
            Key = instrument.Key,
            Category = instrument.Category,
            MarketType = instrument.MarketType,
            Name = displayName,
            Code = instrument.Code,
            StrategyCodes = instrument.StrategyCodes,
            LatestPrice = ValidPrice(quote?.Price),
            HistoricalMaximumClose = maximumClose,
            HistoricalMaximumDate = maximumDate,
            CurrentDrawdown = currentDrawdown,
            Drawdown20 = CalculatePeriodDrawdown(points, 20),
            Drawdown60 = CalculatePeriodDrawdown(points, 60),
            Drawdown120 = CalculatePeriodDrawdown(points, 120),
            Drawdown252 = CalculatePeriodDrawdown(points, 252),
            YearToDateDrawdown = CalculateYearToDateDrawdown(points),
            MaximumDrawdown = maximumDrawdown,
            MaximumDrawdownPeakDate = peakDate,
            MaximumDrawdownTroughDate = troughDate,
            HistoricalPointCount = points.Length,
            HistoryStartDate = points.FirstOrDefault()?.Date.Date,
            HistoryEndDate = historyEnd,
            HistorySource = selection.Source,
            QuoteSource = quote?.Source ?? string.Empty,
            HistorySignature = selection.FullSignature,
            HistoryMetadataSignature = selection.MetadataSignature,
            HistoryState = selection.State,
            HistorySelectionNote = selection.Note,
            IsHistoryStale = stale,
            IsHistoryInsufficient = insufficient,
            DataStatus = status,
            DataStatusDetail = statusDetail,
            QuoteTime = quote?.QuoteTime?.Trim() ?? string.Empty,
            ReceivedAt = quote?.ReceivedAt?.Trim() ?? string.Empty,
            QuoteFreshnessStatus = MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, now),
            IsNewHigh = ValidPrice(quote?.Price).HasValue
                        && maximumClose.HasValue
                        && quote!.Price!.Value > maximumClose.Value,
            LatestPriceText = FormatPrice(quote?.Price, instrument.MarketType),
            HistoricalMaximumCloseText = FormatPrice(maximumClose, instrument.MarketType),
            HistoricalMaximumDateText = FormatDate(maximumDate),
            CurrentDrawdownText = FormatCurrentDrawdown(currentDrawdown, quote?.Price, maximumClose),
            Drawdown20Text = FormatPercent(CalculatePeriodDrawdown(points, 20)),
            Drawdown60Text = FormatPercent(CalculatePeriodDrawdown(points, 60)),
            Drawdown120Text = FormatPercent(CalculatePeriodDrawdown(points, 120)),
            Drawdown252Text = FormatPercent(CalculatePeriodDrawdown(points, 252)),
            YearToDateDrawdownText = FormatPercent(CalculateYearToDateDrawdown(points)),
            MaximumDrawdownText = FormatPercent(maximumDrawdown),
            MaximumDrawdownIntervalText = maximumDrawdown is 0 or null
                ? "--"
                : $"{FormatDate(peakDate)} → {FormatDate(troughDate)}",
            HistoricalDataText = FormatHistoryData(points),
            DataSourceText = FormatDataSources(selection.Source, quote?.Source),
            PeriodDataToolTip = BuildPeriodToolTip(points.Length)
        };
    }

    public IndicatorDrawdownRow RefreshRealtime(
        IndicatorDrawdownRow current,
        IndicatorDrawdownInstrument instrument,
        IReadOnlyList<MarketQuoteRecord> allQuotes,
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(instrument);
        MarketQuoteRecord? quote = SelectQuote(instrument, allQuotes, now.Offset);
        double? latestPrice = ValidPrice(quote?.Price);
        double? currentDrawdown = CalculateCurrentDrawdown(latestPrice, current.HistoricalMaximumClose);
        bool stale = IsHistoryStale(current.HistoryEndDate, quote, now);
        string relevantHistorySource = string.IsNullOrWhiteSpace(current.HistorySource)
            ? instrument.PreferredHistorySource
            : current.HistorySource;
        string relevantQuoteSource = string.IsNullOrWhiteSpace(quote?.Source)
            ? instrument.PreferredQuoteSource
            : quote.Source;
        string status = ResolveDataStatus(
            current.HistoryState,
            stale,
            current.IsHistoryInsufficient,
            quote,
            relevantHistorySource,
            relevantQuoteSource,
            sourceStatuses);
        var selection = new HistorySelection(
            Array.Empty<MarketHistoryPoint>(),
            current.HistorySource,
            current.HistoryState,
            current.HistorySelectionNote,
            current.HistorySignature,
            current.HistoryMetadataSignature);

        return current with
        {
            Name = ResolveInstrumentName(instrument, quote),
            StrategyCodes = instrument.StrategyCodes,
            LatestPrice = latestPrice,
            CurrentDrawdown = currentDrawdown,
            QuoteSource = quote?.Source ?? string.Empty,
            IsHistoryStale = stale,
            DataStatus = status,
            DataStatusDetail = BuildStatusDetail(
                status,
                selection,
                current.HistoricalPointCount,
                current.HistoryEndDate,
                quote,
                relevantHistorySource,
                relevantQuoteSource,
                sourceStatuses,
                now),
            QuoteTime = quote?.QuoteTime?.Trim() ?? string.Empty,
            ReceivedAt = quote?.ReceivedAt?.Trim() ?? string.Empty,
            QuoteFreshnessStatus = MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, now),
            IsNewHigh = latestPrice.HasValue
                        && current.HistoricalMaximumClose.HasValue
                        && latestPrice.Value > current.HistoricalMaximumClose.Value,
            LatestPriceText = FormatPrice(latestPrice, instrument.MarketType),
            CurrentDrawdownText = FormatCurrentDrawdown(currentDrawdown, latestPrice, current.HistoricalMaximumClose),
            DataSourceText = FormatDataSources(current.HistorySource, quote?.Source)
        };
    }

    public static double? CalculateCurrentDrawdown(double? latestPrice, double? historicalMaximumClose)
    {
        double? price = ValidPrice(latestPrice);
        double? maximum = ValidPrice(historicalMaximumClose);
        if (!price.HasValue || !maximum.HasValue)
        {
            return null;
        }

        double denominator = Math.Max(price.Value, maximum.Value);
        return price.Value / denominator - 1.0;
    }

    public static double? CalculatePeriodDrawdown(IReadOnlyList<MarketHistoryPoint> source, int pointCount)
    {
        MarketHistoryPoint[] points = NormalizePoints(source);
        if (points.Length == 0 || pointCount <= 0)
        {
            return null;
        }

        MarketHistoryPoint[] window = points.TakeLast(Math.Min(pointCount, points.Length)).ToArray();
        double maximum = window.Max(point => point.Close);
        return window[^1].Close / maximum - 1.0;
    }

    public static double? CalculateYearToDateDrawdown(IReadOnlyList<MarketHistoryPoint> source)
    {
        MarketHistoryPoint[] points = NormalizePoints(source);
        if (points.Length == 0)
        {
            return null;
        }

        int year = points[^1].Date.Year;
        MarketHistoryPoint[] yearPoints = points.Where(point => point.Date.Year == year).ToArray();
        double maximum = yearPoints.Max(point => point.Close);
        return yearPoints[^1].Close / maximum - 1.0;
    }

    public static (double? Drawdown, DateTime? PeakDate, DateTime? TroughDate) CalculateMaximumDrawdown(
        IReadOnlyList<MarketHistoryPoint> source)
    {
        MarketHistoryPoint[] points = NormalizePoints(source);
        if (points.Length < 2)
        {
            return (null, null, null);
        }

        double peak = points[0].Close;
        DateTime peakDate = points[0].Date.Date;
        double maximumDrawdown = 0;
        DateTime? maximumPeakDate = null;
        DateTime? maximumTroughDate = null;
        foreach (MarketHistoryPoint point in points.Skip(1))
        {
            if (point.Close > peak)
            {
                peak = point.Close;
                peakDate = point.Date.Date;
                continue;
            }

            double drawdown = point.Close / peak - 1.0;
            if (drawdown < maximumDrawdown)
            {
                maximumDrawdown = drawdown;
                maximumPeakDate = peakDate;
                maximumTroughDate = point.Date.Date;
            }
        }

        return maximumDrawdown == 0
            ? (0, null, null)
            : (maximumDrawdown, maximumPeakDate, maximumTroughDate);
    }

    public static MarketHistoryPoint[] NormalizePoints(IEnumerable<MarketHistoryPoint> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Select((point, index) => new { Point = point, Index = index })
            .Where(item => item.Point.Date != default
                           && double.IsFinite(item.Point.Close)
                           && item.Point.Close > 0)
            .GroupBy(item => item.Point.Date.Date)
            .Select(group => group.OrderBy(item => item.Index).Last().Point)
            .OrderBy(point => point.Date)
            .Select(point => new MarketHistoryPoint
            {
                Date = point.Date.Date,
                Open = point.Open,
                Close = point.Close,
                High = point.High,
                Low = point.Low,
                Volume = point.Volume,
                Amount = point.Amount
            })
            .ToArray();
    }

    public static string BuildFullHistorySignature(IndicatorDrawdownHistoryCandidate candidate)
    {
        string payload = candidate.RawPayload ?? string.Empty;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return string.Join("|",
            candidate.Source,
            candidate.MarketType,
            candidate.Symbol,
            candidate.Id.ToString(CultureInfo.InvariantCulture),
            candidate.UpdatedAt,
            candidate.PayloadLength.ToString(CultureInfo.InvariantCulture),
            hash);
    }

    private static HistorySelection SelectHistory(
        IndicatorDrawdownInstrument instrument,
        IReadOnlyList<IndicatorDrawdownHistoryCandidate> allCandidates)
    {
        string preferredSource = ExpectedHistorySource(instrument.MarketType);
        IndicatorDrawdownHistoryCandidate[] candidates = allCandidates
            .Where(candidate => string.Equals(candidate.MarketType, instrument.MarketType, StringComparison.OrdinalIgnoreCase)
                                && SymbolsEqual(candidate.Symbol, instrument.Code, instrument.MarketType)
                                && IsAllowedHistorySource(candidate.Source))
            .OrderBy(candidate => string.Equals(candidate.Source, preferredSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(candidate => ParseTimestampOrMinimum(candidate.UpdatedAt))
            .ThenByDescending(candidate => candidate.Id)
            .ToArray();
        if (candidates.Length == 0)
        {
            return HistorySelection.Missing;
        }

        foreach (IndicatorDrawdownHistoryCandidate candidate in candidates)
        {
            if (!TryParseDailyPoints(candidate, out MarketHistoryPoint[] points))
            {
                continue;
            }

            bool fallback = !ReferenceEquals(candidate, candidates[0]);
            string note = fallback ? "较新候选损坏，已回退到较早的完整真实历史" : string.Empty;
            return new HistorySelection(
                points,
                candidate.Source,
                "正常",
                note,
                BuildFullHistorySignature(candidate),
                candidate.MetadataSignature);
        }

        return new HistorySelection(
            Array.Empty<MarketHistoryPoint>(),
            preferredSource,
            "数据损坏",
            "目标历史候选均无法解析为真实日线收盘序列",
            string.Empty,
            candidates[0].MetadataSignature);
    }

    private static bool TryParseDailyPoints(
        IndicatorDrawdownHistoryCandidate candidate,
        out MarketHistoryPoint[] points)
    {
        points = Array.Empty<MarketHistoryPoint>();
        if (string.IsNullOrWhiteSpace(candidate.RawPayload))
        {
            return false;
        }

        try
        {
            IReadOnlyList<MarketHistoryPoint> parsed;
            if (string.Equals(candidate.Source, MarketSources.TencentHistory, StringComparison.OrdinalIgnoreCase))
            {
                parsed = TencentHistoryParser.ParsePoints(candidate.RawPayload);
                if (parsed.Count == 0)
                {
                    parsed = EastMoneyHistoryParser.ParsePoints(candidate.RawPayload);
                }
            }
            else
            {
                parsed = EastMoneyHistoryParser.ParsePoints(candidate.RawPayload);
            }

            points = NormalizePoints(parsed);
            if (points.Length == 0)
            {
                return false;
            }

            if (points.Length > 1)
            {
                MarketHistoryPoint[] normalizedPoints = points;
                double[] intervals = normalizedPoints.Skip(1)
                    .Select((point, index) => (point.Date - normalizedPoints[index].Date).TotalDays)
                    .Where(days => days > 0)
                    .OrderBy(days => days)
                    .ToArray();
                if (intervals.Length == 0 || Median(intervals) > 7)
                {
                    points = Array.Empty<MarketHistoryPoint>();
                    return false;
                }
            }

            return true;
        }
        catch
        {
            points = Array.Empty<MarketHistoryPoint>();
            return false;
        }
    }

    private static MarketQuoteRecord? SelectQuote(
        IndicatorDrawdownInstrument instrument,
        IReadOnlyList<MarketQuoteRecord> allQuotes,
        TimeSpan localOffset)
    {
        string preferredSource = PreferredQuoteSource(instrument.MarketType);
        return allQuotes
            .Where(quote => string.Equals(quote.MarketType, instrument.MarketType, StringComparison.OrdinalIgnoreCase)
                            && SymbolsEqual(quote.Symbol, instrument.Code, instrument.MarketType)
                            && ValidPrice(quote.Price).HasValue)
            .OrderBy(quote => string.Equals(quote.Source, preferredSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(quote => ParseTimestampOrMinimum(quote.ReceivedAt, localOffset))
            .ThenByDescending(quote => quote.Id)
            .FirstOrDefault();
    }

    private static string ResolveDataStatus(
        string historyState,
        bool stale,
        bool insufficient,
        MarketQuoteRecord? quote,
        string historySource,
        string quoteSource,
        IReadOnlyList<MarketSourceStatusRecord> statuses)
    {
        if (historyState == "数据损坏")
        {
            return "数据损坏";
        }

        if (historyState == "无历史")
        {
            return "无历史";
        }

        string[] relevantSources = { historySource, quoteSource };
        MarketSourceStatusRecord[] relevant = statuses
            .Where(status => relevantSources.Contains(status.Source, StringComparer.OrdinalIgnoreCase))
            .GroupBy(status => status.Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(status => ParseTimestampOrMinimum(status.UpdatedAt)).ThenByDescending(status => status.Id).First())
            .ToArray();
        if (relevant.Any(status => string.Equals(status.Status, "ERROR", StringComparison.OrdinalIgnoreCase)))
        {
            return "数据源异常";
        }

        if (relevant.Any(status => string.Equals(status.Status, "COOLDOWN", StringComparison.OrdinalIgnoreCase)))
        {
            return "熔断冷却";
        }

        if (relevant.Any(status => string.Equals(status.Status, "RATE_LIMIT", StringComparison.OrdinalIgnoreCase)))
        {
            return "限频";
        }

        if (stale)
        {
            return "历史滞后";
        }

        if (insufficient)
        {
            return "数据不足";
        }

        return ValidPrice(quote?.Price).HasValue ? "正常" : "实时行情缺失";
    }

    private static bool IsHistoryStale(DateTime? historyEnd, MarketQuoteRecord? quote, DateTimeOffset now)
    {
        if (!historyEnd.HasValue)
        {
            return false;
        }

        DateTime referenceDate = TryParseTimestamp(quote?.QuoteTime, now.Offset, out DateTimeOffset quoteTime)
            ? quoteTime.Date
            : now.Date;
        return (referenceDate - historyEnd.Value.Date).TotalDays > 7;
    }

    private static string BuildStatusDetail(
        string status,
        HistorySelection selection,
        int pointCount,
        DateTime? historyEnd,
        MarketQuoteRecord? quote,
        string historySource,
        string quoteSource,
        IReadOnlyList<MarketSourceStatusRecord> statuses,
        DateTimeOffset now)
    {
        var details = new List<string>();
        details.Add(status switch
        {
            "数据损坏" => selection.Note,
            "无历史" => "未找到目标标的的真实前复权日线缓存",
            "数据源异常" => "相关真实行情源当前记录为 ERROR",
            "熔断冷却" => "相关真实行情源当前处于 cooldown",
            "限频" => "相关真实行情源当前处于 rate limit",
            "历史滞后" => $"历史截止 {FormatDate(historyEnd)}，与当前参考日期相差 {HistoryGapDays(historyEnd, quote, now)} 个自然日",
            "数据不足" => $"当前仅有 {pointCount} 个有效日线点；周期指标按现有全部真实点降级计算",
            "实时行情缺失" => "未找到有效真实 quote；最新价和当前回撤保持 --",
            _ when selection.Note.Length > 0 => selection.Note,
            _ => string.IsNullOrWhiteSpace(quote?.QuoteTime) ? "真实历史与实时缓存可用" : $"真实行情时间 {quote!.QuoteTime}"
        });
        details.Add($"历史来源：{DisplaySource(historySource)}；行情来源：{DisplaySource(quoteSource)}");
        details.Add($"历史截止：{FormatDate(historyEnd)}；有效点数：{pointCount}");
        details.Add(BuildPeriodToolTip(pointCount));
        string[] relevantSources = { historySource, quoteSource };
        foreach (MarketSourceStatusRecord sourceStatus in statuses
                     .Where(item => relevantSources.Contains(item.Source, StringComparer.OrdinalIgnoreCase))
                     .GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(item => ParseTimestampOrMinimum(item.UpdatedAt)).ThenByDescending(item => item.Id).First()))
        {
            details.Add($"{sourceStatus.Source} status={sourceStatus.Status}，FailureCount={sourceStatus.FailureCount}，CooldownUntil={DisplayValue(sourceStatus.CooldownUntil)}，LastError={DisplayValue(sourceStatus.LastError)}");
        }

        return string.Join("；", details.Where(detail => !string.IsNullOrWhiteSpace(detail)));
    }

    private static string BuildPeriodToolTip(int count)
        => count == 0
            ? "无真实日线数据"
            : string.Join("；", new[] { 20, 60, 120, 252 }.Select(period =>
                $"{period} 日：使用 {Math.Min(count, period)} / {period} 个有效交易点"));

    private static string FormatHistoryData(IReadOnlyList<MarketHistoryPoint> points)
        => points.Count == 0
            ? "--"
            : $"{points.Count}点｜{points[0].Date:yyyy-MM-dd} 至 {points[^1].Date:yyyy-MM-dd}";

    private static string FormatDataSources(string? historySource, string? quoteSource)
    {
        return DisplaySource(historySource) + " / " + DisplaySource(quoteSource);
    }

    private static string ResolveInstrumentName(IndicatorDrawdownInstrument instrument, MarketQuoteRecord? quote)
    {
        if (string.Equals(instrument.MarketType, "ETF", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(quote?.DisplayName))
        {
            return quote.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(instrument.Name) ? "--" : instrument.Name;
    }

    private static string DisplaySource(string? source)
        => source?.Trim().ToUpperInvariant() switch
        {
            MarketSources.TencentHistory => "腾讯前复权日K",
            MarketSources.EastMoneyHistory => "东方财富历史K线",
            MarketSources.Tencent => "腾讯实时",
            MarketSources.EastMoney => "东方财富实时",
            null or "" => "--",
            _ => source!.Trim()
        };

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private static int HistoryGapDays(DateTime? historyEnd, MarketQuoteRecord? quote, DateTimeOffset now)
    {
        if (!historyEnd.HasValue)
        {
            return 0;
        }

        DateTime referenceDate = TryParseTimestamp(quote?.QuoteTime, now.Offset, out DateTimeOffset quoteTime)
            ? quoteTime.Date
            : now.Date;
        return Math.Max(0, (int)(referenceDate - historyEnd.Value.Date).TotalDays);
    }

    private static string FormatCurrentDrawdown(double? drawdown, double? price, double? maximum)
    {
        if (!drawdown.HasValue)
        {
            return "--";
        }

        bool newHigh = ValidPrice(price).HasValue
                       && ValidPrice(maximum).HasValue
                       && price!.Value > maximum!.Value;
        return newHigh ? "0.00%（创新高）" : FormatPercent(drawdown);
    }

    private static string FormatPrice(double? value, string marketType)
    {
        double? finite = ValidPrice(value);
        if (!finite.HasValue)
        {
            return "--";
        }

        return string.Equals(marketType, "INDEX", StringComparison.OrdinalIgnoreCase)
            ? finite.Value.ToString("N2", CultureInfo.CurrentCulture)
            : finite.Value.ToString("#,##0.####", CultureInfo.CurrentCulture);
    }

    private static string FormatPercent(double? value)
        => value.HasValue && double.IsFinite(value.Value)
            ? (value.Value * 100.0).ToString("0.00", CultureInfo.CurrentCulture) + "%"
            : "--";

    private static string FormatDate(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "--";

    private static bool SymbolsEqual(string left, string right, string marketType)
        => string.Equals(
            string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase) ? MarketSymbolNormalizer.DigitsOnly(left) : left.Trim(),
            string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase) ? MarketSymbolNormalizer.DigitsOnly(right) : right.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedHistorySource(string source)
        => string.Equals(source, MarketSources.TencentHistory, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, MarketSources.EastMoneyHistory, StringComparison.OrdinalIgnoreCase);

    private static string ExpectedHistorySource(string marketType)
        => string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase)
            ? MarketSources.TencentHistory
            : MarketSources.EastMoneyHistory;

    private static string PreferredQuoteSource(string marketType)
        => string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase)
            ? MarketSources.Tencent
            : MarketSources.EastMoney;

    private static double? ValidPrice(double? value)
        => value.HasValue && double.IsFinite(value.Value) && value.Value > 0 ? value : null;

    private static double Median(double[] values)
    {
        int middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
    }

    private static DateTimeOffset ParseTimestampOrMinimum(string? value, TimeSpan? offset = null)
        => TryParseTimestamp(value, offset ?? TimeSpan.FromHours(8), out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private static bool TryParseTimestamp(string? value, TimeSpan offset, out DateTimeOffset parsed)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)
            && (value?.Contains('+') == true || value?.EndsWith('Z') == true))
        {
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime local))
        {
            parsed = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
            return true;
        }

        parsed = default;
        return false;
    }

    private sealed record HistorySelection(
        MarketHistoryPoint[] Points,
        string Source,
        string State,
        string Note,
        string FullSignature,
        string MetadataSignature)
    {
        public static HistorySelection Missing { get; } = new(
            Array.Empty<MarketHistoryPoint>(),
            string.Empty,
            "无历史",
            string.Empty,
            string.Empty,
            string.Empty);
    }
}
