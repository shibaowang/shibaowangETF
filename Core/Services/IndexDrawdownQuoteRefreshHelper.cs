using System.Globalization;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

// LOCKED: Accepted index drawdown refresh trigger. Track only 251.NDXTMC/100.NDX100 quote price/time.
public static class IndexDrawdownQuoteRefreshHelper
{
    private static readonly string[] Symbols =
    {
        IndexDrawdownChartSeriesBuilder.LeftChartSymbol,
        IndexDrawdownChartSeriesBuilder.RightChartSymbol
    };

    public static string BuildQuoteSignature(IEnumerable<MarketQuoteRecord> quotes)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        var builder = new StringBuilder();
        foreach (string symbol in Symbols)
        {
            MarketQuoteRecord? quote = quotes
                .Where(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(item.MarketType, "INDEX", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.ReceivedAt, StringComparer.Ordinal)
                .FirstOrDefault();

            builder.Append(symbol).Append('|')
                .Append(quote?.Price?.ToString("R", CultureInfo.InvariantCulture) ?? "--").Append('|')
                .Append(quote?.QuoteTime ?? string.Empty).Append('|')
                .Append(quote?.ReceivedAt ?? string.Empty).Append(';');
        }

        return builder.ToString();
    }

    public static bool HasQuoteChanged(IEnumerable<MarketQuoteRecord> previousQuotes, IEnumerable<MarketQuoteRecord> currentQuotes)
        => !string.Equals(BuildQuoteSignature(previousQuotes), BuildQuoteSignature(currentQuotes), StringComparison.Ordinal);
}
