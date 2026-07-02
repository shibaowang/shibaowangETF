using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class EtfDecisionTableMetrics
{
    private const string OtcReplacementSource = "\u573a\u5916\u66ff\u4ee3";

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
        DateTime startInclusive = now.Date;
        DateTime endExclusive = startInclusive.AddDays(1);
        bool includedAny = false;
        double total = 0;
        var includedOtcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PositionReplayStateRecord position in replayPositions)
        {
            if (!HasFiniteValue(position.DailyPnl))
            {
                continue;
            }

            MarketQuoteRecord? quote = FindValuationQuote(position, quotes);
            if (!IsValuationUpdatedInRange(quote, startInclusive, endExclusive))
            {
                continue;
            }

            includedAny = true;
            total += position.DailyPnl!.Value;
            if (IsOtcReplacementPosition(position))
            {
                includedOtcKeys.Add(BuildPositionKey(position.StrategyCode, position.ActualCode));
            }
        }

        foreach (OtcPositionReplayStateRecord position in otcPositions)
        {
            if (!HasFiniteValue(position.DailyPnl)
                || !includedOtcKeys.Add(BuildPositionKey(position.StrategyCode, position.ActualCode)))
            {
                continue;
            }

            MarketQuoteRecord? quote = FindOtcValuationQuote(position.ActualCode, quotes);
            if (!IsValuationUpdatedInRange(quote, startInclusive, endExclusive))
            {
                continue;
            }

            includedAny = true;
            total += position.DailyPnl!.Value;
        }

        return includedAny ? total : null;
    }

    public static bool IsValuationUpdatedInRange(MarketQuoteRecord? quote, DateTime startInclusive, DateTime endExclusive)
    {
        DateTime? valuationTime = ParseTime(quote?.ReceivedAt) ?? ParseTime(quote?.QuoteTime);
        return valuationTime.HasValue
               && valuationTime.Value >= startInclusive
               && valuationTime.Value < endExclusive;
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
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool IsSinaFundQuote(MarketQuoteRecord quote)
        => string.Equals(quote.MarketType, "OTC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(quote.Source, "SINA_FUND", StringComparison.OrdinalIgnoreCase);

    private static bool IsOtcReplacementPosition(PositionReplayStateRecord position)
        => string.Equals(position.Source?.Trim(), OtcReplacementSource, StringComparison.Ordinal);

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
