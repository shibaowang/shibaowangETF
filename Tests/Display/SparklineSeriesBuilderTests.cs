using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class SparklineSeriesBuilderTests
{
    [Fact]
    public void Build_NoData_ReturnsEmptySeries()
    {
        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(
            Array.Empty<AccountReplayStateRecord>(),
            record => record.TotalAssets);

        Assert.Empty(points);
    }

    [Fact]
    public void Build_SingleRealPoint_ReturnsSinglePointWithoutFakeMovement()
    {
        var records = new[]
        {
            new AccountReplayStateRecord { CalculatedAt = "2026-06-14 10:00:00", TotalAssets = 100000 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        SparklinePoint point = Assert.Single(points);
        Assert.Equal(0.5, point.X, 4);
        Assert.Equal(0.5, point.Y, 4);
        Assert.Equal(100000, point.Value, 2);
    }

    [Fact]
    public void Build_RealReplayPoints_ReturnsSortedPointsWithoutChangingValues()
    {
        var records = new[]
        {
            new AccountReplayStateRecord { Id = 2, CalculatedAt = "2026-06-14 10:01:00", TotalPnl = 150 },
            new AccountReplayStateRecord { Id = 1, CalculatedAt = "2026-06-14 10:00:00", TotalPnl = 100 },
            new AccountReplayStateRecord { Id = 3, CalculatedAt = "2026-06-14 10:02:00", TotalPnl = 80 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalPnl);

        Assert.Equal(3, points.Count);
        Assert.Equal(100, points[0].Value, 2);
        Assert.Equal(150, points[1].Value, 2);
        Assert.Equal(80, points[2].Value, 2);
    }

    [Fact]
    public void Build_SnapshotValues_UniformlySpreadsRealChangePoints()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 100500 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 101000 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(3, points.Count);
        Assert.Equal(0, points[0].X, 4);
        Assert.Equal(0.5, points[1].X, 4);
        Assert.Equal(1, points[^1].X, 4);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 0, 1);
            Assert.InRange(point.Y, 0, 1);
        });
        Assert.Equal(SparklineTrend.Up, SparklineSeriesBuilder.GetTrend(points));
    }

    [Fact]
    public void Build_TotalAssetSparkline_UsesTotalAssetsValues()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100000, TotalPnl = 1000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101000, TotalPnl = 900 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 102000, TotalPnl = 800 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(new[] { 100000d, 101000d, 102000d }, points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void Build_HoldingPnlSparkline_UsesTotalPnlValuesNotTotalAssets()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100000, TotalPnl = 1000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101000, TotalPnl = 900 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 102000, TotalPnl = 800 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(
            records,
            record => record.TotalPnl ?? record.TotalUnrealizedPnl);

        Assert.Equal(new[] { 1000d, 900d, 800d }, points.Select(point => point.Value).ToArray());
        Assert.DoesNotContain(100000d, points.Select(point => point.Value));
        Assert.DoesNotContain(101000d, points.Select(point => point.Value));
        Assert.DoesNotContain(102000d, points.Select(point => point.Value));
    }

    [Fact]
    public void Build_HoldingPnlSparkline_FallsBackToTotalUnrealizedPnlWhenTotalPnlMissing()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100000, TotalPnl = null, TotalUnrealizedPnl = 500 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101000, TotalPnl = null, TotalUnrealizedPnl = 600 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 102000, TotalPnl = null, TotalUnrealizedPnl = 550 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(
            records,
            record => record.TotalPnl ?? record.TotalUnrealizedPnl);

        Assert.Equal(new[] { 500d, 600d, 550d }, points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void Build_TotalAssetAndHoldingPnlSparklines_MayShareShapeButUseDifferentValueSeries()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100000, TotalPnl = 1000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 100100, TotalPnl = 1100 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 100200, TotalPnl = 1200 }
        };

        IReadOnlyList<SparklinePoint> assetPoints = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);
        IReadOnlyList<SparklinePoint> pnlPoints = SparklineSeriesBuilder.Build(
            records,
            record => record.TotalPnl ?? record.TotalUnrealizedPnl);

        Assert.Equal(new[] { 100000d, 100100d, 100200d }, assetPoints.Select(point => point.Value).ToArray());
        Assert.Equal(new[] { 1000d, 1100d, 1200d }, pnlPoints.Select(point => point.Value).ToArray());
        Assert.Equal(assetPoints.Select(point => point.X), pnlPoints.Select(point => point.X));
        Assert.Equal(assetPoints.Select(point => point.Y), pnlPoints.Select(point => point.Y));
    }

    [Fact]
    public void Build_TwoRealChangePointsUseFullMiniTrendWidth()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].X, 4);
        Assert.Equal(1, points[^1].X, 4);
    }

    [Fact]
    public void Build_NewRealChangeAddsPointAndUpdatesMiniTrendShape()
    {
        var firstRecords = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101 }
        };
        var secondRecords = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 09:20:00", TotalAssets = 102 }
        };

        IReadOnlyList<SparklinePoint> firstPoints = SparklineSeriesBuilder.Build(firstRecords, record => record.TotalAssets);
        IReadOnlyList<SparklinePoint> secondPoints = SparklineSeriesBuilder.Build(secondRecords, record => record.TotalAssets);

        Assert.Equal(2, firstPoints.Count);
        Assert.Equal(3, secondPoints.Count);
        Assert.Equal(new[] { 100d, 101d, 102d }, secondPoints.Select(point => point.Value).ToArray());
        Assert.Equal(0.5, secondPoints[1].X, 4);
        Assert.Equal(1, secondPoints[^1].X, 4);
    }

    [Fact]
    public void Build_CompressesConsecutiveDuplicateSnapshotValues()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 10:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:01:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 10:02:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 4, CreatedAt = "2026-06-14 10:03:00", TotalAssets = 101 },
            new AccountReplaySnapshotRecord { Id = 5, CreatedAt = "2026-06-14 10:04:00", TotalAssets = 101 },
            new AccountReplaySnapshotRecord { Id = 6, CreatedAt = "2026-06-14 10:05:00", TotalAssets = 102 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(new[] { 100d, 101d, 102d }, points.Select(point => point.Value).ToArray());
        Assert.Equal(new[] { 0d, 0.5d, 1d }, points.Select(point => Math.Round(point.X, 4)).ToArray());
        Assert.Equal(DateTime.Parse("2026-06-14 10:00:00"), points[0].Time);
        Assert.Equal(DateTime.Parse("2026-06-14 10:03:00"), points[1].Time);
        Assert.Equal(DateTime.Parse("2026-06-14 10:05:00"), points[2].Time);
    }

    [Fact]
    public void Build_SameRealChangeInputKeepsSameShapeWithoutAppending()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 09:10:00", TotalAssets = 101 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);
        IReadOnlyList<SparklinePoint> rebuiltPoints = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(new[] { 100d, 101d }, points.Select(point => point.Value).ToArray());
        Assert.Equal(DateTime.Parse("2026-06-14 09:10:00"), points[^1].Time);
        Assert.Equal(1, points[^1].X, 4);
        Assert.Equal(
            points.Select(point => (point.X, point.Y, point.Value, point.Time)).ToArray(),
            rebuiltPoints.Select(point => (point.X, point.Y, point.Value, point.Time)).ToArray());
    }

    [Fact]
    public void Build_PreservesRealReturnToPreviousValue()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 10:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:01:00", TotalAssets = 101 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 10:02:00", TotalAssets = 100 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalAssets);

        Assert.Equal(new[] { 100d, 101d, 100d }, points.Select(point => point.Value).ToArray());
        Assert.Equal(new[] { 0d, 0.5d, 1d }, points.Select(point => Math.Round(point.X, 4)).ToArray());
    }

    [Fact]
    public void Build_AllSameValuesReturnSingleRealPoint()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 10:00:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:01:00", TotalAssets = 100 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 10:02:00", TotalAssets = 100 }
        };

        SparklinePoint point = Assert.Single(SparklineSeriesBuilder.Build(records, record => record.TotalAssets));

        Assert.Equal(100, point.Value, 2);
        Assert.Equal(0.5, point.X, 4);
        Assert.Equal(0.5, point.Y, 4);
    }

    [Fact]
    public void Build_HoldingPnlUsesSelectedPnlValuesWhenCompressing()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 10:00:00", TotalPnl = 20, TotalUnrealizedPnl = 200 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:01:00", TotalPnl = 20, TotalUnrealizedPnl = 210 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-14 10:02:00", TotalPnl = 30, TotalUnrealizedPnl = 210 },
            new AccountReplaySnapshotRecord { Id = 4, CreatedAt = "2026-06-14 10:03:00", TotalPnl = null, TotalUnrealizedPnl = 40 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(
            records,
            record => record.TotalPnl ?? record.TotalUnrealizedPnl);

        Assert.Equal(new[] { 20d, 30d, 40d }, points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void GetTrend_DownTrend_ReturnsDown()
    {
        var records = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 10:00:00", TotalPnl = 200 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:01:00", TotalPnl = 120 }
        };

        IReadOnlyList<SparklinePoint> points = SparklineSeriesBuilder.Build(records, record => record.TotalPnl);

        Assert.Equal(SparklineTrend.Down, SparklineSeriesBuilder.GetTrend(points));
    }
}
