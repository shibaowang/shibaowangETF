using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartDataService
{
    // LOCKED: Chart rendering uses real cache/quote data only; display-only points must never replace persisted history.
    private const int MaxKLineDisplayPoints = 180;
    private const int MaxIntradayDisplayPoints = 260;
    private const string QuoteIntradayBarSource = "QUOTE_INTRADAY_BAR";
    private const string QuoteCloseDisplayPointSource = "QUOTE_CLOSE_DISPLAY";

    private sealed record DailyKLineCandidate(
        IReadOnlyList<KLinePoint> Points,
        DateTime? LastDate,
        DateTime? ReceivedAt,
        ChartDataStatus Status);

    public static ChartSecurityInfo CreateSecurityInfo(string strategyCode, string? name, string? actualCode = null)
    {
        string code = MarketSymbolNormalizer.DigitsOnly(string.IsNullOrWhiteSpace(actualCode) ? strategyCode : actualCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            code = MarketSymbolNormalizer.DigitsOnly(strategyCode);
        }

        return new ChartSecurityInfo(
            strategyCode.Trim(),
            code,
            string.IsNullOrWhiteSpace(name) ? strategyCode.Trim() : name.Trim(),
            MarketSymbolNormalizer.NormalizeEastMoneyEtfSecId(code));
    }

    public static ChartSecurityInfo CreateIndexSecurityInfo(string symbol, string name)
    {
        string normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim();
        string actualCode = MarketSymbolNormalizer.LooksLikeSecId(normalizedSymbol)
            ? normalizedSymbol
            : MarketSymbolNormalizer.DigitsOnly(normalizedSymbol);
        return new ChartSecurityInfo(
            normalizedSymbol,
            actualCode,
            string.IsNullOrWhiteSpace(name) ? normalizedSymbol : name.Trim(),
            MarketSymbolNormalizer.NormalizeEastMoneySecId(normalizedSymbol, preferIndex: true),
            ChartInstrumentType.Index);
    }

    public static SecurityChartSnapshot BuildSnapshot(
        ChartSecurityInfo security,
        SecurityChartPeriod period,
        SecurityChartSubPanel subPanel,
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketQuoteRecord> historyRecords,
        ChartIntradayCacheEntry? intradayCache,
        ChartKLineCacheEntry? chartDailyKLineCache)
    {
        MarketQuoteRecord? quote = FindQuote(security.ActualCode, security.InstrumentType, quotes);
        DateTimeOffset updatedAt = DateTimeOffset.Now;
        if (period == SecurityChartPeriod.Intraday)
        {
            IReadOnlyList<IntradayPoint> points = BuildIntradayPoints(security, intradayCache, quote, out bool hasQuoteTail);
            double? previousClose = ResolveIntradayPreviousClose(quote, historyRecords, chartDailyKLineCache, security);
            bool hasRealIntradayCache = intradayCache?.Points.Count > 0;
            ChartDataStatus status = points.Count == 0
                ? new ChartDataStatus(
                    false,
                    intradayCache is not null ? intradayCache.Status.Message : "分时数据暂不可用",
                    intradayCache is not null,
                    intradayCache?.Status.IsRateLimited ?? false,
                    intradayCache?.Status.IsCircuitOpen ?? false)
                : new ChartDataStatus(
                    true,
                    ResolveIntradayStatusMessage(security, hasQuoteTail, hasRealIntradayCache, intradayCache?.Status),
                    intradayCache?.Status.IsUsingCache ?? false,
                    intradayCache?.Status.IsRateLimited ?? false,
                    intradayCache?.Status.IsCircuitOpen ?? false);
            ChartDataStatus volumeStatus = points.Any(point => point.Volume is > 0)
                ? new ChartDataStatus(true, "真实分时成交量")
                : new ChartDataStatus(false, "成交量数据不可用");
            double? intradayChangePercent = ChartChangePercentCalculator.ResolveChangeForPeriod(
                period,
                quote,
                points,
                Array.Empty<KLinePoint>());
            IReadOnlyList<MacdPoint> intradayMacd = MacdCalculator.CalculateFromPrices(
                points.Select(point => (Time: point.Time, Close: point.Price)));
            ChartDataStatus intradayMacdStatus = intradayMacd.Count == 0
                ? new ChartDataStatus(false, "MACD数据不足")
                : new ChartDataStatus(true, "真实分时MACD");
            return new SecurityChartSnapshot(
                security,
                period,
                subPanel,
                quote,
                points,
                Array.Empty<KLinePoint>(),
                TrimIntradayMacdForDisplay(security, intradayMacd),
                status,
                volumeStatus,
                intradayMacdStatus,
                intradayChangePercent,
                previousClose,
                updatedAt,
                hasQuoteTail);
        }

        IReadOnlyList<KLinePoint> daily = ReadDailyKLines(security, historyRecords, chartDailyKLineCache, out ChartDataStatus historyStatus);
        bool quoteAdjusted = false;
        string? quoteStatusSuffix = null;
        IReadOnlyList<KLinePoint> dailyForPeriod = ApplyQuoteToDailyDisplayKLines(
            security,
            daily,
            quote,
            out quoteAdjusted,
            out quoteStatusSuffix);

        IReadOnlyList<KLinePoint> periodKLines = period switch
        {
            SecurityChartPeriod.Weekly => KLineAggregator.AggregateWeekly(dailyForPeriod),
            SecurityChartPeriod.Monthly => KLineAggregator.AggregateMonthly(dailyForPeriod),
            _ => dailyForPeriod
        };

        IReadOnlyList<MacdPoint> macd = MacdCalculator.Calculate(periodKLines);
        string mainStatusMessage = ResolveKLineStatusMessage(
            historyStatus.Message,
            security,
            quoteAdjusted,
            quoteStatusSuffix);
        ChartDataStatus mainStatus = periodKLines.Count == 0
            ? historyStatus
            : new ChartDataStatus(
                true,
                mainStatusMessage,
                true,
                historyStatus.IsRateLimited,
                historyStatus.IsCircuitOpen);
        ChartDataStatus volumeStatusForK = periodKLines.Any(point => point.Volume is > 0)
            ? new ChartDataStatus(true, "真实K线成交量", true)
            : new ChartDataStatus(false, "成交量数据不可用");
        ChartDataStatus macdStatus = macd.Count == 0
            ? new ChartDataStatus(false, "MACD数据不足")
            : new ChartDataStatus(true, "MACD基于真实收盘价");

        double? periodChangePercent = ChartChangePercentCalculator.ResolveChangeForPeriod(
            period,
            quote,
            Array.Empty<IntradayPoint>(),
            periodKLines);
        return new SecurityChartSnapshot(
            security,
            period,
            subPanel,
            quote,
            Array.Empty<IntradayPoint>(),
            periodKLines.TakeLast(MaxKLineDisplayPoints).ToArray(),
            macd.TakeLast(MaxKLineDisplayPoints).ToArray(),
            mainStatus,
            volumeStatusForK,
            macdStatus,
            periodChangePercent,
            null,
            updatedAt,
            quoteAdjusted);
    }

    private static MarketQuoteRecord? FindQuote(
        string actualCode,
        ChartInstrumentType instrumentType,
        IReadOnlyList<MarketQuoteRecord> quotes)
    {
        string marketType = MarketTypeFor(instrumentType);
        return quotes
            .Where(quote => string.Equals(quote.Symbol, actualCode, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string MarketTypeFor(ChartInstrumentType instrumentType)
        => instrumentType == ChartInstrumentType.Index ? "INDEX" : "ETF";

    private static bool MatchesHistorySymbol(MarketQuoteRecord item, ChartSecurityInfo security)
        => string.Equals(item.Symbol, security.ActualCode, StringComparison.OrdinalIgnoreCase)
           || string.Equals(item.Symbol, security.StrategyCode, StringComparison.OrdinalIgnoreCase)
           || string.Equals(item.RawCode, security.EastMoneySecId, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<MarketQuoteRecord> FilterHistory(
        ChartSecurityInfo security,
        IReadOnlyList<MarketQuoteRecord> historyRecords)
    {
        string marketType = MarketTypeFor(security.InstrumentType);
        return historyRecords
            .Where(item => string.Equals(item.MarketType, marketType, StringComparison.OrdinalIgnoreCase)
                           && MatchesHistorySymbol(item, security))
            .OrderByDescending(item => item.ReceivedAt, StringComparer.Ordinal);
    }

    private static string ResolveIntradayStatusMessage(
        ChartSecurityInfo security,
        bool hasQuoteTail,
        bool hasRealIntradayCache,
        ChartDataStatus? cacheStatus)
    {
        bool isIndex = security.InstrumentType == ChartInstrumentType.Index;
        if (!hasQuoteTail)
        {
            return cacheStatus?.IsUsingCache == true
                ? cacheStatus.Message
                : isIndex ? "指数价格分时可用" : "真实分时数据";
        }

        if (!hasRealIntradayCache)
        {
            if (isIndex)
            {
                if (cacheStatus?.IsCircuitOpen == true)
                {
                    return "指数分时接口熔断中，无真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
                }

                if (cacheStatus?.IsRateLimited == true)
                {
                    return "指数分时接口限频中，无真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
                }

                if (cacheStatus is { IsReady: false })
                {
                    return "指数分时接口暂不可用，且无真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
                }
            }

            return "实时 quote 尾点（分时数据暂不可用）";
        }

        if (cacheStatus?.IsCircuitOpen == true)
        {
            if (isIndex)
            {
                return "指数分时接口熔断中，显示最近真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
            }

            return "分时接口熔断中，使用最近真实分时缓存 + 实时 quote 尾点";
        }

        if (cacheStatus?.IsRateLimited == true)
        {
            if (isIndex)
            {
                return "指数分时接口限频中，显示最近真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
            }

            return "分时接口限频中，使用最近真实分时缓存 + 实时 quote 尾点";
        }

        if (isIndex)
        {
            return "指数价格分时可用；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点";
        }

        return "真实分时缓存 + 实时 quote 尾点";
    }

    private static IReadOnlyList<IntradayPoint> BuildIntradayPoints(
        ChartSecurityInfo security,
        ChartIntradayCacheEntry? intradayCache,
        MarketQuoteRecord? quote,
        out bool hasQuoteTail)
    {
        hasQuoteTail = false;
        bool useStandardTradingAxis = security.InstrumentType != ChartInstrumentType.Index;
        bool useUsEasternAxis = security.InstrumentType == ChartInstrumentType.Index;
        IntradayPoint[] points = intradayCache?.Points
            .Where(point => point.Price > 0
                            && (!useStandardTradingAxis || IntradayTradingTimeAxis.IsTradingTime(point.Time))
                            && (!useUsEasternAxis || IntradayTradingTimeAxis.TryGetUsEasternXRatio(point.Time, out _)))
            .OrderBy(point => point.Time)
            .Select(CloneIntraday)
            .ToArray() ?? Array.Empty<IntradayPoint>();
        if (quote?.Price is not double price || price <= 0)
        {
            return points;
        }

        DateTime? quoteTime = ParseMarketTime(quote.QuoteTime) ?? ParseMarketTime(quote.ReceivedAt);
        if (!quoteTime.HasValue
            || (useStandardTradingAxis && !IntradayTradingTimeAxis.IsTradingTime(quoteTime.Value)))
        {
            return points;
        }

        if (useUsEasternAxis
            && TryBuildIndexQuoteCloseDisplayPoints(security, points, quoteTime.Value, price, out IReadOnlyList<IntradayPoint> quoteClosePoints))
        {
            return quoteClosePoints;
        }

        if (useUsEasternAxis && !IntradayTradingTimeAxis.TryGetUsEasternXRatio(quoteTime.Value, out _))
        {
            return points;
        }

        var tail = new IntradayPoint
        {
            Time = quoteTime.Value,
            Price = price,
            Volume = null,
            Amount = null,
            IsQuoteTail = true,
            PointSource = "QUOTE_TAIL"
        };

        if (points.Length == 0)
        {
            hasQuoteTail = true;
            return new[] { tail };
        }

        var result = points.ToList();
        IntradayPoint last = result[^1];
        if (tail.Time <= last.Time)
        {
            last.Price = tail.Price;
            last.IsQuoteTail = true;
        }
        else
        {
            result.Add(tail);
        }

        hasQuoteTail = true;
        return TrimIntradayForDisplay(security, result);
    }

    private static bool TryBuildIndexQuoteCloseDisplayPoints(
        ChartSecurityInfo security,
        IntradayPoint[] points,
        DateTime quoteTime,
        double quotePrice,
        out IReadOnlyList<IntradayPoint> result)
    {
        // LOCKED: QUOTE_CLOSE_DISPLAY is display-only close alignment; it must not fill missing minutes or write cache.
        result = Array.Empty<IntradayPoint>();
        if (points.Length == 0
            || !IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(quoteTime, out DateTime quoteEasternTime)
            || quoteEasternTime.TimeOfDay >= IntradayTradingTimeAxis.UsEasternOpen
               && quoteEasternTime.TimeOfDay <= IntradayTradingTimeAxis.UsEasternClose)
        {
            return false;
        }

        DateTimeOffset quoteNow = CreateChinaDateTimeOffset(quoteTime);
        IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(points, quoteNow);
        if (!completeness.IsCompleteSession
            || DateOnly.FromDateTime(quoteEasternTime.Date) != completeness.LatestCompletedTradeDate)
        {
            return false;
        }

        IntradayPoint last = points[^1];
        if (Math.Abs(last.Price - quotePrice) < 0.0000001)
        {
            return false;
        }

        DateTime closeEasternTime = completeness.LatestCompletedTradeDate
            .ToDateTime(TimeOnly.FromTimeSpan(IntradayTradingTimeAxis.UsEasternClose));
        if (!IntradayTradingTimeAxis.TryConvertUsEasternToChina(closeEasternTime, out DateTime closeChinaTime)
            || !IntradayTradingTimeAxis.TryGetUsEasternXRatio(closeChinaTime, out _))
        {
            return false;
        }

        List<IntradayPoint> displayPoints = points.ToList();
        displayPoints[^1] = new IntradayPoint
        {
            Time = closeChinaTime,
            Price = quotePrice,
            AveragePrice = null,
            Volume = null,
            Amount = null,
            IsQuoteTail = false,
            IsQuoteCloseDisplayPoint = true,
            PointSource = QuoteCloseDisplayPointSource
        };
        result = TrimIntradayForDisplay(security, displayPoints);
        return true;
    }

    private static IntradayPoint[] TrimIntradayForDisplay(
        ChartSecurityInfo security,
        IEnumerable<IntradayPoint> points)
    {
        IntradayPoint[] ordered = points
            .OrderBy(point => point.Time)
            .ToArray();
        if (security.InstrumentType == ChartInstrumentType.Index)
        {
            return ordered;
        }

        return ordered.TakeLast(MaxIntradayDisplayPoints).ToArray();
    }

    private static IReadOnlyList<MacdPoint> TrimIntradayMacdForDisplay(
        ChartSecurityInfo security,
        IReadOnlyList<MacdPoint> points)
    {
        // LOCKED: Index intraday MACD keeps the full US session and must not be truncated by the ETF tail limit.
        MacdPoint[] ordered = points
            .OrderBy(point => point.Date)
            .ToArray();
        if (security.InstrumentType == ChartInstrumentType.Index)
        {
            return ordered;
        }

        return ordered.TakeLast(MaxIntradayDisplayPoints).ToArray();
    }

    private static IReadOnlyList<KLinePoint> ReadDailyKLines(
        ChartSecurityInfo security,
        IReadOnlyList<MarketQuoteRecord> historyRecords,
        ChartKLineCacheEntry? chartDailyKLineCache,
        out ChartDataStatus status)
    {
        ChartDataStatus? cacheStatus = null;
        DailyKLineCandidate? persistedCandidate = SelectBestDailyKLineCandidate(security, historyRecords);
        if (chartDailyKLineCache is { Points.Count: > 0 })
        {
            if (persistedCandidate is null
                || IsMemoryCacheAtLeastAsFresh(chartDailyKLineCache, persistedCandidate))
            {
                status = chartDailyKLineCache.Status;
                return chartDailyKLineCache.Points;
            }

            status = persistedCandidate.Status;
            return persistedCandidate.Points;
        }
        else if (chartDailyKLineCache is not null)
        {
            cacheStatus = chartDailyKLineCache.Status;
        }

        if (persistedCandidate is not null)
        {
            status = persistedCandidate.Status;
            return persistedCandidate.Points;
        }

        status = cacheStatus ?? new ChartDataStatus(false, "无可用DailyLike日K缓存");
        return Array.Empty<KLinePoint>();
    }

    private static DailyKLineCandidate? SelectBestDailyKLineCandidate(
        ChartSecurityInfo security,
        IReadOnlyList<MarketQuoteRecord> historyRecords)
    {
        MarketQuoteRecord[] candidateHistories = FilterHistory(security, historyRecords).ToArray();
        DailyKLineCandidate? best = null;

        foreach (MarketQuoteRecord history in candidateHistories)
        {
            if (string.IsNullOrWhiteSpace(history.RawPayload)
                || !MarketHistoryQuality.IsDailyLike(history.RawPayload))
            {
                continue;
            }

            try
            {
                IReadOnlyList<KLinePoint> points = KLineAggregator.FromHistoryPoints(EastMoneyHistoryParser.ParsePoints(history.RawPayload));
                if (points.Count == 0)
                {
                    continue;
                }

                var candidate = new DailyKLineCandidate(
                    points,
                    points[^1].Date.Date,
                    ParseMarketTime(history.ReceivedAt),
                    new ChartDataStatus(true, "使用最近真实日K缓存", true));
                if (best is null || CompareDailyCandidates(candidate, best) > 0)
                {
                    best = candidate;
                }
            }
            catch
            {
                // Continue scanning older history cache records; a newer malformed DailyLike payload should not hide an older real daily cache.
            }
        }

        return best;
    }

    private static bool IsMemoryCacheAtLeastAsFresh(ChartKLineCacheEntry memory, DailyKLineCandidate persisted)
    {
        DateTime? memoryLastDate = memory.Points.Count > 0 ? memory.Points[^1].Date.Date : null;
        int dateCompare = CompareNullableDates(memoryLastDate, persisted.LastDate);
        if (dateCompare != 0)
        {
            return dateCompare > 0;
        }

        DateTime memoryUpdatedAt = memory.UpdatedAt.LocalDateTime;
        return CompareNullableDates(memoryUpdatedAt, persisted.ReceivedAt) >= 0;
    }

    private static int CompareDailyCandidates(DailyKLineCandidate left, DailyKLineCandidate right)
    {
        int dateCompare = CompareNullableDates(left.LastDate, right.LastDate);
        if (dateCompare != 0)
        {
            return dateCompare;
        }

        return CompareNullableDates(left.ReceivedAt, right.ReceivedAt);
    }

    private static int CompareNullableDates(DateTime? left, DateTime? right)
    {
        if (left.HasValue && right.HasValue)
        {
            return left.Value.CompareTo(right.Value);
        }

        if (left.HasValue)
        {
            return 1;
        }

        return right.HasValue ? -1 : 0;
    }

    private static string ResolveKLineStatusMessage(
        string baseMessage,
        ChartSecurityInfo security,
        bool quoteAdjusted,
        string? quoteStatusSuffix)
    {
        if (quoteAdjusted)
        {
            return baseMessage + "；含盘中临时K线（仅显示，不写缓存）";
        }

        return string.IsNullOrWhiteSpace(quoteStatusSuffix)
            ? baseMessage
            : baseMessage + "；" + quoteStatusSuffix;
    }

    private static double? ResolveIntradayPreviousClose(
        MarketQuoteRecord? quote,
        IReadOnlyList<MarketQuoteRecord> historyRecords,
        ChartKLineCacheEntry? chartDailyKLineCache,
        ChartSecurityInfo security)
    {
        if (IsValidPositive(quote?.LastClose))
        {
            return quote!.LastClose;
        }

        IReadOnlyList<KLinePoint> daily = ReadDailyKLines(security, historyRecords, chartDailyKLineCache, out _);
        return ResolvePreviousCloseFromDaily(daily, ResolveQuoteTradingDate(security, quote));
    }

    private static double? ResolvePreviousCloseFromDaily(IReadOnlyList<KLinePoint> daily, DateTime? quoteDate)
    {
        KLinePoint[] ordered = daily
            .Where(point => IsValidPositive(point.Close))
            .OrderBy(point => point.Date)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        if (quoteDate.HasValue)
        {
            KLinePoint? previousTradingDay = ordered
                .Where(point => point.Date.Date < quoteDate.Value.Date)
                .LastOrDefault();
            if (previousTradingDay is not null)
            {
                return previousTradingDay.Close;
            }

            if (ordered[^1].Date.Date == quoteDate.Value.Date)
            {
                return null;
            }
        }

        return ordered[^1].Close;
    }

    private static IReadOnlyList<KLinePoint> ApplyQuoteToDailyDisplayKLines(
        ChartSecurityInfo security,
        IReadOnlyList<KLinePoint> daily,
        MarketQuoteRecord? quote,
        out bool adjusted,
        out string? statusSuffix)
    {
        // LOCKED: Accepted live K-line behavior. QUOTE_INTRADAY_BAR is display-only and must never persist to market_history_cache.
        adjusted = false;
        statusSuffix = null;
        if (daily.Count == 0 || quote is null)
        {
            return daily;
        }

        DateTime? quoteDate = ResolveQuoteTradingDate(security, quote);
        if (!quoteDate.HasValue)
        {
            return daily;
        }

        DateTime latestDailyDate = daily[^1].Date.Date;
        if (quoteDate.Value.Date < latestDailyDate)
        {
            return daily;
        }

        if (quoteDate.Value.Date == latestDailyDate)
        {
            KLinePoint? quoteBar = BuildQuoteDisplayBar(quote, latestDailyDate);
            if (quoteBar is null)
            {
                statusSuffix = BuildQuoteOhlcMissingStatus(security);
                return daily;
            }

            KLinePoint[] result = daily.Select(CloneKLine).ToArray();
            KLinePoint last = result[^1];
            last.Open = quoteBar.Open;
            last.High = quoteBar.High;
            last.Low = quoteBar.Low;
            last.Close = quoteBar.Close;
            last.Volume = quoteBar.Volume;
            last.Amount = quoteBar.Amount;
            last.IsQuoteAdjusted = true;
            last.IsDisplayOnly = true;
            last.PointSource = quoteBar.PointSource;
            adjusted = true;
            return result;
        }

        KLinePoint? displayBar = BuildQuoteDisplayBar(quote, quoteDate.Value.Date);
        if (displayBar is null)
        {
            statusSuffix = BuildQuoteOhlcMissingStatus(security);
            return daily;
        }

        adjusted = true;
        return daily
            .Select(CloneKLine)
            .Concat(new[] { displayBar })
            .ToArray();
    }

    private static string BuildQuoteOhlcMissingStatus(ChartSecurityInfo security)
        => security.InstrumentType == ChartInstrumentType.Index
            ? "当前美股交易日盘中OHLC字段不足，未生成临时K线"
            : "当前交易日盘中OHLC字段不足，未生成临时K线";

    private static KLinePoint? BuildQuoteDisplayBar(MarketQuoteRecord quote, DateTime date)
    {
        if (!IsValidPositive(quote.Price)
            || !IsValidPositive(quote.OpenValue)
            || !IsValidPositive(quote.HighValue)
            || !IsValidPositive(quote.LowValue))
        {
            return null;
        }

        double price = quote.Price!.Value;
        double open = quote.OpenValue!.Value;
        double high = quote.HighValue!.Value;
        double low = quote.LowValue!.Value;
        if (high < open || high < price || low > open || low > price)
        {
            return null;
        }

        return new KLinePoint
        {
            Date = date.Date,
            Open = open,
            High = high,
            Low = low,
            Close = price,
            Volume = quote.Volume,
            Amount = quote.Amount,
            IsQuoteAdjusted = true,
            IsDisplayOnly = true,
            PointSource = QuoteIntradayBarSource
        };
    }

    private static DateTime? ResolveQuoteTradingDate(ChartSecurityInfo security, MarketQuoteRecord? quote)
    {
        DateTime? marketTime = ParseMarketTime(quote?.QuoteTime) ?? ParseMarketTime(quote?.ReceivedAt);
        if (!marketTime.HasValue)
        {
            return null;
        }

        if (security.InstrumentType == ChartInstrumentType.Index
            && IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(marketTime.Value, out DateTime easternTime))
        {
            return easternTime.Date;
        }

        return marketTime.Value.Date;
    }

    private static IntradayPoint CloneIntraday(IntradayPoint point)
        => new()
        {
            Time = point.Time,
            Price = point.Price,
            AveragePrice = point.AveragePrice,
            Volume = point.Volume,
            Amount = point.Amount,
            IsQuoteTail = point.IsQuoteTail,
            IsQuoteCloseDisplayPoint = point.IsQuoteCloseDisplayPoint,
            PointSource = point.PointSource
        };

    private static DateTimeOffset CreateChinaDateTimeOffset(DateTime chinaTime)
    {
        try
        {
            TimeZoneInfo chinaZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            DateTime unspecified = DateTime.SpecifyKind(chinaTime, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, chinaZone.GetUtcOffset(unspecified));
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            TimeZoneInfo chinaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            DateTime unspecified = DateTime.SpecifyKind(chinaTime, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, chinaZone.GetUtcOffset(unspecified));
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return new DateTimeOffset(chinaTime, TimeSpan.FromHours(8));
    }

    private static KLinePoint CloneKLine(KLinePoint point)
        => new()
        {
            Date = point.Date,
            Open = point.Open,
            High = point.High,
            Low = point.Low,
            Close = point.Close,
            Volume = point.Volume,
            Amount = point.Amount,
            IsQuoteAdjusted = point.IsQuoteAdjusted,
            IsDisplayOnly = point.IsDisplayOnly,
            PointSource = point.PointSource
        };

    private static DateTime? ParseMarketTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
               || DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed
            : null;
    }

    private static bool IsValidPositive(double? value)
        => value is double number && number > 0 && !double.IsNaN(number) && !double.IsInfinity(number);
}
