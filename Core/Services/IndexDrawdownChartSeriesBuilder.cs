using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class IndexDrawdownChartSeriesBuilder
{
    public const string LeftChartSymbol = "251.NDXTMC";
    public const string RightChartSymbol = "100.NDX100";
    public const int DefaultMaxDisplayPoints = 126;
    public const int DefaultAxisLabelCount = 7;
    public const double DefaultAxisMinDrawdown = -0.25;
    public const double AxisTickStep = 0.05;

    public static IndexDrawdownChartSeries Build(
        IReadOnlyList<MarketHistoryPoint> historyPoints,
        MarketHistoryPoint? latestPoint = null,
        int maxDisplayPoints = DefaultMaxDisplayPoints,
        int axisLabelCount = DefaultAxisLabelCount)
    {
        ArgumentNullException.ThrowIfNull(historyPoints);

        MarketHistoryPoint[] allPoints = Normalize(historyPoints, latestPoint);
        if (allPoints.Length < 2)
        {
            return IndexDrawdownChartSeries.NotReady();
        }

        double historyHigh = allPoints.Max(point => point.High);
        if (historyHigh <= 0 || double.IsNaN(historyHigh) || double.IsInfinity(historyHigh))
        {
            return IndexDrawdownChartSeries.NotReady();
        }

        MarketHistoryPoint[] displayPoints = SelectDisplayWindow(allPoints, maxDisplayPoints);
        if (displayPoints.Length < 2)
        {
            return IndexDrawdownChartSeries.NotReady();
        }

        var rawPoints = new List<(MarketHistoryPoint Point, double Drawdown)>(displayPoints.Length);
        foreach (MarketHistoryPoint point in displayPoints)
        {
            rawPoints.Add((point, point.Close / historyHigh - 1.0));
        }

        double axisMinDrawdown = CalculateAxisMinDrawdown(rawPoints.Min(item => item.Drawdown));
        double denominator = Math.Max(1, displayPoints.Length - 1);
        var chartPoints = new List<IndexDrawdownChartPoint>(displayPoints.Length);
        for (int i = 0; i < rawPoints.Count; i++)
        {
            (MarketHistoryPoint point, double drawdown) = rawPoints[i];
            double yRatio = CalculateYRatio(drawdown, axisMinDrawdown);
            chartPoints.Add(new IndexDrawdownChartPoint(
                point.Date.Date,
                drawdown,
                i / denominator,
                yRatio,
                drawdown < axisMinDrawdown));
        }

        IReadOnlyList<IndexDrawdownAxisLabel> labels = BuildAxisLabels(displayPoints, axisLabelCount);
        IReadOnlyList<IndexDrawdownAxisTick> ticks = BuildAxisTicks(axisMinDrawdown);
        return new IndexDrawdownChartSeries(
            true,
            chartPoints,
            labels,
            ticks,
            historyHigh,
            chartPoints[^1].Drawdown,
            axisMinDrawdown);
    }

    public static double CalculateAxisMinDrawdown(double minDrawdown)
    {
        if (double.IsNaN(minDrawdown) || double.IsInfinity(minDrawdown) || minDrawdown >= DefaultAxisMinDrawdown)
        {
            return DefaultAxisMinDrawdown;
        }

        double steps = Math.Ceiling(Math.Abs(minDrawdown) / AxisTickStep);
        return -Math.Round(steps * AxisTickStep, 10);
    }

    public static double CalculateYRatio(double drawdown, double axisMinDrawdown)
    {
        double effectiveAxisMin = axisMinDrawdown < 0 ? axisMinDrawdown : DefaultAxisMinDrawdown;
        double clamped = Math.Clamp(drawdown, effectiveAxisMin, 0);
        return clamped / effectiveAxisMin;
    }

    public static IReadOnlyList<IndexDrawdownAxisTick> BuildAxisTicks(double axisMinDrawdown)
    {
        double effectiveAxisMin = axisMinDrawdown < 0 ? axisMinDrawdown : DefaultAxisMinDrawdown;
        int steps = (int)Math.Round(Math.Abs(effectiveAxisMin) / AxisTickStep, MidpointRounding.AwayFromZero);
        var ticks = new List<IndexDrawdownAxisTick>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double drawdown = -Math.Round(i * AxisTickStep, 10);
            ticks.Add(new IndexDrawdownAxisTick(
                drawdown,
                FormatTick(drawdown),
                CalculateYRatio(drawdown, effectiveAxisMin)));
        }

        return ticks;
    }

    public static IReadOnlyList<IndexDrawdownAreaPoint> BuildAreaPointRatios(IReadOnlyList<IndexDrawdownChartPoint> points)
    {
        if (points.Count == 0)
        {
            return Array.Empty<IndexDrawdownAreaPoint>();
        }

        var areaPoints = new List<IndexDrawdownAreaPoint>(points.Count + 2)
        {
            new(points[0].XRatio, 1)
        };
        areaPoints.AddRange(points.Select(point => new IndexDrawdownAreaPoint(point.XRatio, point.YRatio)));
        areaPoints.Add(new IndexDrawdownAreaPoint(points[^1].XRatio, 1));
        return areaPoints;
    }

    public static IndexDrawdownLabelPlacement PlaceLatestLabel(
        IReadOnlyList<IndexDrawdownChartPoint> points,
        double plotWidth,
        double plotHeight,
        double labelWidth,
        double labelHeight,
        IReadOnlyList<IndexDrawdownRect>? forbiddenRects = null,
        int recentPointCount = 8)
    {
        ArgumentNullException.ThrowIfNull(points);

        double safePlotWidth = Math.Max(1, plotWidth);
        double safePlotHeight = Math.Max(1, plotHeight);
        double safeLabelWidth = Math.Min(Math.Max(1, labelWidth), safePlotWidth);
        double safeLabelHeight = Math.Min(Math.Max(1, labelHeight), safePlotHeight);
        if (points.Count == 0)
        {
            return new IndexDrawdownLabelPlacement(0, 0, safeLabelWidth, safeLabelHeight, 0, 0, 0, 0);
        }

        IndexDrawdownChartPoint latest = points[^1];
        double latestX = Math.Clamp(latest.XRatio, 0, 1) * safePlotWidth;
        double latestY = Math.Clamp(latest.YRatio, 0, 1) * safePlotHeight;
        int firstRecentIndex = Math.Max(0, points.Count - Math.Max(1, recentPointCount));
        var recentPixels = points
            .Skip(firstRecentIndex)
            .Select(point => new IndexDrawdownPixelPoint(
                Math.Clamp(point.XRatio, 0, 1) * safePlotWidth,
                Math.Clamp(point.YRatio, 0, 1) * safePlotHeight))
            .ToArray();

        double recentMinY = recentPixels.Length == 0 ? latestY : recentPixels.Min(point => point.Y);
        double recentMaxY = recentPixels.Length == 0 ? latestY : recentPixels.Max(point => point.Y);
        double rightX = safePlotWidth - safeLabelWidth - 6;
        var candidates = new List<IndexDrawdownRect>
        {
            LabelRect(latestX + 10, latestY - safeLabelHeight - 10),
            LabelRect(latestX + 10, latestY + 10),
            LabelRect(latestX - safeLabelWidth - 10, latestY - safeLabelHeight - 10),
            LabelRect(latestX - safeLabelWidth - 10, latestY + 10),
            LabelRect(rightX, recentMinY - safeLabelHeight - 8),
            LabelRect(rightX, recentMaxY + 8),
            LabelRect(rightX, latestY - safeLabelHeight - 12),
            LabelRect(rightX, latestY + 12),
            LabelRect(rightX, 8),
            LabelRect(rightX, safePlotHeight - safeLabelHeight - 8)
        };

        IndexDrawdownRect chosen = candidates
            .OrderBy(rect => ScoreCandidate(rect, recentPixels, forbiddenRects, latestX, latestY))
            .First();

        (double endX, double endY) = NearestPointOnRect(chosen, latestX, latestY);
        return new IndexDrawdownLabelPlacement(
            chosen.X,
            chosen.Y,
            chosen.Width,
            chosen.Height,
            latestX,
            latestY,
            endX,
            endY);

        IndexDrawdownRect LabelRect(double x, double y)
        {
            double clampedX = Math.Clamp(x, 4, Math.Max(4, safePlotWidth - safeLabelWidth - 4));
            double clampedY = Math.Clamp(y, 4, Math.Max(4, safePlotHeight - safeLabelHeight - 4));
            return new IndexDrawdownRect(clampedX, clampedY, safeLabelWidth, safeLabelHeight);
        }
    }

    private static double ScoreCandidate(
        IndexDrawdownRect rect,
        IReadOnlyList<IndexDrawdownPixelPoint> recentPixels,
        IReadOnlyList<IndexDrawdownRect>? forbiddenRects,
        double latestX,
        double latestY)
    {
        IndexDrawdownRect padded = rect.Inflate(5);
        double score = 0;
        foreach (IndexDrawdownPixelPoint point in recentPixels)
        {
            if (padded.Contains(point.X, point.Y))
            {
                score += 1000;
            }
        }

        for (int i = 1; i < recentPixels.Count; i++)
        {
            if (SegmentIntersectsRect(recentPixels[i - 1], recentPixels[i], padded))
            {
                score += 800;
            }
        }

        if (forbiddenRects is not null)
        {
            foreach (IndexDrawdownRect forbidden in forbiddenRects)
            {
                if (rect.Intersects(forbidden))
                {
                    score += 10000;
                }
            }
        }

        double centerX = rect.X + rect.Width / 2;
        double centerY = rect.Y + rect.Height / 2;
        double distance = Math.Sqrt(Math.Pow(centerX - latestX, 2) + Math.Pow(centerY - latestY, 2));
        return score + distance / 1000.0;
    }

    private static bool SegmentIntersectsRect(IndexDrawdownPixelPoint a, IndexDrawdownPixelPoint b, IndexDrawdownRect rect)
    {
        if (rect.Contains(a.X, a.Y) || rect.Contains(b.X, b.Y))
        {
            return true;
        }

        double minX = Math.Min(a.X, b.X);
        double maxX = Math.Max(a.X, b.X);
        double minY = Math.Min(a.Y, b.Y);
        double maxY = Math.Max(a.Y, b.Y);
        if (maxX < rect.X || minX > rect.Right || maxY < rect.Y || minY > rect.Bottom)
        {
            return false;
        }

        return LinesIntersect(a.X, a.Y, b.X, b.Y, rect.X, rect.Y, rect.Right, rect.Y)
               || LinesIntersect(a.X, a.Y, b.X, b.Y, rect.Right, rect.Y, rect.Right, rect.Bottom)
               || LinesIntersect(a.X, a.Y, b.X, b.Y, rect.Right, rect.Bottom, rect.X, rect.Bottom)
               || LinesIntersect(a.X, a.Y, b.X, b.Y, rect.X, rect.Bottom, rect.X, rect.Y);
    }

    private static bool LinesIntersect(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double dx,
        double dy)
    {
        double denominator = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
        if (Math.Abs(denominator) < 0.0000001)
        {
            return false;
        }

        double ua = ((dx - cx) * (ay - cy) - (dy - cy) * (ax - cx)) / denominator;
        double ub = ((bx - ax) * (ay - cy) - (by - ay) * (ax - cx)) / denominator;
        return ua is >= 0 and <= 1 && ub is >= 0 and <= 1;
    }

    private static (double X, double Y) NearestPointOnRect(IndexDrawdownRect rect, double x, double y)
    {
        double clampedX = Math.Clamp(x, rect.X, rect.Right);
        double clampedY = Math.Clamp(y, rect.Y, rect.Bottom);
        if (!rect.Contains(x, y))
        {
            return (clampedX, clampedY);
        }

        double left = Math.Abs(x - rect.X);
        double right = Math.Abs(rect.Right - x);
        double top = Math.Abs(y - rect.Y);
        double bottom = Math.Abs(rect.Bottom - y);
        double min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (Math.Abs(min - left) < 0.0000001)
        {
            return (rect.X, clampedY);
        }

        if (Math.Abs(min - right) < 0.0000001)
        {
            return (rect.Right, clampedY);
        }

        if (Math.Abs(min - top) < 0.0000001)
        {
            return (clampedX, rect.Y);
        }

        return (clampedX, rect.Bottom);
    }

    private static string FormatTick(double drawdown)
    {
        if (Math.Abs(drawdown) < 0.0000001)
        {
            return "0%";
        }

        return (drawdown * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    // LOCKED: Accepted index drawdown latest quote behavior. Same-date latestPoint replaces display tail; do not persist it.
    private static MarketHistoryPoint[] Normalize(IReadOnlyList<MarketHistoryPoint> historyPoints, MarketHistoryPoint? latestPoint)
    {
        var normalized = historyPoints
            .Where(IsUsable)
            .GroupBy(point => point.Date.Date)
            .Select(group => group.OrderBy(point => point.Date).Last())
            .OrderBy(point => point.Date)
            .ToList();

        if (latestPoint is not null && IsUsable(latestPoint))
        {
            DateTime latestDate = latestPoint.Date.Date;
            DateTime lastHistoryDate = normalized.Count == 0 ? DateTime.MinValue : normalized[^1].Date.Date;
            if (latestDate > lastHistoryDate)
            {
                normalized.Add(CreateDisplayPointFromLatest(latestPoint, null));
            }
            else if (latestDate == lastHistoryDate && normalized.Count > 0)
            {
                normalized[^1] = CreateDisplayPointFromLatest(latestPoint, normalized[^1]);
            }
        }

        return normalized.ToArray();
    }

    private static MarketHistoryPoint CreateDisplayPointFromLatest(MarketHistoryPoint latestPoint, MarketHistoryPoint? existingPoint)
    {
        double high = existingPoint is null
            ? latestPoint.High
            : Math.Max(existingPoint.High, latestPoint.High);
        double low = existingPoint is null
            ? latestPoint.Low
            : Math.Min(existingPoint.Low, latestPoint.Low);

        return new MarketHistoryPoint
        {
            Date = latestPoint.Date.Date,
            Open = existingPoint?.Open > 0 ? existingPoint.Open : latestPoint.Open,
            Close = latestPoint.Close,
            High = high,
            Low = low
        };
    }

    private static MarketHistoryPoint[] SelectDisplayWindow(MarketHistoryPoint[] points, int maxDisplayPoints)
    {
        int cappedMaxPoints = Math.Max(2, maxDisplayPoints);
        DateTime latestDate = points[^1].Date.Date;
        DateTime startDate = latestDate.AddMonths(-6);
        MarketHistoryPoint[] recentPoints = points
            .Where(point => point.Date.Date >= startDate)
            .ToArray();

        if (recentPoints.Length < 2)
        {
            recentPoints = points;
        }

        return recentPoints.Length > cappedMaxPoints
            ? recentPoints.Skip(recentPoints.Length - cappedMaxPoints).ToArray()
            : recentPoints;
    }

    private static IReadOnlyList<IndexDrawdownAxisLabel> BuildAxisLabels(MarketHistoryPoint[] displayPoints, int labelCount)
    {
        int labelsToBuild = Math.Min(Math.Max(2, labelCount), displayPoints.Length);
        double denominator = Math.Max(1, displayPoints.Length - 1);
        var labels = new List<IndexDrawdownAxisLabel>(labelsToBuild);
        int previousIndex = -1;

        for (int i = 0; i < labelsToBuild; i++)
        {
            int index = (int)Math.Round(i * (displayPoints.Length - 1) / Math.Max(1.0, labelsToBuild - 1));
            index = Math.Clamp(index, 0, displayPoints.Length - 1);
            if (index == previousIndex)
            {
                continue;
            }

            previousIndex = index;
            DateTime date = displayPoints[index].Date.Date;
            labels.Add(new IndexDrawdownAxisLabel(
                date,
                date.ToString("MM-dd", CultureInfo.InvariantCulture),
                index / denominator));
        }

        return labels;
    }

    private static bool IsUsable(MarketHistoryPoint point)
        => point.Date != default
           && point.Close > 0
           && point.High > 0
           && !double.IsNaN(point.Close)
           && !double.IsInfinity(point.Close)
           && !double.IsNaN(point.High)
           && !double.IsInfinity(point.High);
}

public sealed record IndexDrawdownChartSeries(
    bool IsReady,
    IReadOnlyList<IndexDrawdownChartPoint> Points,
    IReadOnlyList<IndexDrawdownAxisLabel> AxisLabels,
    IReadOnlyList<IndexDrawdownAxisTick> AxisTicks,
    double? HistoryHigh,
    double? LatestDrawdown,
    double AxisMinDrawdown)
{
    public static IndexDrawdownChartSeries NotReady()
        => new(false, Array.Empty<IndexDrawdownChartPoint>(), Array.Empty<IndexDrawdownAxisLabel>(), IndexDrawdownChartSeriesBuilder.BuildAxisTicks(IndexDrawdownChartSeriesBuilder.DefaultAxisMinDrawdown), null, null, IndexDrawdownChartSeriesBuilder.DefaultAxisMinDrawdown);
}

public sealed record IndexDrawdownChartPoint(DateTime Date, double Drawdown, double XRatio, double YRatio, bool IsClippedToAxis);

public sealed record IndexDrawdownAxisLabel(DateTime Date, string Text, double XRatio);

public sealed record IndexDrawdownAxisTick(double Drawdown, string Text, double YRatio);

public sealed record IndexDrawdownAreaPoint(double XRatio, double YRatio);

public sealed record IndexDrawdownLabelPlacement(
    double X,
    double Y,
    double Width,
    double Height,
    double LeaderStartX,
    double LeaderStartY,
    double LeaderEndX,
    double LeaderEndY);

public sealed record IndexDrawdownRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y)
        => x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool Intersects(IndexDrawdownRect other)
        => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public IndexDrawdownRect Inflate(double value)
        => new(X - value, Y - value, Width + value * 2, Height + value * 2);
}

public sealed record IndexDrawdownPixelPoint(double X, double Y);
