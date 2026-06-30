using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class IndexDrawdownChartSeriesBuilderTests
{
    [Fact]
    public void Build_UsesRecentSixMonthWindowAndDoesNotStartFromFullHistory()
    {
        IReadOnlyList<MarketHistoryPoint> points = GenerateDailyPoints(
            new DateTime(2019, 1, 1),
            new DateTime(2026, 6, 14));

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points);

        Assert.True(series.IsReady);
        Assert.True(series.Points.Count <= IndexDrawdownChartSeriesBuilder.DefaultMaxDisplayPoints);
        Assert.True(series.Points[0].Date >= new DateTime(2025, 12, 14));
        Assert.NotEqual(2019, series.Points[0].Date.Year);
    }

    [Fact]
    public void BuildDateLabels_UseMmDdAndMoveWithLatestDate()
    {
        IReadOnlyList<MarketHistoryPoint> points = GenerateDailyPoints(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 6, 13));
        var latestPoint = new MarketHistoryPoint
        {
            Date = new DateTime(2026, 6, 14),
            Open = 8800,
            Close = 8800,
            High = 8800,
            Low = 8800
        };

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points, latestPoint);

        Assert.True(series.IsReady);
        Assert.Equal("06-14", series.AxisLabels[^1].Text);
        Assert.DoesNotContain(series.AxisLabels, label => label.Text.Contains("2026", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CalculatesDrawdownFromHistoryHigh()
    {
        var points = new[]
        {
            Point(new DateTime(2026, 1, 1), close: 9800, high: 10000),
            Point(new DateTime(2026, 6, 14), close: 8800, high: 9000)
        };

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points);

        Assert.True(series.IsReady);
        Assert.Equal(10000, series.HistoryHigh);
        Assert.Equal(-0.12, series.LatestDrawdown!.Value, 6);
    }

    [Fact]
    public void Build_UsesLatestQuotePointForCurrentDrawdown()
    {
        var points = new[]
        {
            Point(new DateTime(2026, 1, 1), close: 9800, high: 10000),
            Point(new DateTime(2026, 6, 26), close: 9000, high: 9200)
        };
        var latestQuotePoint = new MarketHistoryPoint
        {
            Date = new DateTime(2026, 6, 29),
            Open = 9500,
            Close = 9500,
            High = 9500,
            Low = 9500
        };

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points, latestQuotePoint);

        Assert.True(series.IsReady);
        Assert.Equal(10000, series.HistoryHigh);
        Assert.Equal(-0.05, series.LatestDrawdown!.Value, 6);
        Assert.Equal(new DateTime(2026, 6, 29), series.Points[^1].Date);
    }

    [Fact]
    public void Build_ChangesDrawdownWhenLatestQuotePointChanges()
    {
        var points = new[]
        {
            Point(new DateTime(2026, 1, 1), close: 3000, high: 3071.70),
            Point(new DateTime(2026, 6, 26), close: 2800, high: 2850)
        };
        var firstQuotePoint = Point(new DateTime(2026, 6, 29), close: 2824.76, high: 2824.76);
        var secondQuotePoint = Point(new DateTime(2026, 6, 29), close: 2846.58, high: 2846.58);

        IndexDrawdownChartSeries first = IndexDrawdownChartSeriesBuilder.Build(points, firstQuotePoint);
        IndexDrawdownChartSeries second = IndexDrawdownChartSeriesBuilder.Build(points, secondQuotePoint);

        Assert.True(first.IsReady);
        Assert.True(second.IsReady);
        Assert.Equal(2824.76 / 3071.70 - 1, first.LatestDrawdown!.Value, 6);
        Assert.Equal(2846.58 / 3071.70 - 1, second.LatestDrawdown!.Value, 6);
        Assert.NotEqual(first.LatestDrawdown.Value, second.LatestDrawdown.Value);
    }

    [Fact]
    public void Build_SameDateLatestQuoteOverridesHistoryTailForCurrentDrawdown()
    {
        var points = new[]
        {
            Point(new DateTime(2026, 1, 1), close: 3000, high: 3071.70),
            Point(new DateTime(2026, 6, 29), close: 2818.00, high: 2850.00)
        };
        var latestQuotePoint = Point(new DateTime(2026, 6, 29), close: 2851.05, high: 2851.05);

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points, latestQuotePoint);

        Assert.True(series.IsReady);
        Assert.Equal(3071.70, series.HistoryHigh!.Value, 6);
        Assert.Equal(2851.05 / 3071.70 - 1, series.LatestDrawdown!.Value, 6);
        Assert.NotEqual(2818.00 / 3071.70 - 1, series.LatestDrawdown.Value);
    }

    [Fact]
    public void Build_SameDateLatestQuoteRaisesDisplayHighButKeepsLatestClose()
    {
        var points = new[]
        {
            Point(new DateTime(2026, 1, 1), close: 3000, high: 3071.70),
            Point(new DateTime(2026, 6, 29), close: 2818.00, high: 2820.00)
        };
        var latestQuotePoint = Point(new DateTime(2026, 6, 29), close: 3090.00, high: 3090.00);

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points, latestQuotePoint);

        Assert.True(series.IsReady);
        Assert.Equal(3090.00, series.HistoryHigh!.Value, 6);
        Assert.Equal(0, series.LatestDrawdown!.Value, 6);
    }

    [Fact]
    public void ChartSymbols_AreFixedToNasdaqTechnologyAndNasdaq100()
    {
        Assert.Equal("251.NDXTMC", IndexDrawdownChartSeriesBuilder.LeftChartSymbol);
        Assert.Equal("100.NDX100", IndexDrawdownChartSeriesBuilder.RightChartSymbol);
    }

    [Fact]
    public void Build_ReturnsNotReadyWhenHistoryKLinesUnavailable()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(Array.Empty<MarketHistoryPoint>());

        Assert.False(series.IsReady);
        Assert.Empty(series.Points);
        Assert.Empty(series.AxisLabels);
        Assert.Null(series.HistoryHigh);
        Assert.Null(series.LatestDrawdown);
    }

    [Fact]
    public void Build_LimitsFullHistoryCompressionToRecentDisplayPoints()
    {
        IReadOnlyList<MarketHistoryPoint> points = GenerateDailyPoints(
            new DateTime(2019, 4, 1),
            new DateTime(2026, 6, 14));

        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(points);

        Assert.True(series.IsReady);
        Assert.Equal(IndexDrawdownChartSeriesBuilder.DefaultMaxDisplayPoints, series.Points.Count);
        Assert.True(series.Points[0].Date > new DateTime(2019, 4, 1));
        Assert.Equal(new DateTime(2026, 6, 14), series.Points[^1].Date);
    }

    [Fact]
    public void Build_ExpandsAxisWhenDrawdownFallsBelowTwentyFivePercent()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(PointsFromDrawdowns(-0.10, -0.22, -0.27, -0.28, -0.26));

        Assert.True(series.IsReady);
        Assert.True(series.AxisMinDrawdown <= -0.30);
        IndexDrawdownChartPoint[] deepPoints = series.Points
            .Where(point => point.Drawdown <= -0.26)
            .ToArray();
        Assert.All(deepPoints, point => Assert.True(point.YRatio < 1));
        Assert.True(deepPoints.Select(point => Math.Round(point.YRatio, 6)).Distinct().Count() > 1);
    }

    [Fact]
    public void Build_KeepsDefaultAxisWhenDrawdownStaysWithinTwentyFivePercent()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(PointsFromDrawdowns(-0.03, -0.08, -0.12, -0.20));

        Assert.True(series.IsReady);
        Assert.Equal(-0.25, series.AxisMinDrawdown, 6);
    }

    [Fact]
    public void BuildAxisTicks_ExtendsInFivePercentSteps()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(PointsFromDrawdowns(-0.08, -0.16, -0.32));

        Assert.True(series.IsReady);
        Assert.Equal(-0.35, series.AxisMinDrawdown, 6);
        Assert.Contains(series.AxisTicks, tick => tick.Text == "-25%");
        Assert.Contains(series.AxisTicks, tick => tick.Text == "-30%");
        Assert.Contains(series.AxisTicks, tick => tick.Text == "-35%");
    }

    [Fact]
    public void Build_AllowsHorizontalLineWhenDrawdownValuesAreTrulyEqual()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(PointsFromDrawdowns(-0.12, -0.12, -0.12, -0.12));

        Assert.True(series.IsReady);
        Assert.Single(series.Points.Select(point => Math.Round(point.YRatio, 6)).Distinct());
        Assert.DoesNotContain(series.Points, point => point.IsClippedToAxis);
    }

    [Fact]
    public void BuildAreaPointRatios_UsesCurvePointsWithoutChangingThem()
    {
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(PointsFromDrawdowns(-0.03, -0.08, -0.12, -0.20));

        IReadOnlyList<IndexDrawdownAreaPoint> areaPoints = IndexDrawdownChartSeriesBuilder.BuildAreaPointRatios(series.Points);

        Assert.Equal(series.Points.Count + 2, areaPoints.Count);
        Assert.Equal(series.Points[0].XRatio, areaPoints[0].XRatio);
        Assert.Equal(1, areaPoints[0].YRatio);
        for (int i = 0; i < series.Points.Count; i++)
        {
            Assert.Equal(series.Points[i].XRatio, areaPoints[i + 1].XRatio);
            Assert.Equal(series.Points[i].YRatio, areaPoints[i + 1].YRatio);
        }
        Assert.Equal(series.Points[^1].XRatio, areaPoints[^1].XRatio);
        Assert.Equal(1, areaPoints[^1].YRatio);
    }

    [Fact]
    public void PlaceLatestLabel_AvoidsRecentCurvePoints()
    {
        var points = new[]
        {
            ChartPoint(0.00, 0.55),
            ChartPoint(0.82, 0.30),
            ChartPoint(0.88, 0.26),
            ChartPoint(0.92, 0.31),
            ChartPoint(0.96, 0.25),
            ChartPoint(1.00, 0.29)
        };
        double plotWidth = 500;
        double plotHeight = 220;

        IndexDrawdownLabelPlacement placement = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
            points,
            plotWidth,
            plotHeight,
            labelWidth: 96,
            labelHeight: 30);

        var labelRect = new IndexDrawdownRect(placement.X, placement.Y, placement.Width, placement.Height).Inflate(2);
        foreach (IndexDrawdownChartPoint point in points.TakeLast(5))
        {
            Assert.False(labelRect.Contains(point.XRatio * plotWidth, point.YRatio * plotHeight));
        }
    }

    [Fact]
    public void PlaceLatestLabel_StaysInsidePlotWhenLatestPointIsAtRightEdge()
    {
        var points = new[]
        {
            ChartPoint(0.00, 0.55),
            ChartPoint(0.90, 0.12),
            ChartPoint(1.00, 0.10)
        };

        IndexDrawdownLabelPlacement placement = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
            points,
            plotWidth: 120,
            plotHeight: 80,
            labelWidth: 72,
            labelHeight: 26);

        Assert.True(placement.X >= 0);
        Assert.True(placement.Y >= 0);
        Assert.True(placement.X + placement.Width <= 120);
        Assert.True(placement.Y + placement.Height <= 80);
    }

    [Fact]
    public void PlaceLatestLabel_AvoidsForbiddenWarningArea()
    {
        var points = new[]
        {
            ChartPoint(0.20, 0.60),
            ChartPoint(0.80, 0.40)
        };
        var forbidden = new[]
        {
            new IndexDrawdownRect(326, 46, 80, 26)
        };

        IndexDrawdownLabelPlacement placement = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
            points,
            plotWidth: 420,
            plotHeight: 180,
            labelWidth: 80,
            labelHeight: 26,
            forbiddenRects: forbidden);

        var labelRect = new IndexDrawdownRect(placement.X, placement.Y, placement.Width, placement.Height);
        Assert.DoesNotContain(forbidden, rect => labelRect.Intersects(rect));
    }

    [Fact]
    public void PlaceLatestLabel_LeaderStartsAtLatestPointAndEndsOnLabel()
    {
        var points = new[]
        {
            ChartPoint(0.00, 0.50),
            ChartPoint(0.80, 0.35)
        };

        IndexDrawdownLabelPlacement placement = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
            points,
            plotWidth: 300,
            plotHeight: 200,
            labelWidth: 88,
            labelHeight: 28);

        Assert.Equal(240, placement.LeaderStartX, 6);
        Assert.Equal(70, placement.LeaderStartY, 6);
        Assert.True(placement.LeaderEndX >= placement.X && placement.LeaderEndX <= placement.X + placement.Width);
        Assert.True(placement.LeaderEndY >= placement.Y && placement.LeaderEndY <= placement.Y + placement.Height);
    }

    [Fact]
    public void PlaceLatestLabel_DoesNotChangeOriginalCurvePoints()
    {
        var points = new[]
        {
            ChartPoint(0.00, 0.55),
            ChartPoint(0.60, 0.32),
            ChartPoint(1.00, 0.29)
        };
        var snapshot = points
            .Select(point => (point.Date, point.Drawdown, point.XRatio, point.YRatio, point.IsClippedToAxis))
            .ToArray();

        _ = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
            points,
            plotWidth: 500,
            plotHeight: 220,
            labelWidth: 96,
            labelHeight: 30);

        Assert.Equal(snapshot, points.Select(point => (point.Date, point.Drawdown, point.XRatio, point.YRatio, point.IsClippedToAxis)).ToArray());
    }

    private static IReadOnlyList<MarketHistoryPoint> GenerateDailyPoints(DateTime start, DateTime end)
    {
        var points = new List<MarketHistoryPoint>();
        int offset = 0;
        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            double close = 9000 + offset;
            points.Add(Point(date, close, close + 100));
            offset++;
        }

        return points;
    }

    private static MarketHistoryPoint Point(DateTime date, double close, double high)
        => new()
        {
            Date = date,
            Open = close,
            Close = close,
            High = high,
            Low = close
        };

    private static IReadOnlyList<MarketHistoryPoint> PointsFromDrawdowns(params double[] drawdowns)
    {
        return drawdowns
            .Select((drawdown, index) => Point(
                new DateTime(2026, 1, 1).AddDays(index),
                close: 100 * (1 + drawdown),
                high: 100))
            .ToArray();
    }

    private static IndexDrawdownChartPoint ChartPoint(double xRatio, double yRatio)
        => new(
            new DateTime(2026, 1, 1).AddDays((int)Math.Round(xRatio * 100)),
            Drawdown: -yRatio,
            XRatio: xRatio,
            YRatio: yRatio,
            IsClippedToAxis: false);
}
