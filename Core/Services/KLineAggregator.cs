using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class KLineAggregator
{
    public static IReadOnlyList<KLinePoint> FromHistoryPoints(IEnumerable<MarketHistoryPoint> points)
        => points
            .Where(point => point.Open > 0 && point.High > 0 && point.Low > 0 && point.Close > 0)
            .OrderBy(point => point.Date)
            .Select(point => new KLinePoint
            {
                Date = point.Date.Date,
                Open = point.Open,
                High = point.High,
                Low = point.Low,
                Close = point.Close,
                Volume = point.Volume,
                Amount = point.Amount
            })
            .ToArray();

    public static IReadOnlyList<KLinePoint> AggregateWeekly(IEnumerable<KLinePoint> daily)
        => Aggregate(daily, point => ResolveWeekStart(point.Date));

    public static IReadOnlyList<KLinePoint> AggregateMonthly(IEnumerable<KLinePoint> daily)
        => Aggregate(daily, point => new DateTime(point.Date.Year, point.Date.Month, 1));

    private static IReadOnlyList<KLinePoint> Aggregate(IEnumerable<KLinePoint> daily, Func<KLinePoint, DateTime> keySelector)
        => daily
            .Where(point => point.Open > 0 && point.High > 0 && point.Low > 0 && point.Close > 0)
            .OrderBy(point => point.Date)
            .GroupBy(keySelector)
            .Select(group =>
            {
                KLinePoint[] ordered = group.OrderBy(point => point.Date).ToArray();
                bool hasVolume = ordered.Any(point => point.Volume.HasValue);
                bool hasAmount = ordered.Any(point => point.Amount.HasValue);
                bool hasDisplayOnlyPoint = ordered.Any(point => point.IsDisplayOnly);
                return new KLinePoint
                {
                    Date = ordered[^1].Date,
                    Open = ordered[0].Open,
                    High = ordered.Max(point => point.High),
                    Low = ordered.Min(point => point.Low),
                    Close = ordered[^1].Close,
                    Volume = hasVolume ? ordered.Sum(point => point.Volume ?? 0) : null,
                    Amount = hasAmount ? ordered.Sum(point => point.Amount ?? 0) : null,
                    IsQuoteAdjusted = ordered.Any(point => point.IsQuoteAdjusted),
                    IsDisplayOnly = hasDisplayOnlyPoint,
                    PointSource = hasDisplayOnlyPoint
                        ? ordered.LastOrDefault(point => !string.IsNullOrWhiteSpace(point.PointSource))?.PointSource
                        : null
                };
            })
            .ToArray();

    public static DateTime ResolveWeekStart(DateTime date)
    {
        int offset = date.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)date.DayOfWeek - 1;
        return date.Date.AddDays(-offset);
    }
}
