using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class SinaFundQuoteParser
{
    public static IReadOnlyList<MarketQuoteRecord> Parse(string payload, DateTimeOffset receivedAt)
    {
        var records = new List<MarketQuoteRecord>();
        foreach (string line in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int nameStart = line.IndexOf("hq_str_f_", StringComparison.Ordinal);
            int valueStart = line.IndexOf('"');
            int valueEnd = line.LastIndexOf('"');
            if (nameStart < 0 || valueStart < 0 || valueEnd <= valueStart)
            {
                continue;
            }

            int codeStart = nameStart + "hq_str_f_".Length;
            int codeEnd = line.IndexOf('=', codeStart);
            string code = codeEnd > codeStart ? line[codeStart..codeEnd] : string.Empty;
            string[] parts = line[(valueStart + 1)..valueEnd].Split(',');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            double? navNow = ParseDouble(Get(parts, 1));
            double? navLast = ParseDouble(Get(parts, 3));
            if ((!navNow.HasValue || navNow.Value <= 0) && navLast.HasValue && navLast.Value > 0)
            {
                navNow = navLast;
            }

            if ((!navLast.HasValue || navLast.Value <= 0) && navNow.HasValue && navNow.Value > 0)
            {
                navLast = navNow;
            }

            records.Add(new MarketQuoteRecord
            {
                Symbol = code,
                DisplayName = Get(parts, 0),
                MarketType = "OTC",
                Source = MarketSources.SinaFund,
                Price = navNow,
                LastClose = navLast,
                QuoteTime = Get(parts, 4) ?? receivedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ReceivedAt = receivedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                RawCode = "f_" + code,
                RawPayload = line
            });
        }

        return records;
    }

    private static string? Get(string[] parts, int index)
        => index >= 0 && index < parts.Length && !string.IsNullOrWhiteSpace(parts[index]) ? parts[index].Trim() : null;

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
}
