using System.Text.Json.Nodes;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class DailyKLineFreshnessService
{
    private const string QuotePointSourcePrefix = "QUOTE_";
    private static readonly TimeSpan AShareClose = new(15, 0, 0);

    public static DateOnly GetExpectedCompletedTradingDate(
        ChartInstrumentType instrumentType,
        DateTimeOffset now)
        => instrumentType == ChartInstrumentType.Index
            ? IndexIntradayCacheCompletenessService.GetLatestCompletedUsTradeDate(now)
            : GetLatestCompletedAShareTradeDate(now);

    public static DateOnly? GetLatestRealDailyDate(IEnumerable<KLinePoint> points)
    {
        DateTime? latest = points
            .Where(IsRealDailyPoint)
            .Select(point => (DateTime?)point.Date.Date)
            .Max();
        return latest.HasValue ? DateOnly.FromDateTime(latest.Value) : null;
    }

    public static bool NeedsTailCatchUp(
        IEnumerable<KLinePoint> points,
        DateOnly expectedCompletedTradingDate)
    {
        DateOnly? latest = GetLatestRealDailyDate(points);
        return !latest.HasValue || latest.Value < expectedCompletedTradingDate;
    }

    public static IReadOnlyList<KLinePoint> MergeRealDaily(
        IEnumerable<KLinePoint> existing,
        IEnumerable<KLinePoint> incoming)
    {
        var byDate = new SortedDictionary<DateTime, KLinePoint>();
        foreach (KLinePoint point in existing.Where(IsRealDailyPoint))
        {
            byDate[point.Date.Date] = CloneFormal(point);
        }

        foreach (KLinePoint point in incoming.Where(IsRealDailyPoint))
        {
            byDate[point.Date.Date] = CloneFormal(point);
        }

        return byDate.Values.ToArray();
    }

    public static string BuildMergedFormalPayload(
        IEnumerable<KLinePoint> points,
        string providerSource,
        DateOnly expectedCompletedTradingDate,
        DateTimeOffset mergedAt)
    {
        MarketHistoryPoint[] formalPoints = points
            .Where(IsRealDailyPoint)
            .OrderBy(point => point.Date)
            .Select(point => new MarketHistoryPoint
            {
                Date = point.Date.Date,
                Open = point.Open,
                Close = point.Close,
                High = point.High,
                Low = point.Low,
                Volume = point.Volume,
                Amount = point.Amount
            })
            .ToArray();

        JsonObject root = JsonNode.Parse(TencentHistoryParser.ToEastMoneyCompatiblePayload(formalPoints))!.AsObject();
        root["cross_etf_cache"] = new JsonObject
        {
            ["kind"] = "MERGED_REAL_DAILY_LIKE",
            ["provider_source"] = providerSource,
            ["expected_completed_trading_date"] = expectedCompletedTradingDate.ToString("yyyy-MM-dd"),
            ["merged_at"] = mergedAt.ToString("O")
        };
        return root.ToJsonString();
    }

    private static DateOnly GetLatestCompletedAShareTradeDate(DateTimeOffset now)
    {
        DateTime beijing = ConvertToBeijing(now);
        DateOnly candidate = DateOnly.FromDateTime(beijing.Date);
        if (beijing.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return PreviousWeekday(candidate);
        }

        return beijing.TimeOfDay >= AShareClose
            ? candidate
            : PreviousWeekday(candidate);
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

    private static DateTime ConvertToBeijing(DateTimeOffset now)
    {
        foreach (string timeZoneId in new[] { "China Standard Time", "Asia/Shanghai" })
        {
            try
            {
                return TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime;
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return now.ToOffset(TimeSpan.FromHours(8)).DateTime;
    }

    private static bool IsRealDailyPoint(KLinePoint point)
        => !point.IsDisplayOnly
           && (string.IsNullOrWhiteSpace(point.PointSource)
               || !point.PointSource.StartsWith(QuotePointSourcePrefix, StringComparison.OrdinalIgnoreCase))
           && point.Date != default
           && point.Open > 0
           && point.High > 0
           && point.Low > 0
           && point.Close > 0
           && point.High >= Math.Max(point.Open, point.Close)
           && point.Low <= Math.Min(point.Open, point.Close);

    private static KLinePoint CloneFormal(KLinePoint point)
        => new()
        {
            Date = point.Date.Date,
            Open = point.Open,
            High = point.High,
            Low = point.Low,
            Close = point.Close,
            Volume = point.Volume,
            Amount = point.Amount,
            IsQuoteAdjusted = false,
            IsDisplayOnly = false,
            PointSource = string.IsNullOrWhiteSpace(point.PointSource)
                || point.PointSource.StartsWith(QuotePointSourcePrefix, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : point.PointSource
        };
}
