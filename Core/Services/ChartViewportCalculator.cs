using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartViewportCalculator
{
    public const int MinimumVisibleCount = 20;
    public const int DailyDefaultVisibleCount = 120;
    public const int WeeklyDefaultVisibleCount = 104;
    public const int MonthlyDefaultVisibleCount = 60;

    public static int DefaultVisibleCount(SecurityChartPeriod period)
        => period switch
        {
            SecurityChartPeriod.Weekly => WeeklyDefaultVisibleCount,
            SecurityChartPeriod.Monthly => MonthlyDefaultVisibleCount,
            _ => DailyDefaultVisibleCount
        };

    public static ChartViewportState Reset(SecurityChartPeriod period, int totalCount)
        => Reset(totalCount, DefaultVisibleCount(period));

    public static ChartViewportState Reset(int totalCount, int requestedVisibleCount)
    {
        int total = Math.Max(0, totalCount);
        if (total == 0)
        {
            return new ChartViewportState(0, 0, 0, true);
        }

        int visible = Math.Clamp(requestedVisibleCount, Math.Min(MinimumVisibleCount, total), total);
        return new ChartViewportState(total, total - visible, visible, true);
    }

    public static ChartViewportState Reconcile(ChartViewportState? previous, SecurityChartPeriod period, int totalCount)
    {
        if (previous is null || previous.TotalCount == 0)
        {
            return Reset(period, totalCount);
        }

        int total = Math.Max(0, totalCount);
        if (total == 0)
        {
            return new ChartViewportState(0, 0, 0, true);
        }

        int visible = Math.Clamp(previous.VisibleCount, Math.Min(MinimumVisibleCount, total), total);
        int start = previous.IsAtLatestEdge
            ? total - visible
            : Math.Clamp(previous.VisibleStartIndex, 0, total - visible);
        return new ChartViewportState(total, start, visible, start + visible >= total);
    }

    public static ChartViewportState ZoomAt(ChartViewportState state, double anchorRatio, int wheelDelta)
    {
        ChartViewportState current = Clamp(state);
        if (current.TotalCount == 0 || wheelDelta == 0)
        {
            return current;
        }

        int minimum = Math.Min(MinimumVisibleCount, current.TotalCount);
        int step = Math.Max(2, (int)Math.Round(current.VisibleCount * 0.15, MidpointRounding.AwayFromZero));
        int requested = wheelDelta > 0
            ? current.VisibleCount - step
            : current.VisibleCount + step;
        int visible = Math.Clamp(requested, minimum, current.TotalCount);
        if (visible == current.VisibleCount)
        {
            return current;
        }

        double ratio = Math.Clamp(anchorRatio, 0, 1);
        int oldOffset = Math.Clamp((int)Math.Floor(ratio * current.VisibleCount), 0, current.VisibleCount - 1);
        int anchorIndex = current.VisibleStartIndex + oldOffset;
        int newOffset = Math.Clamp((int)Math.Floor(ratio * visible), 0, visible - 1);
        int start = Math.Clamp(anchorIndex - newOffset, 0, current.TotalCount - visible);
        return new ChartViewportState(current.TotalCount, start, visible, start + visible >= current.TotalCount);
    }

    public static ChartViewportState Pan(ChartViewportState state, int barsTowardHistory)
    {
        ChartViewportState current = Clamp(state);
        if (current.TotalCount == 0 || barsTowardHistory == 0)
        {
            return current;
        }

        int start = Math.Clamp(
            current.VisibleStartIndex - barsTowardHistory,
            0,
            current.TotalCount - current.VisibleCount);
        return new ChartViewportState(
            current.TotalCount,
            start,
            current.VisibleCount,
            start + current.VisibleCount >= current.TotalCount);
    }

    public static ChartViewportState Clamp(ChartViewportState state)
    {
        int total = Math.Max(0, state.TotalCount);
        if (total == 0)
        {
            return new ChartViewportState(0, 0, 0, true);
        }

        int visible = Math.Clamp(state.VisibleCount, Math.Min(MinimumVisibleCount, total), total);
        int start = Math.Clamp(state.VisibleStartIndex, 0, total - visible);
        return new ChartViewportState(total, start, visible, start + visible >= total);
    }

    public static ChartVisibleRange ResolveVisibleRange(ChartViewportState state)
    {
        ChartViewportState current = Clamp(state);
        return new ChartVisibleRange(current.VisibleStartIndex, current.VisibleCount);
    }
}

public sealed class ChartViewportStore
{
    private readonly Dictionary<SecurityChartPeriod, ChartViewportState> _states = new();

    public ChartViewportState Reconcile(SecurityChartPeriod period, int totalCount)
    {
        _states.TryGetValue(period, out ChartViewportState? current);
        ChartViewportState next = ChartViewportCalculator.Reconcile(current, period, totalCount);
        _states[period] = next;
        return next;
    }

    public ChartViewportState Reset(SecurityChartPeriod period, int totalCount)
    {
        ChartViewportState next = ChartViewportCalculator.Reset(period, totalCount);
        _states[period] = next;
        return next;
    }

    public void Set(SecurityChartPeriod period, ChartViewportState state)
        => _states[period] = ChartViewportCalculator.Clamp(state);

    public bool TryGet(SecurityChartPeriod period, out ChartViewportState state)
        => _states.TryGetValue(period, out state!);
}
