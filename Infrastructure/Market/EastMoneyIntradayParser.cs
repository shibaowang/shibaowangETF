using System.Globalization;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class EastMoneyIntradayParser
{
    public static IReadOnlyList<IntradayPoint> ParsePoints(string json)
    {
        json = StripJsonp(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("trends", out JsonElement trends)
            || trends.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IntradayPoint>();
        }

        var points = new List<IntradayPoint>();
        foreach (JsonElement element in trends.EnumerateArray())
        {
            string? line = element.GetString();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length < 2
                || !TryParseTime(parts[0], out DateTime time)
                || !TryParse(parts[1], out double price)
                || price <= 0)
            {
                continue;
            }

            points.Add(new IntradayPoint
            {
                Time = time,
                Price = price,
                AveragePrice = parts.Length > 7 && TryParse(parts[7], out double average) ? average : null,
                Volume = parts.Length > 5 && TryParse(parts[5], out double volume) ? volume : null,
                Amount = parts.Length > 6 && TryParse(parts[6], out double amount) ? amount : null
            });
        }

        IntradayPoint[] uniquePoints = points
            .GroupBy(point => point.Time)
            .Select(group => group.Last())
            .OrderBy(point => point.Time)
            .ToArray();
        return IntradayVolumeNormalizer.Normalize(uniquePoints, IntradayVolumeFieldKind.Minute);
    }

    private static bool TryParseTime(string value, out DateTime time)
        => DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out time)
           || DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out time)
           || DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out time);

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
