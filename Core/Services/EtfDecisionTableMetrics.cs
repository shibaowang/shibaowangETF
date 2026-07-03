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
        (DateTime startInclusive, DateTime endExclusive) = GetNaturalDayRange(now);
        DateTime? latestEveningSinaFundQuoteDate = ResolveLatestEveningSinaFundQuoteDate(quotes, startInclusive, endExclusive);
        bool includedAny = false;
        double total = 0;
        var includedOtcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PositionReplayStateRecord position in replayPositions)
        {
            MarketQuoteRecord? quote = FindValuationQuote(position, quotes);
            if (!IsValuationUpdatedInRange(quote, startInclusive, endExclusive, latestEveningSinaFundQuoteDate))
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

            MarketQuoteRecord? quote = FindOtcValuationQuote(position.ActualCode, quotes);
            if (!IsValuationUpdatedInRange(quote, startInclusive, endExclusive, latestEveningSinaFundQuoteDate))
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

    public static bool IsValuationUpdatedInRange(MarketQuoteRecord? quote, DateTime startInclusive, DateTime endExclusive)
        => IsValuationUpdatedInRange(quote, startInclusive, endExclusive, latestEveningSinaFundQuoteDate: null);

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
        DateTime? latestEveningSinaFundQuoteDate)
    {
        if (quote is null)
        {
            return false;
        }

        if (IsSinaFundQuote(quote))
        {
            return IsSinaFundValuationUpdatedInRange(
                quote,
                startInclusive,
                endExclusive,
                latestEveningSinaFundQuoteDate);
        }

        DateTime? valuationTime = ResolveValuationTime(quote);
        return valuationTime.HasValue
               && valuationTime.Value >= startInclusive
               && valuationTime.Value < endExclusive;
    }

    private static bool IsSinaFundValuationUpdatedInRange(
        MarketQuoteRecord quote,
        DateTime startInclusive,
        DateTime endExclusive,
        DateTime? latestEveningSinaFundQuoteDate)
    {
        DateTime? quoteTime = ParseTime(quote.QuoteTime);
        if (!quoteTime.HasValue)
        {
            return false;
        }

        DateTime quoteDate = quoteTime.Value.Date;
        if (quoteDate == startInclusive.Date)
        {
            return true;
        }

        DateTime? receivedAt = ParseTime(quote.ReceivedAt);
        if (!receivedAt.HasValue || receivedAt.Value < startInclusive || receivedAt.Value >= endExclusive)
        {
            return false;
        }

        DateTime eveningWindowStart = startInclusive.Date.Add(SinaFundEveningWindowStart);
        if (receivedAt.Value < eveningWindowStart)
        {
            return false;
        }

        return latestEveningSinaFundQuoteDate.HasValue
               && quoteDate == latestEveningSinaFundQuoteDate.Value.Date;
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

    private static MarketQuoteRecord? FindValuationQuote(PositionReplayStateRecord position, IEnumerable<MarketQuoteRecord> quotes)
    {
        string actualCode = DigitsOnly(position.ActualCode);
        string strategyCode = DigitsOnly(position.StrategyCode);

        if (IsOtcReplacementPosition(position))
        {
            return FindOtcValuationQuote(actualCode, quotes);
        }

        return quotes
            .Where(quote => string.Equals(quote.MarketType, "ETF", StringComparison.OrdinalIgnoreCase)
                            && (SameSymbol(quote.Symbol, actualCode) || SameSymbol(quote.Symbol, strategyCode)))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
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

    private static bool IsOtcReplacementPosition(PositionReplayStateRecord position)
        => string.Equals(position.Source?.Trim(), OtcReplacementSource, StringComparison.Ordinal);

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
}

public sealed record EtfPositionCostMetrics(double TotalQuantity, double TotalCostAmount, double AverageCost);
