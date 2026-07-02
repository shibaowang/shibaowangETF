namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public readonly record struct BeijingNaturalDayRange(DateTime StartInclusive, DateTime EndExclusive)
{
    public bool Contains(DateTime value)
    {
        DateTime beijingTime = BeijingNaturalDayRangeProvider.NormalizeToBeijingLocal(value);
        return beijingTime >= StartInclusive && beijingTime < EndExclusive;
    }
}

public static class BeijingNaturalDayRangeProvider
{
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();

    public static BeijingNaturalDayRange FromNow(DateTime now)
        => ForBeijingDate(NormalizeToBeijingLocal(now).Date);

    public static BeijingNaturalDayRange ForBeijingDate(DateTime beijingDate)
    {
        DateTime start = beijingDate.Date;
        return new BeijingNaturalDayRange(start, start.AddDays(1));
    }

    public static DateTime NormalizeToBeijingLocal(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(value, BeijingTimeZone);
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return TimeZoneInfo.ConvertTime(value, BeijingTimeZone);
        }

        // Database timestamps in this project are stored as local Beijing time strings.
        return value;
    }

    private static TimeZoneInfo ResolveBeijingTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
