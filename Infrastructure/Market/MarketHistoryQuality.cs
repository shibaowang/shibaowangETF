using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public enum MarketHistoryFrequency
{
    Invalid = 0,
    MonthlyLike = 1,
    WeeklyLike = 2,
    DailyLike = 3
}

public sealed record MarketHistoryQualityInfo(
    int KLineCount,
    DateTime? FirstDate,
    DateTime? LastDate,
    double? MedianIntervalDays,
    MarketHistoryFrequency Frequency,
    int PayloadLength,
    double? LatestClose,
    double? HistoryHigh)
{
    public bool IsValid => Frequency != MarketHistoryFrequency.Invalid;
}

public sealed record MarketHistoryOverwriteDecision(
    bool AllowOverwrite,
    string Code,
    string Detail);

public static class MarketHistoryQuality
{
    private const int DailyLikeMinimumCount = 200;
    private const double DailyLikeMaximumMedianIntervalDays = 7;
    private const double MonthlyLikeMinimumMedianIntervalDays = 20;

    public static MarketHistoryQualityInfo Analyze(string? rawPayload)
    {
        int payloadLength = rawPayload?.Length ?? 0;
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return Invalid(payloadLength);
        }

        IReadOnlyList<MarketHistoryPoint> points;
        try
        {
            points = EastMoneyHistoryParser.ParsePoints(rawPayload);
        }
        catch
        {
            return Invalid(payloadLength);
        }

        if (points.Count == 0)
        {
            return Invalid(payloadLength);
        }

        MarketHistoryPoint[] ordered = points
            .OrderBy(point => point.Date)
            .ToArray();
        double? medianInterval = CalculateMedianIntervalDays(ordered);
        MarketHistoryFrequency frequency = DetectFrequency(ordered.Length, medianInterval);

        return new MarketHistoryQualityInfo(
            ordered.Length,
            ordered[0].Date.Date,
            ordered[^1].Date.Date,
            medianInterval,
            frequency,
            payloadLength,
            ordered[^1].Close,
            ordered.Max(point => point.High));
    }

    public static MarketHistoryOverwriteDecision DecideOverwrite(
        string symbol,
        MarketHistoryQualityInfo? oldQuality,
        MarketHistoryQualityInfo newQuality,
        bool isCoreIndex)
    {
        if (!newQuality.IsValid)
        {
            return Skip("SKIP_HISTORY_INVALID", symbol, oldQuality, newQuality);
        }

        if (oldQuality is null || !oldQuality.IsValid)
        {
            return newQuality.Frequency == MarketHistoryFrequency.MonthlyLike
                ? Allow("HISTORY_DEGRADED_MONTHLY_FALLBACK", symbol, oldQuality, newQuality)
                : Allow("HISTORY_SAVE", symbol, oldQuality, newQuality);
        }

        if (isCoreIndex
            && oldQuality.Frequency == MarketHistoryFrequency.DailyLike
            && newQuality.Frequency != MarketHistoryFrequency.DailyLike)
        {
            return Skip("SKIP_HISTORY_DOWNGRADE", symbol, oldQuality, newQuality);
        }

        if (oldQuality.Frequency == MarketHistoryFrequency.DailyLike
            && newQuality.Frequency == MarketHistoryFrequency.MonthlyLike)
        {
            return Skip("SKIP_HISTORY_DOWNGRADE", symbol, oldQuality, newQuality);
        }

        if (oldQuality.Frequency == MarketHistoryFrequency.DailyLike
            && newQuality.Frequency == MarketHistoryFrequency.DailyLike
            && oldQuality.FirstDate.HasValue
            && newQuality.FirstDate.HasValue
            && oldQuality.LastDate.HasValue
            && newQuality.LastDate.HasValue
            && newQuality.KLineCount < oldQuality.KLineCount
            && newQuality.FirstDate.Value.Date > oldQuality.FirstDate.Value.Date
            && newQuality.LastDate.Value.Date <= oldQuality.LastDate.Value.Date.AddDays(7))
        {
            return Skip("SKIP_HISTORY_SHRINK", symbol, oldQuality, newQuality);
        }

        bool newDailyNotOlder = newQuality.Frequency == MarketHistoryFrequency.DailyLike
                                && (!oldQuality.LastDate.HasValue
                                    || !newQuality.LastDate.HasValue
                                    || newQuality.LastDate.Value.Date >= oldQuality.LastDate.Value.Date);
        if (newQuality.KLineCount < oldQuality.KLineCount * 0.5
            && !newDailyNotOlder)
        {
            return Skip("SKIP_HISTORY_SHRINK", symbol, oldQuality, newQuality);
        }

        if (oldQuality.LastDate.HasValue
            && newQuality.LastDate.HasValue
            && newQuality.LastDate.Value.Date < oldQuality.LastDate.Value.Date
            && newQuality.Frequency <= oldQuality.Frequency)
        {
            return Skip("SKIP_HISTORY_STALE", symbol, oldQuality, newQuality);
        }

        return Allow("HISTORY_SAVE", symbol, oldQuality, newQuality);
    }

    public static bool IsDailyLike(string? rawPayload)
        => Analyze(rawPayload).Frequency == MarketHistoryFrequency.DailyLike;

    private static MarketHistoryQualityInfo Invalid(int payloadLength)
        => new(0, null, null, null, MarketHistoryFrequency.Invalid, payloadLength, null, null);

    private static MarketHistoryFrequency DetectFrequency(int count, double? medianIntervalDays)
    {
        if (count <= 0 || !medianIntervalDays.HasValue)
        {
            return MarketHistoryFrequency.Invalid;
        }

        if (count >= DailyLikeMinimumCount
            && medianIntervalDays.Value <= DailyLikeMaximumMedianIntervalDays)
        {
            return MarketHistoryFrequency.DailyLike;
        }

        if (medianIntervalDays.Value >= MonthlyLikeMinimumMedianIntervalDays)
        {
            return MarketHistoryFrequency.MonthlyLike;
        }

        if (medianIntervalDays.Value <= DailyLikeMaximumMedianIntervalDays)
        {
            return MarketHistoryFrequency.WeeklyLike;
        }

        return MarketHistoryFrequency.MonthlyLike;
    }

    private static double? CalculateMedianIntervalDays(IReadOnlyList<MarketHistoryPoint> ordered)
    {
        if (ordered.Count < 2)
        {
            return null;
        }

        double[] intervals = ordered
            .Skip(1)
            .Select((point, index) => (point.Date.Date - ordered[index].Date.Date).TotalDays)
            .Where(interval => interval > 0)
            .OrderBy(interval => interval)
            .ToArray();
        if (intervals.Length == 0)
        {
            return null;
        }

        int middle = intervals.Length / 2;
        return intervals.Length % 2 == 1
            ? intervals[middle]
            : (intervals[middle - 1] + intervals[middle]) / 2.0;
    }

    private static MarketHistoryOverwriteDecision Allow(
        string code,
        string symbol,
        MarketHistoryQualityInfo? oldQuality,
        MarketHistoryQualityInfo newQuality)
        => new(true, code, BuildDetail(code, symbol, oldQuality, newQuality));

    private static MarketHistoryOverwriteDecision Skip(
        string code,
        string symbol,
        MarketHistoryQualityInfo? oldQuality,
        MarketHistoryQualityInfo newQuality)
        => new(false, code, BuildDetail(code, symbol, oldQuality, newQuality));

    private static string BuildDetail(
        string code,
        string symbol,
        MarketHistoryQualityInfo? oldQuality,
        MarketHistoryQualityInfo newQuality)
        => string.Join("|",
            code,
            symbol,
            "old=" + FormatFrequency(oldQuality),
            "new=" + FormatFrequency(newQuality),
            "old_count=" + (oldQuality?.KLineCount.ToString() ?? "0"),
            "new_count=" + newQuality.KLineCount,
            "old_last=" + FormatDate(oldQuality?.LastDate),
            "new_last=" + FormatDate(newQuality.LastDate));

    private static string FormatFrequency(MarketHistoryQualityInfo? quality)
        => quality?.Frequency switch
        {
            MarketHistoryFrequency.DailyLike => "daily",
            MarketHistoryFrequency.WeeklyLike => "weekly",
            MarketHistoryFrequency.MonthlyLike => "monthly",
            _ => "invalid"
        };

    private static string FormatDate(DateTime? date)
        => date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "--";
}
