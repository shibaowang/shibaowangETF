namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record ChartViewportState(
    int TotalCount,
    int VisibleStartIndex,
    int VisibleCount,
    bool IsAtLatestEdge)
{
    public int VisibleEndExclusive => Math.Min(TotalCount, VisibleStartIndex + VisibleCount);
}

public sealed record ChartVisibleRange(int StartIndex, int Count)
{
    public int EndExclusive => StartIndex + Count;
}

public sealed record MovingAverageSeries(
    int Period,
    IReadOnlyList<double?> Values);

public enum ChartTradeMarkerType
{
    B,
    S
}

public sealed record ChartTradeMarker(
    DateTime PeriodKey,
    ChartTradeMarkerType MarkerType,
    DateTime TradeDate,
    int KLineIndex);

public sealed record ChartCrosshairState(
    bool IsVisible,
    int VisibleKLineIndex,
    double MouseX,
    double MouseY)
{
    public static ChartCrosshairState Hidden { get; } = new(false, -1, 0, 0);
}
