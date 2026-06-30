using System.Globalization;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class EastMoneyQuoteParser
{
    public static IReadOnlyList<MarketQuoteRecord> Parse(string json, IReadOnlyDictionary<string, MarketWatchItem> requestedItems, DateTimeOffset receivedAt)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("diff", out JsonElement diff)
            || diff.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MarketQuoteRecord>();
        }

        var records = new List<MarketQuoteRecord>();
        foreach (JsonElement item in diff.EnumerateArray())
        {
            string? code = GetString(item, "f12");
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            MarketWatchItem? watchItem = FindWatchItem(requestedItems, code);
            string symbol = watchItem?.Symbol ?? code;
            records.Add(new MarketQuoteRecord
            {
                Symbol = symbol,
                DisplayName = watchItem?.DisplayName ?? GetString(item, "f14") ?? code,
                MarketType = watchItem?.MarketType ?? "INDEX",
                Source = MarketSources.EastMoney,
                Price = ScaleHundred(GetNumber(item, "f2"))
                    ?? ScaleHundred(GetNumber(item, "f43"))
                    ?? ScaleHundred(GetNumber(item, "f170")),
                ChangePercent = ScaleTenThousand(GetNumber(item, "f3")),
                ChangeValue = GetNumber(item, "f4"),
                HighValue = ScaleHundred(GetNumber(item, "f15")),
                LowValue = ScaleHundred(GetNumber(item, "f16")),
                OpenValue = ScaleHundred(GetNumber(item, "f17")),
                LastClose = ScaleHundred(GetNumber(item, "f18")),
                QuoteTime = receivedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ReceivedAt = receivedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                RawCode = watchItem?.RawCode ?? code,
                RawPayload = item.GetRawText()
            });
        }

        return records;
    }

    private static MarketWatchItem? FindWatchItem(IReadOnlyDictionary<string, MarketWatchItem> items, string code)
    {
        foreach (MarketWatchItem item in items.Values)
        {
            if (string.Equals(MarketSymbolNormalizer.ExtractEastMoneyTargetCode(item.RawCode), code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.RawCode, code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Symbol, code, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? GetNumber(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
                return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ScaleHundred(double? value)
        => value.HasValue ? value.Value / 100.0 : null;

    private static double? ScaleTenThousand(double? value)
        => value.HasValue ? value.Value / 10000.0 : null;
}
