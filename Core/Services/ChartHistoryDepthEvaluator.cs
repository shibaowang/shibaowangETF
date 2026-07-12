using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartHistoryDepthEvaluator
{
    public const int DailyBasicTarget = 120;
    public const int WeeklyIdealTarget = 260;
    public const int MonthlyBasicTarget = 60;
    public const int MonthlyIdealTarget = 120;
    public static readonly TimeSpan ExhaustedSourceRecheckInterval = TimeSpan.FromDays(30);

    public static ChartHistoryDepthInfo Evaluate(IEnumerable<KLinePoint> dailyPoints)
    {
        KLinePoint[] daily = dailyPoints
            .Where(point => !point.IsDisplayOnly && IsValidPoint(point))
            .OrderBy(point => point.Date)
            .GroupBy(point => point.Date.Date)
            .Select(group => group.Last())
            .ToArray();
        int weeklyCount = KLineAggregator.AggregateWeekly(daily).Count;
        int monthlyCount = KLineAggregator.AggregateMonthly(daily).Count;
        bool dailySufficient = daily.Length >= DailyBasicTarget;
        bool weeklySufficient = weeklyCount >= WeeklyIdealTarget;
        bool monthlySufficient = monthlyCount >= MonthlyBasicTarget;
        string reason = monthlyCount >= MonthlyIdealTarget
            ? "月线历史达到理想深度"
            : monthlySufficient
                ? "月线历史达到基本深度"
                : $"月线仅{monthlyCount}根，少于基本目标{MonthlyBasicTarget}根";

        return new ChartHistoryDepthInfo(
            daily.Length,
            weeklyCount,
            monthlyCount,
            daily.FirstOrDefault()?.Date.Date,
            daily.LastOrDefault()?.Date.Date,
            dailySufficient,
            weeklySufficient,
            monthlySufficient,
            reason);
    }

    public static bool NeedsBackfill(SecurityChartPeriod period, ChartHistoryDepthInfo depth)
        => period switch
        {
            SecurityChartPeriod.Weekly => !depth.IsSufficientForWeekly,
            SecurityChartPeriod.Monthly => !depth.IsSufficientForMonthly,
            _ => false
        };

    public static bool ShouldSkipExhaustedSource(
        ChartHistoryDepthInfo current,
        ChartHistoryDepthCheckpoint? checkpoint,
        DateTimeOffset now)
        => checkpoint is
            {
                SourceExhausted: true
            }
           && current.DailyCount > 0
           && current.DailyCount >= checkpoint.DailyCount
           && current.EarliestDate.HasValue
           && checkpoint.EarliestDate.HasValue
           && current.EarliestDate.Value.Date <= checkpoint.EarliestDate.Value.Date
           && current.LatestDate.HasValue
           && checkpoint.LatestDate.HasValue
           && current.LatestDate.Value.Date >= checkpoint.LatestDate.Value.Date
           && checkpoint.CheckedAt <= now
           && now - checkpoint.CheckedAt < ExhaustedSourceRecheckInterval;

    public static ChartHistoryReplacementDecision DecideReplacement(
        IReadOnlyList<KLinePoint> existing,
        IReadOnlyList<KLinePoint> candidate)
    {
        if (!TryValidateDailySequence(candidate, out string validationReason))
        {
            return new ChartHistoryReplacementDecision(false, validationReason);
        }

        if (existing.Count == 0)
        {
            return new ChartHistoryReplacementDecision(true, "当前无有效日K缓存");
        }

        if (!TryValidateDailySequence(existing, out _))
        {
            return new ChartHistoryReplacementDecision(true, "当前缓存无效，采用合法DailyLike结果");
        }

        KLinePoint[] oldOrdered = existing.OrderBy(point => point.Date).ToArray();
        KLinePoint[] newOrdered = candidate.OrderBy(point => point.Date).ToArray();
        DateTime oldFirst = oldOrdered[0].Date.Date;
        DateTime oldLast = oldOrdered[^1].Date.Date;
        DateTime newFirst = newOrdered[0].Date.Date;
        DateTime newLast = newOrdered[^1].Date.Date;

        if (newLast < oldLast.AddDays(-7))
        {
            return new ChartHistoryReplacementDecision(false, "新结果最新日期明显落后现有缓存");
        }

        if (newOrdered.Length < oldOrdered.Length
            && newFirst > oldFirst
            && newLast <= oldLast.AddDays(7))
        {
            return new ChartHistoryReplacementDecision(false, "新结果是更短的滚动窗口，保留更深历史");
        }

        bool extendsHistory = newFirst < oldFirst && newLast >= oldLast.AddDays(-7);
        bool addsCurrentData = newLast > oldLast && newOrdered.Length >= oldOrdered.Length;
        bool equivalentOrBetter = newFirst <= oldFirst
                                  && newLast >= oldLast
                                  && newOrdered.Length >= oldOrdered.Length;
        return extendsHistory || addsCurrentData || equivalentOrBetter
            ? new ChartHistoryReplacementDecision(true, extendsHistory ? "新结果扩展了真实历史深度" : "新结果覆盖范围不劣于现有缓存")
            : new ChartHistoryReplacementDecision(false, "新结果未扩展历史且覆盖范围较弱");
    }

    public static string SerializeCheckpoint(ChartHistoryDepthCheckpoint checkpoint)
        => JsonSerializer.Serialize(checkpoint);

    public static ChartHistoryDepthCheckpoint? ParseCheckpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChartHistoryDepthCheckpoint>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool TryValidateDailySequence(IReadOnlyList<KLinePoint> points, out string reason)
    {
        if (points.Count == 0)
        {
            reason = "结果没有日K数据";
            return false;
        }

        DateTime? previous = null;
        var dates = new HashSet<DateTime>();
        foreach (KLinePoint point in points.OrderBy(point => point.Date))
        {
            if (!IsValidPoint(point))
            {
                reason = "结果包含无效OHLC";
                return false;
            }

            DateTime date = point.Date.Date;
            if (!dates.Add(date))
            {
                reason = "结果包含重复日期";
                return false;
            }

            if (previous.HasValue && date <= previous.Value)
            {
                reason = "结果日期无法严格排序";
                return false;
            }

            previous = date;
        }

        reason = "DailyLike序列有效";
        return true;
    }

    private static bool IsValidPoint(KLinePoint point)
        => IsFinitePositive(point.Open)
           && IsFinitePositive(point.High)
           && IsFinitePositive(point.Low)
           && IsFinitePositive(point.Close)
           && point.High >= Math.Max(point.Open, point.Close)
           && point.Low <= Math.Min(point.Open, point.Close)
           && point.High >= point.Low;

    private static bool IsFinitePositive(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
