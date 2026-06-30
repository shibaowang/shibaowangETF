using System.Globalization;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class TencentIntradayParser
{
    public static IReadOnlyList<IntradayPoint> ParsePoints(string json)
    {
        json = StripJsonp(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!TryFindMinuteArray(document.RootElement, out JsonElement rows, out DateTime tradeDate))
        {
            return Array.Empty<IntradayPoint>();
        }

        var points = new List<IntradayPoint>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string? line = row.GetString();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !TryParseTime(tradeDate, parts[0], out DateTime time)
                || !TryParse(parts[1], out double price)
                || price <= 0)
            {
                continue;
            }

            points.Add(new IntradayPoint
            {
                Time = time,
                Price = price,
                Volume = parts.Length > 2 && TryParse(parts[2], out double volume) ? volume : null,
                Amount = parts.Length > 3 && TryParse(parts[3], out double amount) ? amount : null
            });
        }

        IntradayPoint[] uniquePoints = points
            .GroupBy(point => point.Time)
            .Select(group => group.Last())
            .OrderBy(point => point.Time)
            .ToArray();
        IntradayVolumeFieldKind kind = LooksCumulative(uniquePoints)
            ? IntradayVolumeFieldKind.Cumulative
            : IntradayVolumeFieldKind.Minute;
        return IntradayVolumeNormalizer.Normalize(uniquePoints, kind);
    }

    private static bool TryFindMinuteArray(JsonElement root, out JsonElement rows, out DateTime tradeDate)
    {
        rows = default;
        tradeDate = DateTime.Today;
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in data.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            JsonElement instrument = property.Value;
            DateTime candidateDate = tradeDate;
            if (instrument.TryGetProperty("date", out JsonElement directDate)
                && directDate.ValueKind == JsonValueKind.String
                && TryParseDate(directDate.GetString(), out DateTime parsedDirectDate))
            {
                candidateDate = parsedDirectDate;
            }

            if (!instrument.TryGetProperty("data", out JsonElement minuteData))
            {
                continue;
            }

            if (minuteData.ValueKind == JsonValueKind.Array)
            {
                rows = minuteData;
                tradeDate = candidateDate;
                return true;
            }

            if (minuteData.ValueKind == JsonValueKind.Object
                && minuteData.TryGetProperty("data", out JsonElement nestedRows)
                && nestedRows.ValueKind == JsonValueKind.Array)
            {
                if (minuteData.TryGetProperty("date", out JsonElement nestedDate)
                    && nestedDate.ValueKind == JsonValueKind.String
                    && TryParseDate(nestedDate.GetString(), out DateTime parsedNestedDate))
                {
                    candidateDate = parsedNestedDate;
                }

                rows = nestedRows;
                tradeDate = candidateDate;
                return true;
            }
        }

        return false;
    }

    private static bool LooksCumulative(IReadOnlyList<IntradayPoint> points)
        => IsNonDecreasing(points.Select(point => point.Volume))
           && IsNonDecreasing(points.Select(point => point.Amount));

    private static bool IsNonDecreasing(IEnumerable<double?> values)
    {
        double? previous = null;
        bool sawTwoValues = false;
        foreach (double? value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            double current = value.Value;
            if (previous.HasValue)
            {
                sawTwoValues = true;
                if (current + 0.0000001 < previous.Value)
                {
                    return false;
                }
            }

            previous = current;
        }

        return sawTwoValues;
    }

    private static bool TryParseTime(DateTime tradeDate, string value, out DateTime time)
    {
        string cleaned = value.Trim().Replace(":", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length == 4
            && int.TryParse(cleaned[..2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour)
            && int.TryParse(cleaned[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59)
        {
            time = tradeDate.Date.AddHours(hour).AddMinutes(minute);
            return true;
        }

        return DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out time)
               || DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out time);
    }

    private static bool TryParseDate(string? value, out DateTime date)
        => DateTime.TryParseExact(value?.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
           || DateTime.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
           || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
           || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);

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
