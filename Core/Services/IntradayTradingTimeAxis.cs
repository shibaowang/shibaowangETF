namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class IntradayTradingTimeAxis
{
    // LOCKED: Accepted index charts use US Eastern 09:30-16:00 ownership across Beijing midnight.
    public static readonly TimeSpan MorningOpen = new(9, 30, 0);
    public static readonly TimeSpan MorningClose = new(11, 30, 0);
    public static readonly TimeSpan AfternoonOpen = new(13, 0, 0);
    public static readonly TimeSpan AfternoonClose = new(15, 0, 0);
    public const double TotalTradingMinutes = 240d;

    public static readonly TimeSpan UsEasternOpen = new(9, 30, 0);
    public static readonly TimeSpan UsEasternClose = new(16, 0, 0);
    public const double UsEasternTotalTradingMinutes = 390d;

    public static IReadOnlyList<IntradayAxisTick> StandardTicks { get; } =
    [
        new("09:30", 0d),
        new("10:30", 60d / TotalTradingMinutes),
        new("11:30", 120d / TotalTradingMinutes),
        new("13:00", 120d / TotalTradingMinutes),
        new("14:00", 180d / TotalTradingMinutes),
        new("15:00", 1d)
    ];

    public static IReadOnlyList<IntradayAxisTick> UsEasternTicks { get; } =
    [
        new("09:30", 0d),
        new("12:00", 150d / UsEasternTotalTradingMinutes),
        new("14:00", 270d / UsEasternTotalTradingMinutes),
        new("16:00", 1d)
    ];

    public static bool IsTradingTime(DateTime time)
        => TryGetTradingMinuteIndex(time, out _);

    public static int GetTradingSession(DateTime time)
    {
        TimeSpan value = time.TimeOfDay;
        if (value >= MorningOpen && value <= MorningClose)
        {
            return 1;
        }

        if (value >= AfternoonOpen && value <= AfternoonClose)
        {
            return 2;
        }

        return 0;
    }

    public static bool TryGetTradingMinuteIndex(DateTime time, out double minuteIndex)
    {
        TimeSpan value = time.TimeOfDay;
        if (value >= MorningOpen && value <= MorningClose)
        {
            minuteIndex = (value - MorningOpen).TotalMinutes;
            return true;
        }

        if (value >= AfternoonOpen && value <= AfternoonClose)
        {
            minuteIndex = 120d + (value - AfternoonOpen).TotalMinutes;
            return true;
        }

        minuteIndex = 0d;
        return false;
    }

    public static bool TryGetXRatio(DateTime time, out double ratio)
    {
        if (!TryGetTradingMinuteIndex(time, out double minuteIndex))
        {
            ratio = 0d;
            return false;
        }

        ratio = Math.Clamp(minuteIndex / TotalTradingMinutes, 0d, 1d);
        return true;
    }

    public static bool TryConvertChinaTimeToUsEastern(DateTime chinaTime, out DateTime easternTime)
    {
        easternTime = default;
        if (!TryFindTimeZone("China Standard Time", "Asia/Shanghai", out TimeZoneInfo? chinaZone)
            || !TryFindTimeZone("Eastern Standard Time", "America/New_York", out TimeZoneInfo? easternZone)
            || chinaZone is null
            || easternZone is null)
        {
            return false;
        }

        DateTime unspecified = DateTime.SpecifyKind(chinaTime, DateTimeKind.Unspecified);
        easternTime = TimeZoneInfo.ConvertTime(unspecified, chinaZone, easternZone);
        return true;
    }

    public static bool TryConvertUsEasternToChina(DateTime easternTime, out DateTime chinaTime)
    {
        chinaTime = default;
        if (!TryFindTimeZone("Eastern Standard Time", "America/New_York", out TimeZoneInfo? easternZone)
            || !TryFindTimeZone("China Standard Time", "Asia/Shanghai", out TimeZoneInfo? chinaZone)
            || easternZone is null
            || chinaZone is null)
        {
            return false;
        }

        DateTime unspecified = DateTime.SpecifyKind(easternTime, DateTimeKind.Unspecified);
        chinaTime = TimeZoneInfo.ConvertTime(unspecified, easternZone, chinaZone);
        return true;
    }

    public static bool TryGetUsEasternMinuteIndex(DateTime chinaTime, out double minuteIndex)
    {
        minuteIndex = 0d;
        if (!TryConvertChinaTimeToUsEastern(chinaTime, out DateTime easternTime))
        {
            return false;
        }

        TimeSpan value = easternTime.TimeOfDay;
        if (value < UsEasternOpen || value > UsEasternClose)
        {
            return false;
        }

        minuteIndex = (value - UsEasternOpen).TotalMinutes;
        return true;
    }

    public static bool TryGetUsEasternXRatio(DateTime chinaTime, out double ratio)
    {
        if (!TryGetUsEasternMinuteIndex(chinaTime, out double minuteIndex))
        {
            ratio = 0d;
            return false;
        }

        ratio = Math.Clamp(minuteIndex / UsEasternTotalTradingMinutes, 0d, 1d);
        return true;
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
}

public sealed record IntradayAxisTick(string Label, double Ratio);
