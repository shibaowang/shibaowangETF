using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class SparklineSeriesBuilder
{
    private const double DuplicateValueTolerance = 0.009999;

    public static IReadOnlyList<SparklinePoint> Build(
        IEnumerable<AccountReplayStateRecord> records,
        Func<AccountReplayStateRecord, double?> valueSelector,
        int maxPoints = 60)
    {
        var values = records
            .Select(record => new
            {
                record.Id,
                SortTime = ParseSortTime(record.CalculatedAt),
                Value = valueSelector(record)
            })
            .Where(point => point.Value.HasValue
                            && !double.IsNaN(point.Value.Value)
                            && !double.IsInfinity(point.Value.Value))
            .OrderBy(point => point.SortTime)
            .ThenBy(point => point.Id)
            .Select(point => new TimedSparklineValue(point.SortTime, point.Value!.Value))
            .ToList();

        return Build(values, maxPoints);
    }

    public static IReadOnlyList<SparklinePoint> Build(
        IEnumerable<AccountReplaySnapshotRecord> records,
        Func<AccountReplaySnapshotRecord, double?> valueSelector,
        int maxPoints = 60)
    {
        var values = records
            .Select(record => new
            {
                record.Id,
                SortTime = ParseSortTime(record.CreatedAt),
                Value = valueSelector(record)
            })
            .Where(point => point.Value.HasValue
                            && !double.IsNaN(point.Value.Value)
                            && !double.IsInfinity(point.Value.Value))
            .OrderBy(point => point.SortTime)
            .ThenBy(point => point.Id)
            .Select(point => new TimedSparklineValue(point.SortTime, point.Value!.Value))
            .ToList();

        return Build(values, maxPoints);
    }

    public static SparklineTrend GetTrend(IReadOnlyList<SparklinePoint> points)
    {
        if (points.Count < 2)
        {
            return SparklineTrend.Neutral;
        }

        return points[^1].Value >= points[0].Value ? SparklineTrend.Up : SparklineTrend.Down;
    }

    private static IReadOnlyList<SparklinePoint> Build(List<TimedSparklineValue> values, int maxPoints)
    {
        values = CompressConsecutiveDuplicateValues(values);

        if (maxPoints > 0 && values.Count > maxPoints)
        {
            values = values.Skip(values.Count - maxPoints).ToList();
        }

        if (values.Count == 0)
        {
            return Array.Empty<SparklinePoint>();
        }

        if (values.Count == 1)
        {
            return new[] { new SparklinePoint(0.5, 0.5, values[0].Value, values[0].Time) };
        }

        double min = values.Min(point => point.Value);
        double max = values.Max(point => point.Value);
        double range = max - min;
        var result = new List<SparklinePoint>(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            TimedSparklineValue point = values[index];
            double x = index / (double)(values.Count - 1);
            double y = range <= 0 ? 0.5 : 1.0 - ((point.Value - min) / range);
            result.Add(new SparklinePoint(x, y, point.Value, point.Time));
        }

        return result;
    }

    private static List<TimedSparklineValue> CompressConsecutiveDuplicateValues(IEnumerable<TimedSparklineValue> values)
    {
        var compressed = new List<TimedSparklineValue>();
        foreach (TimedSparklineValue value in values)
        {
            if (compressed.Count == 0
                || !AreSameValue(compressed[^1].Value, value.Value))
            {
                compressed.Add(value);
            }
        }

        return compressed;
    }

    private static bool AreSameValue(double left, double right)
        => Math.Abs(left - right) < DuplicateValueTolerance;

    private static DateTime ParseSortTime(string? value)
        => DateTime.TryParse(value, out DateTime parsed) ? parsed : DateTime.MinValue;

    private sealed record TimedSparklineValue(DateTime Time, double Value);
}

public enum SparklineTrend
{
    Neutral,
    Up,
    Down
}

public sealed record SparklinePoint(double X, double Y, double Value, DateTime Time);
