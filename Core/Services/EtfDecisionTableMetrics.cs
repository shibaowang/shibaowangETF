using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class EtfDecisionTableMetrics
{
    private const string OtcReplacementSource = "\u573a\u5916\u66ff\u4ee3";
    private static readonly TimeSpan SinaFundEveningWindowStart = TimeSpan.FromHours(18);

    public static double? CalculatePremiumRate(MarketQuoteRecord? quote)
    {
        if (quote?.Price is not double price || quote.Iopv is not double iopv || iopv <= 0)
        {
            return null;
        }

        return (price - iopv) / iopv;
    }

    public static EtfPositionCostMetrics CalculatePositionCostMetrics(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions)
    {
        var replayList = replayPositions.ToList();
        var otcList = otcPositions.ToList();
        bool hasOtcDetails = otcList.Count > 0;
        IEnumerable<PositionReplayStateRecord> marketReplayPositions = hasOtcDetails
            ? replayList.Where(position => !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            : replayList;
        double marketQuantity = marketReplayPositions.Sum(position => position.Quantity);
        double marketCost = marketReplayPositions
            .Where(position => !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            .Sum(position => position.CostAmount);
        if (!hasOtcDetails)
        {
            marketCost = marketReplayPositions.Sum(position => position.CostAmount);
        }

        double otcQuantity = hasOtcDetails ? otcList.Sum(position => position.Quantity) : 0;
        double otcCost = otcPositions.Sum(position => position.CostAmount);
        double totalQuantity = marketQuantity + otcQuantity;
        double totalCostAmount = marketCost + otcCost;
        double averageCost = totalQuantity > 0 ? totalCostAmount / totalQuantity : 0;
        return new EtfPositionCostMetrics(totalQuantity, totalCostAmount, averageCost);
    }

    public static double CalculateCompositeCost(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions)
        => CalculatePositionCostMetrics(replayPositions, otcPositions).TotalCostAmount;

    public static double? CalculatePrincipalRatio(double totalCostAmount, double principal)
        => principal > 0 && totalCostAmount > 0 ? totalCostAmount / principal : null;

    public static double? CalculateHoldingPnl(double? marketValue, double totalCostAmount)
        => marketValue.HasValue && totalCostAmount > 0 ? marketValue.Value - totalCostAmount : null;

    public static double? CalculateHoldingReturnRate(double? holdingPnl, double totalCostAmount)
        => holdingPnl.HasValue && totalCostAmount > 0 ? holdingPnl.Value / totalCostAmount : null;

    public static double? CalculateNaturalDayValuationDailyPnl(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime now)
        => CalculateNaturalDayValuationDailyPnl(
            replayPositions,
            Array.Empty<OtcPositionReplayStateRecord>(),
            quotes,
            now);

    public static double? CalculateNaturalDayValuationDailyPnl(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions,
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime now)
    {
        IReadOnlyList<MarketQuoteRecord> quoteList = quotes as IReadOnlyList<MarketQuoteRecord> ?? quotes.ToArray();
        (DateTime startInclusive, DateTime endExclusive) = GetNaturalDayRange(now);
        DateTime? latestEveningSinaFundQuoteDate = ResolveLatestEveningSinaFundQuoteDate(quoteList, startInclusive, endExclusive);
        bool hasEtfQuotes = quoteList.Any(IsEtfQuote);
        bool hasCurrentDayEtfQuote = HasCurrentDayEtfQuote(quoteList, startInclusive, endExclusive);
        bool includedAny = false;
        double total = 0;
        var includedOtcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PositionReplayStateRecord position in replayPositions)
        {
            MarketQuoteRecord? quote = FindValuationQuote(position, quoteList);
            if (!IsValuationUpdatedInRange(
                    quote,
                    startInclusive,
                    endExclusive,
                    latestEveningSinaFundQuoteDate,
                    hasEtfQuotes,
                    hasCurrentDayEtfQuote))
            {
                continue;
            }

            double? dailyPnl = ResolveReplayDailyPnl(position, quote);
            if (!HasFiniteValue(dailyPnl))
            {
                continue;
            }

            includedAny = true;
            total += dailyPnl!.Value;
            if (IsOtcReplacementPosition(position))
            {
                includedOtcKeys.Add(BuildPositionKey(position.StrategyCode, position.ActualCode));
            }
        }

        foreach (OtcPositionReplayStateRecord position in otcPositions)
        {
            if (!includedOtcKeys.Add(BuildPositionKey(position.StrategyCode, position.ActualCode)))
            {
                continue;
            }

            MarketQuoteRecord? quote = FindOtcValuationQuote(position.ActualCode, quoteList);
            if (!IsValuationUpdatedInRange(
                    quote,
                    startInclusive,
                    endExclusive,
                    latestEveningSinaFundQuoteDate,
                    hasEtfQuotes,
                    hasCurrentDayEtfQuote))
            {
                continue;
            }

            double? dailyPnl = ResolveDailyPnl(position.DailyPnl, position.Quantity, quote);
            if (!HasFiniteValue(dailyPnl))
            {
                continue;
            }

            includedAny = true;
            total += dailyPnl!.Value;
        }

        return includedAny ? total : null;
    }

    public static IReadOnlyList<NaturalDayPnlEvaluationItem> EvaluateNaturalDayValuationItems(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime now)
        => EvaluateNaturalDayValuationItems(
            replayPositions,
            Array.Empty<OtcPositionReplayStateRecord>(),
            quotes,
            now);

    public static IReadOnlyList<NaturalDayPnlEvaluationItem> EvaluateNaturalDayValuationItems(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions,
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime now)
    {
        IReadOnlyList<MarketQuoteRecord> quoteList = quotes as IReadOnlyList<MarketQuoteRecord> ?? quotes.ToArray();
        (DateTime startInclusive, DateTime endExclusive) = GetNaturalDayRange(now);
        DateTime? latestEveningSinaFundQuoteDate = ResolveLatestEveningSinaFundQuoteDate(quoteList, startInclusive, endExclusive);
        bool hasEtfQuotes = quoteList.Any(IsEtfQuote);
        bool hasCurrentDayEtfQuote = HasCurrentDayEtfQuote(quoteList, startInclusive, endExclusive);
        var items = new List<NaturalDayPnlEvaluationItem>();
        var itemIndexesByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var includedOtcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PositionReplayStateRecord position in replayPositions)
        {
            MarketQuoteRecord? quote = FindValuationQuote(position, quoteList);
            ValuationUpdateDecision decision = EvaluateValuationUpdate(
                    quote,
                    startInclusive,
                    endExclusive,
                    latestEveningSinaFundQuoteDate,
                    hasEtfQuotes,
                    hasCurrentDayEtfQuote);
            double? dailyPnl = decision.Included
                ? ResolveReplayDailyPnl(position, quote)
                : null;
            if (decision.Included && !HasFiniteValue(dailyPnl))
            {
                decision = new ValuationUpdateDecision(false, "NO_VALID_DAILY_PNL", "无有效 daily_pnl");
            }

            bool included = decision.Included && HasFiniteValue(dailyPnl);
            if (included && IsOtcReplacementPosition(position))
            {
                includedOtcKeys.Add(BuildPositionKey(position.StrategyCode, position.ActualCode));
            }

            AddOrMergeEvaluationItem(items, itemIndexesByKey, CreateEvaluationItem(
                position.StrategyCode,
                position.ActualCode,
                ResolveInstrumentName(position.ActualCode, quote),
                position.Source,
                quote?.MarketType ?? string.Empty,
                position.Quantity,
                dailyPnl,
                quote,
                position.CalculatedAt,
                included,
                included ? dailyPnl : null,
                decision));
        }

        foreach (OtcPositionReplayStateRecord position in otcPositions)
        {
            string positionKey = BuildPositionKey(position.StrategyCode, position.ActualCode);
            if (includedOtcKeys.Contains(positionKey))
            {
                continue;
            }

            MarketQuoteRecord? quote = FindOtcValuationQuote(position.ActualCode, quoteList);
            ValuationUpdateDecision decision = EvaluateValuationUpdate(
                    quote,
                    startInclusive,
                    endExclusive,
                    latestEveningSinaFundQuoteDate,
                    hasEtfQuotes,
                    hasCurrentDayEtfQuote);
            double? dailyPnl = decision.Included
                ? ResolveDailyPnl(position.DailyPnl, position.Quantity, quote)
                : null;
            if (decision.Included && !HasFiniteValue(dailyPnl))
            {
                decision = new ValuationUpdateDecision(false, "NO_VALID_DAILY_PNL", "无有效 daily_pnl");
            }

            bool included = decision.Included && HasFiniteValue(dailyPnl);
            AddOrMergeEvaluationItem(items, itemIndexesByKey, CreateEvaluationItem(
                position.StrategyCode,
                position.ActualCode,
                ResolveInstrumentName(position.ActualCode, quote),
                OtcReplacementSource,
                quote?.MarketType ?? "OTC",
                position.Quantity,
                dailyPnl,
                quote,
                position.CalculatedAt,
                included,
                included ? dailyPnl : null,
                decision));
        }

        return items;
    }

    public static bool IsValuationUpdatedInRange(MarketQuoteRecord? quote, DateTime startInclusive, DateTime endExclusive)
        => IsValuationUpdatedInRange(
            quote,
            startInclusive,
            endExclusive,
            latestEveningSinaFundQuoteDate: null,
            hasEtfQuotes: false,
            hasCurrentDayEtfQuote: false);

    public static (DateTime StartInclusive, DateTime EndExclusive) GetNaturalDayRange(DateTime now)
    {
        DateTime startInclusive = now.Date;
        return (startInclusive, startInclusive.AddDays(1));
    }

    public static bool IsPnLEventInNaturalDay(DateTime eventTime, DateTime now)
    {
        (DateTime startInclusive, DateTime endExclusive) = GetNaturalDayRange(now);
        return eventTime >= startInclusive && eventTime < endExclusive;
    }

    private static bool IsValuationUpdatedInRange(
        MarketQuoteRecord? quote,
        DateTime startInclusive,
        DateTime endExclusive,
        DateTime? latestEveningSinaFundQuoteDate,
        bool hasEtfQuotes,
        bool hasCurrentDayEtfQuote)
        => EvaluateValuationUpdate(
            quote,
            startInclusive,
            endExclusive,
            latestEveningSinaFundQuoteDate,
            hasEtfQuotes,
            hasCurrentDayEtfQuote).Included;

    private static ValuationUpdateDecision EvaluateValuationUpdate(
        MarketQuoteRecord? quote,
        DateTime startInclusive,
        DateTime endExclusive,
        DateTime? latestEveningSinaFundQuoteDate,
        bool hasEtfQuotes,
        bool hasCurrentDayEtfQuote)
    {
        if (quote is null)
        {
            return new ValuationUpdateDecision(false, "NO_MATCHING_QUOTE", "未找到匹配行情");
        }

        if (IsSinaFundQuote(quote))
        {
            return EvaluateSinaFundValuationUpdate(
                quote,
                startInclusive,
                endExclusive,
                latestEveningSinaFundQuoteDate,
                hasEtfQuotes,
                hasCurrentDayEtfQuote);
        }

        DateTime? valuationTime = ResolveMarketQuoteValuationTime(quote);
        if (!valuationTime.HasValue)
        {
            return new ValuationUpdateDecision(false, "QUOTE_TIME_MISSING", "quote_time 缺失");
        }

        if (valuationTime.Value >= startInclusive && valuationTime.Value < endExclusive)
        {
            string reasonCode = IsEtfQuote(quote) ? "VALID_ETF_TODAY_QUOTE" : "VALID_TODAY_QUOTE";
            string reasonText = IsEtfQuote(quote) ? "有效 ETF 当日行情" : "有效当日行情";
            return new ValuationUpdateDecision(true, reasonCode, reasonText);
        }

        DateTime? receivedAt = ParseTime(quote.ReceivedAt);
        if (IsEtfQuote(quote)
            && receivedAt.HasValue
            && receivedAt.Value >= startInclusive
            && receivedAt.Value < endExclusive
            && valuationTime.Value < startInclusive)
        {
            return new ValuationUpdateDecision(false, "ETF_STALE_QUOTE_REFETCHED", "ETF 旧行情重新接收");
        }

        return new ValuationUpdateDecision(false, "QUOTE_TIME_OUTSIDE_TODAY", "quote_time 不属于今日自然日");
    }

    private static ValuationUpdateDecision EvaluateSinaFundValuationUpdate(
        MarketQuoteRecord quote,
        DateTime startInclusive,
        DateTime endExclusive,
        DateTime? latestEveningSinaFundQuoteDate,
        bool hasEtfQuotes,
        bool hasCurrentDayEtfQuote)
    {
        DateTime? quoteTime = ParseTime(quote.QuoteTime);
        if (!quoteTime.HasValue)
        {
            return new ValuationUpdateDecision(false, "QUOTE_TIME_MISSING", "quote_time 缺失");
        }

        DateTime quoteDate = quoteTime.Value.Date;
        if (quoteDate == startInclusive.Date)
        {
            return new ValuationUpdateDecision(true, "VALID_SINA_FUND_NAV_DATE", "有效工作日晚间 NAV 事件");
        }

        DateTime? receivedAt = ParseTime(quote.ReceivedAt);
        if (!receivedAt.HasValue || receivedAt.Value < startInclusive || receivedAt.Value >= endExclusive)
        {
            return new ValuationUpdateDecision(false, "SINA_FUND_RECEIVED_OUTSIDE_TODAY", "quote_time 不属于今日自然日");
        }

        // SINA_FUND cache rows are upserted. A non-trading re-fetch can refresh
        // received_at for an old NAV date, but that is not a new PnL event.
        if (IsWeekend(receivedAt.Value.Date))
        {
            return new ValuationUpdateDecision(false, "SINA_FUND_WEEKEND_OLD_NAV_REFETCH", "周末重新接收旧 NAV");
        }

        if (hasEtfQuotes && !hasCurrentDayEtfQuote)
        {
            return new ValuationUpdateDecision(false, "SINA_FUND_MARKET_CLOSED_OLD_NAV_REFETCH", "休市日重新接收旧 NAV");
        }

        if (quoteDate != PreviousWeekday(receivedAt.Value.Date))
        {
            return new ValuationUpdateDecision(false, "SINA_FUND_OLD_NAV_REFETCH", "SINA_FUND 旧 NAV 重新接收");
        }

        DateTime eveningWindowStart = startInclusive.Date.Add(SinaFundEveningWindowStart);
        if (receivedAt.Value < eveningWindowStart)
        {
            return new ValuationUpdateDecision(false, "SINA_FUND_BEFORE_EVENING_NAV", "SINA_FUND 非晚间 NAV 事件");
        }

        if (latestEveningSinaFundQuoteDate.HasValue
            && quoteDate == latestEveningSinaFundQuoteDate.Value.Date)
        {
            return new ValuationUpdateDecision(true, "VALID_SINA_FUND_EVENING_NAV", "有效工作日晚间 NAV 事件");
        }

        return new ValuationUpdateDecision(false, "SINA_FUND_OLDER_NAV_BATCH", "SINA_FUND 旧 NAV 重新接收");
    }

    private static DateTime? ResolveLatestEveningSinaFundQuoteDate(
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime startInclusive,
        DateTime endExclusive)
    {
        DateTime eveningWindowStart = startInclusive.Date.Add(SinaFundEveningWindowStart);

        return quotes
            .Where(IsSinaFundQuote)
            .Select(quote => new
            {
                QuoteDate = ParseTime(quote.QuoteTime)?.Date,
                ReceivedAt = ParseTime(quote.ReceivedAt)
            })
            .Where(item => item.QuoteDate.HasValue
                           && item.QuoteDate.Value < startInclusive.Date
                           && item.ReceivedAt.HasValue
                           && item.ReceivedAt.Value >= eveningWindowStart
                           && item.ReceivedAt.Value < endExclusive)
            .Select(item => (DateTime?)item.QuoteDate!.Value)
            .DefaultIfEmpty(null)
            .Max();
    }

    private static NaturalDayPnlEvaluationItem CreateEvaluationItem(
        string strategyCode,
        string actualCode,
        string instrumentName,
        string source,
        string marketType,
        double quantity,
        double? candidateDailyPnl,
        MarketQuoteRecord? quote,
        string? calculatedAt,
        bool included,
        double? includedAmount,
        ValuationUpdateDecision decision)
        => new(
            strategyCode,
            actualCode,
            instrumentName,
            source,
            marketType,
            quantity,
            candidateDailyPnl,
            quote?.QuoteTime,
            quote?.ReceivedAt,
            calculatedAt,
            included,
            includedAmount,
            decision.ReasonCode,
            decision.ReasonText);

    private static void AddOrMergeEvaluationItem(
        List<NaturalDayPnlEvaluationItem> items,
        Dictionary<string, int> itemIndexesByKey,
        NaturalDayPnlEvaluationItem candidate)
    {
        string key = BuildPositionKey(candidate.StrategyCode, candidate.ActualCode);
        if (!itemIndexesByKey.TryGetValue(key, out int existingIndex))
        {
            itemIndexesByKey[key] = items.Count;
            items.Add(candidate);
            return;
        }

        NaturalDayPnlEvaluationItem existing = items[existingIndex];
        if (ShouldReplaceEvaluationItem(existing, candidate))
        {
            items[existingIndex] = candidate;
        }
    }

    private static bool ShouldReplaceEvaluationItem(
        NaturalDayPnlEvaluationItem existing,
        NaturalDayPnlEvaluationItem candidate)
    {
        if (candidate.IncludedToday != existing.IncludedToday)
        {
            return candidate.IncludedToday;
        }

        int existingEvidence = EvaluationEvidenceScore(existing);
        int candidateEvidence = EvaluationEvidenceScore(candidate);
        return candidateEvidence > existingEvidence;
    }

    private static int EvaluationEvidenceScore(NaturalDayPnlEvaluationItem item)
        => (item.CandidateDailyPnl.HasValue ? 4 : 0)
           + (!string.IsNullOrWhiteSpace(item.QuoteTime) ? 2 : 0)
           + (!string.IsNullOrWhiteSpace(item.ReceivedAt) ? 2 : 0)
           + (!string.IsNullOrWhiteSpace(item.CalculatedAt) ? 1 : 0);

    private static string ResolveInstrumentName(string actualCode, MarketQuoteRecord? quote)
        => !string.IsNullOrWhiteSpace(quote?.DisplayName)
            ? quote!.DisplayName!
            : actualCode;

    private static bool HasCurrentDayEtfQuote(
        IEnumerable<MarketQuoteRecord> quotes,
        DateTime startInclusive,
        DateTime endExclusive)
        => quotes
            .Where(IsEtfQuote)
            .Select(quote => ParseTime(quote.QuoteTime))
            .Any(quoteTime => quoteTime.HasValue
                              && quoteTime.Value >= startInclusive
                              && quoteTime.Value < endExclusive);

    private static DateTime? ResolveValuationTime(MarketQuoteRecord? quote)
    {
        if (quote is null)
        {
            return null;
        }

        if (IsSinaFundQuote(quote))
        {
            return ParseTime(quote.QuoteTime);
        }

        return ParseTime(quote.ReceivedAt) ?? ParseTime(quote.QuoteTime);
    }

    private static DateTime? ResolveMarketQuoteValuationTime(MarketQuoteRecord quote)
        => ParseTime(quote.QuoteTime);

    private static MarketQuoteRecord? FindValuationQuote(PositionReplayStateRecord position, IEnumerable<MarketQuoteRecord> quotes)
    {
        string actualCode = DigitsOnly(position.ActualCode);
        string strategyCode = DigitsOnly(position.StrategyCode);

        if (IsOtcReplacementPosition(position))
        {
            return FindOtcValuationQuote(actualCode, quotes);
        }

        return MarketQuoteFreshnessSelector.SelectBest(quotes, actualCode, "ETF")
               ?? MarketQuoteFreshnessSelector.SelectBest(quotes, strategyCode, "ETF");
    }

    private static MarketQuoteRecord? FindOtcValuationQuote(string actualCode, IEnumerable<MarketQuoteRecord> quotes)
        => quotes
            .Where(quote => IsSinaFundQuote(quote) && SameSymbol(quote.Symbol, actualCode))
            .OrderByDescending(quote => ParseTime(quote.QuoteTime) ?? DateTime.MinValue)
            .ThenByDescending(quote => ParseTime(quote.ReceivedAt) ?? DateTime.MinValue)
            .FirstOrDefault();

    private static bool IsSinaFundQuote(MarketQuoteRecord quote)
        => string.Equals(quote.MarketType, "OTC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(quote.Source, "SINA_FUND", StringComparison.OrdinalIgnoreCase);

    private static bool IsEtfQuote(MarketQuoteRecord quote)
        => string.Equals(quote.MarketType, "ETF", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeekend(DateTime date)
        => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTime PreviousWeekday(DateTime date)
    {
        DateTime candidate = date.AddDays(-1);
        while (IsWeekend(candidate))
        {
            candidate = candidate.AddDays(-1);
        }

        return candidate.Date;
    }

    private static bool IsOtcReplacementPosition(PositionReplayStateRecord position)
        => string.Equals(position.Source?.Trim(), OtcReplacementSource, StringComparison.Ordinal);

    private static double? ResolveReplayDailyPnl(
        PositionReplayStateRecord position,
        MarketQuoteRecord? quote)
        => IsOtcReplacementPosition(position)
            ? ResolveDailyPnl(position.DailyPnl, position.Quantity, quote)
            : HasFiniteValue(position.DailyPnl)
                ? position.DailyPnl
                : null;

    private static double? ResolveDailyPnl(double? storedDailyPnl, double quantity, MarketQuoteRecord? quote)
    {
        if (HasFiniteValue(storedDailyPnl))
        {
            return storedDailyPnl;
        }

        if (quote?.Price is not double price
            || quote.LastClose is not double lastClose
            || !HasFiniteValue(price)
            || !HasFiniteValue(lastClose)
            || quantity <= 0)
        {
            return null;
        }

        return (price - lastClose) * quantity;
    }

    private static string BuildPositionKey(string strategyCode, string actualCode)
        => DigitsOnly(strategyCode) + "|" + DigitsOnly(actualCode);

    private static bool SameSymbol(string? quoteSymbol, string targetDigits)
        => !string.IsNullOrWhiteSpace(targetDigits)
           && string.Equals(DigitsOnly(quoteSymbol), targetDigits, StringComparison.OrdinalIgnoreCase);

    private static string DigitsOnly(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());

    private static DateTime? ParseTime(string? value)
        => DateTime.TryParse(value, out DateTime parsed) ? parsed : null;

    private static bool HasFiniteValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

    private readonly record struct ValuationUpdateDecision(bool Included, string ReasonCode, string ReasonText);
}

public sealed record EtfPositionCostMetrics(double TotalQuantity, double TotalCostAmount, double AverageCost);
