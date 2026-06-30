using System.Globalization;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class TencentHistoryParser
{
    public static IReadOnlyList<MarketHistoryPoint> ParsePoints(string json)
    {
        json = StripJsonp(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!TryFindKLineArray(document.RootElement, out JsonElement rows))
        {
            return Array.Empty<MarketHistoryPoint>();
        }

        var points = new List<MarketHistoryPoint>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5)
            {
                continue;
            }

            if (!TryGetString(row, 0, out string? dateText)
                || !DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                || !TryGetDouble(row, 1, out double open)
                || !TryGetDouble(row, 2, out double close)
                || !TryGetDouble(row, 3, out double high)
                || !TryGetDouble(row, 4, out double low))
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
                Volume = TryGetDouble(row, 5, out double volume) ? volume : null,
                Amount = TryGetDouble(row, 6, out double amount) ? amount : null
            });
        }

        return points
            .OrderBy(point => point.Date)
            .ToArray();
    }

    public static string ToEastMoneyCompatiblePayload(string json)
    {
        IReadOnlyList<MarketHistoryPoint> points = ParsePoints(json);
        string[] lines = points
            .Select(point => string.Join(",",
                point.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Format(point.Open),
                Format(point.Close),
                Format(point.High),
                Format(point.Low),
                point.Volume.HasValue ? Format(point.Volume.Value) : string.Empty,
                point.Amount.HasValue ? Format(point.Amount.Value) : string.Empty))
            .Select(line => JsonSerializer.Serialize(line))
            .ToArray();

        return "{\"data\":{\"klines\":[" + string.Join(",", lines) + "]}}";
    }

    private static bool TryFindKLineArray(JsonElement root, out JsonElement rows)
    {
        rows = default;
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

            if (property.Value.TryGetProperty("qfqday", out JsonElement qfqday)
                && qfqday.ValueKind == JsonValueKind.Array)
            {
                rows = qfqday;
                return true;
            }

            if (property.Value.TryGetProperty("day", out JsonElement day)
                && day.ValueKind == JsonValueKind.Array)
            {
                rows = day;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetString(JsonElement row, int index, out string? value)
    {
        value = null;
        if (row.GetArrayLength() <= index)
        {
            return false;
        }

        JsonElement item = row[index];
        value = item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : item.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDouble(JsonElement row, int index, out double value)
    {
        value = 0;
        return row.GetArrayLength() > index
               && TryParse(row[index], out value);
    }

    private static bool TryParse(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string Format(double value)
        => value.ToString("0.########", CultureInfo.InvariantCulture);

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
