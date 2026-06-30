using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class IndexIntradayCacheCompletenessService
{
    // LOCKED: Accepted index intraday full-session behavior. Do not change completeness without updating docs/LOCKED_MODULES.md and user confirmation.
    private static readonly TimeSpan CompleteSessionThreshold = new(15, 55, 0);
    private static readonly TimeSpan OpenSessionThreshold = new(9, 35, 0);

    public static IndexIntradayCacheCompleteness Analyze(
        IReadOnlyList<IntradayPoint> points,
        DateTimeOffset now)
    {
        DateOnly latestCompletedTradeDate = GetLatestCompletedUsTradeDate(now);
        IndexIntradayPointInfo[] converted = points
            .Where(point => !point.IsQuoteTail
                            && point.Price > 0
                            && IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(point.Time, out _))
            .Select(point =>
            {
                IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(point.Time, out DateTime easternTime);
                return new IndexIntradayPointInfo(point.Time, easternTime);
            })
            .OrderBy(point => point.EasternTime)
            .ToArray();

        if (converted.Length == 0)
        {
            return new IndexIntradayCacheCompleteness(
                false,
                true,
                latestCompletedTradeDate,
                null,
                null,
                null,
                0,
                "missing real index intraday points");
        }

        IndexIntradayPointInfo first = converted[0];
        IndexIntradayPointInfo last = converted[^1];
        DateOnly cacheTradeDate = DateOnly.FromDateTime(last.EasternTime.Date);
        bool isLatestTradeDate = cacheTradeDate == latestCompletedTradeDate;
        bool reachesOpen = first.EasternTime.TimeOfDay <= OpenSessionThreshold;
        bool reachesClose = last.EasternTime.TimeOfDay >= CompleteSessionThreshold;
        bool isComplete = isLatestTradeDate && reachesOpen && reachesClose;
        string reason = isComplete
            ? "latest completed US session cache is complete"
            : !isLatestTradeDate
                ? "cache trade date is older than latest completed US session"
                : !reachesOpen
                    ? "latest completed US session cache is missing morning session"
                : "latest completed US session cache is partial";

        return new IndexIntradayCacheCompleteness(
            isComplete,
            !isComplete,
            latestCompletedTradeDate,
            cacheTradeDate,
            first.EasternTime.TimeOfDay,
            last.EasternTime.TimeOfDay,
            converted.Length,
            reason);
    }

    public static bool IsUsTradingSession(DateTimeOffset now)
    {
        DateTime eastern = ConvertToUsEastern(now);
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        TimeSpan time = eastern.TimeOfDay;
        return time >= IntradayTradingTimeAxis.UsEasternOpen && time <= IntradayTradingTimeAxis.UsEasternClose;
    }

    public static DateOnly GetLatestCompletedUsTradeDate(DateTimeOffset now)
    {
        DateTime eastern = ConvertToUsEastern(now);
        DateOnly candidate = DateOnly.FromDateTime(eastern.Date);
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return PreviousWeekday(candidate);
        }

        if (eastern.TimeOfDay < IntradayTradingTimeAxis.UsEasternClose)
        {
            return PreviousWeekday(candidate);
        }

        return candidate;
    }

    private static DateOnly PreviousWeekday(DateOnly date)
    {
        DateOnly candidate = date.AddDays(-1);
        while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            candidate = candidate.AddDays(-1);
        }

        return candidate;
    }

    private static DateTime ConvertToUsEastern(DateTimeOffset now)
    {
        if (TryFindTimeZone("Eastern Standard Time", "America/New_York", out TimeZoneInfo? easternZone)
            && easternZone is not null)
        {
            return TimeZoneInfo.ConvertTime(now, easternZone).DateTime;
        }

        return now.ToOffset(TimeSpan.FromHours(-5)).DateTime;
    }

    private static bool TryFindTimeZone(string windowsId, string ianaId, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        zone = null;
        return false;
    }

    private sealed record IndexIntradayPointInfo(DateTime ChinaTime, DateTime EasternTime);
}

public sealed record IndexIntradayCacheCompleteness(
    bool IsCompleteSession,
    bool ShouldCatchUp,
    DateOnly LatestCompletedTradeDate,
    DateOnly? CacheTradeDate,
    TimeSpan? FirstPointEasternTime,
    TimeSpan? LastPointEasternTime,
    int PointCount,
    string Reason);
