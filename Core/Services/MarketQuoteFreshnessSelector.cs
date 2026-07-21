using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class MarketQuoteFreshnessSelector
{
    public static MarketQuoteRecord? SelectBest(
        IEnumerable<MarketQuoteRecord> quotes,
        string? code,
        string? marketType)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return quotes
            .Where(quote => marketType is null
                            || string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase))
            .Where(quote => MatchesCode(quote.Symbol, code)
                            || MatchesCode(quote.RawCode, code))
            .OrderByDescending(quote => ParseTime(quote.QuoteTime).HasValue)
            .ThenByDescending(quote => ParseTime(quote.QuoteTime) ?? DateTime.MinValue)
            .ThenByDescending(ValuationCompleteness)
            .ThenByDescending(quote => ParseTime(quote.ReceivedAt) ?? DateTime.MinValue)
            .ThenBy(quote => quote.Source, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool ShouldReplaceCachedQuote(MarketQuoteRecord existing, MarketQuoteRecord incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        DateTime? existingQuoteTime = ParseTime(existing.QuoteTime);
        DateTime? incomingQuoteTime = ParseTime(incoming.QuoteTime);

        if (existingQuoteTime.HasValue && !incomingQuoteTime.HasValue)
        {
            return false;
        }

        if (existingQuoteTime.HasValue && incomingQuoteTime.HasValue)
        {
            if (incomingQuoteTime.Value < existingQuoteTime.Value)
            {
                return false;
            }

            if (HasCompleteValuation(existing) && !HasCompleteValuation(incoming))
            {
                return false;
            }

            if (incomingQuoteTime.Value == existingQuoteTime.Value
                && ValuationCompleteness(incoming) < ValuationCompleteness(existing))
            {
                return false;
            }

            return true;
        }

        if (!existingQuoteTime.HasValue && incomingQuoteTime.HasValue)
        {
            return true;
        }

        int existingCompleteness = ValuationCompleteness(existing);
        int incomingCompleteness = ValuationCompleteness(incoming);
        if (incomingCompleteness != existingCompleteness)
        {
            return incomingCompleteness > existingCompleteness;
        }

        DateTime existingReceivedAt = ParseTime(existing.ReceivedAt) ?? DateTime.MinValue;
        DateTime incomingReceivedAt = ParseTime(incoming.ReceivedAt) ?? DateTime.MinValue;
        return incomingReceivedAt >= existingReceivedAt;
    }

    public static string BuildReplayQuoteSignature(IEnumerable<MarketQuoteRecord> quotes, DateTime now)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("D:")
            .Append(BeijingNaturalDayRangeProvider.FromNow(now).StartInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(';');

        foreach (MarketQuoteRecord quote in quotes
                     .OrderBy(quote => quote.MarketType, StringComparer.Ordinal)
                     .ThenBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(quote => quote.Source, StringComparer.Ordinal))
        {
            builder.Append(quote.Symbol).Append(',')
                .Append(quote.MarketType).Append(',')
                .Append(quote.Source).Append(',')
                .Append(quote.Price?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(quote.LastClose?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(quote.QuoteTime).Append(';');
        }

        return builder.ToString();
    }

    private static bool HasCompleteValuation(MarketQuoteRecord quote)
        => IsFinitePositive(quote.Price) && IsFinitePositive(quote.LastClose);

    private static int ValuationCompleteness(MarketQuoteRecord quote)
    {
        int score = 0;
        score += IsFinitePositive(quote.Price) ? 4 : 0;
        score += IsFinitePositive(quote.LastClose) ? 4 : 0;
        score += ParseTime(quote.QuoteTime).HasValue ? 2 : 0;
        score += IsFiniteValue(quote.ChangeValue) ? 1 : 0;
        score += IsFiniteValue(quote.ChangePercent) ? 1 : 0;
        return score;
    }

    private static bool CodeEquals(string? candidate, string code, string normalizedCode)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return string.Equals(candidate.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(normalizedCode)
                   && string.Equals(NormalizeCode(candidate), normalizedCode, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesCode(string? candidate, string code)
        => CodeEquals(candidate, code, NormalizeCode(code));

    private static string NormalizeCode(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static DateTime? ParseTime(string? value)
        => DateTime.TryParse(
               value,
               CultureInfo.InvariantCulture,
               DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
               out DateTime invariant)
            ? invariant
            : DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out DateTime current)
                ? current
                : null;

    private static bool IsFinitePositive(double? value)
        => IsFiniteValue(value) && value!.Value > 0;

    private static bool IsFiniteValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
}
