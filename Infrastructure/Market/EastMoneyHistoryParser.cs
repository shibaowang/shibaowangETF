using System.Globalization;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class EastMoneyHistoryParser
{
    public static IReadOnlyList<MarketHistoryPoint> ParsePoints(string json)
    {
        json = StripJsonp(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("klines", out JsonElement klines)
            || klines.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MarketHistoryPoint>();
        }

        var points = new List<MarketHistoryPoint>();
        foreach (JsonElement element in klines.EnumerateArray())
        {
            string? line = element.GetString();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length < 5
                || !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                || !TryParse(parts[1], out double open)
                || !TryParse(parts[2], out double close)
                || !TryParse(parts[3], out double high)
                || !TryParse(parts[4], out double low))
            {
                continue;
            }

            points.Add(new MarketHistoryPoint
            {
                Date = date,
                Open = open,
                Close = close,
                High = high,
                Low = low,
                Volume = parts.Length > 5 && TryParse(parts[5], out double volume) ? volume : null,
                Amount = parts.Length > 6 && TryParse(parts[6], out double amount) ? amount : null
            });
        }

        return points;
    }

    public static double? ParseHigh(string json)
    {
        IReadOnlyList<MarketHistoryPoint> points = ParsePoints(json);
        return points.Count == 0 ? null : points.Max(point => point.High);
    }

    public static double? CalculateLatestDrawdown(IReadOnlyList<MarketHistoryPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        double runningHigh = 0;
        double latestDrawdown = 0;
        foreach (MarketHistoryPoint point in points.OrderBy(point => point.Date))
        {
            runningHigh = Math.Max(runningHigh, point.High);
            if (runningHigh > 0)
            {
                latestDrawdown = point.Close / runningHigh - 1.0;
            }
        }

        return latestDrawdown;
    }

    private static bool TryParse(string value, out double number)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static string StripJsonp(string text)
    {
        string value = text.Trim();
        int firstBrace = value.IndexOf('{');
        int lastBrace = value.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? value[firstBrace..(lastBrace + 1)]
            : value;
    }
}
