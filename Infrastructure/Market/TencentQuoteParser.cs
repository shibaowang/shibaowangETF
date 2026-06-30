using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class TencentQuoteParser
{
    public static IReadOnlyList<MarketQuoteRecord> Parse(string payload, DateTimeOffset receivedAt)
    {
        var records = new List<MarketQuoteRecord>();
        foreach (string line in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int codeStart = line.IndexOf("v_", StringComparison.Ordinal);
            int valueStart = line.IndexOf('"');
            int valueEnd = line.LastIndexOf('"');
            if (codeStart < 0 || valueStart < 0 || valueEnd <= valueStart)
            {
                continue;
            }

            string rawCode = line[(codeStart + 2)..line.IndexOf('=', codeStart)];
            string[] parts = line[(valueStart + 1)..valueEnd].Split('~');
            if (parts.Length < 4)
            {
                continue;
            }

            string symbol = MarketSymbolNormalizer.DigitsOnly(rawCode);
            double? currentPrice = ParseDouble(Get(parts, 3));
            double? lastClose = ParseDouble(Get(parts, 4));
            double? iopv = FindIopv(parts);
            records.Add(new MarketQuoteRecord
            {
                Symbol = symbol,
                DisplayName = Get(parts, 1),
                MarketType = "ETF",
                Source = MarketSources.Tencent,
                Price = currentPrice,
                LastClose = lastClose,
                OpenValue = ParseDouble(Get(parts, 5)),
                ChangeValue = currentPrice.HasValue && lastClose.HasValue ? currentPrice.Value - lastClose.Value : null,
                ChangePercent = currentPrice.HasValue && lastClose.HasValue && lastClose.Value > 0
                    ? currentPrice.Value / lastClose.Value - 1.0
                    : null,
                HighValue = ParseDouble(Get(parts, 33)),
                LowValue = ParseDouble(Get(parts, 34)),
                Volume = ParseDouble(Get(parts, 36)),
                Amount = ParseDouble(Get(parts, 37)),
                Iopv = iopv.HasValue && iopv.Value > 0 ? iopv : null,
                QuoteTime = ParseTencentTime(Get(parts, 30)),
                ReceivedAt = receivedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                RawCode = rawCode,
                RawPayload = line
            });
        }

        return records;
    }

    private static string? Get(string[] parts, int index)
        => index >= 0 && index < parts.Length && !string.IsNullOrWhiteSpace(parts[index]) ? parts[index].Trim() : null;

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;

    private static double? FindIopv(string[] parts)
    {
        for (int index = 4; index < parts.Length; index++)
        {
            if (parts[index].Contains("CNY", StringComparison.OrdinalIgnoreCase) && index >= 4)
            {
                return ParseDouble(parts[index - 4]);
            }
        }

        return null;
    }

    private static string? ParseTencentTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 14)
        {
            return null;
        }

        return DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("yyyy-MM-dd HH:mm:ss")
            : null;
    }
}
