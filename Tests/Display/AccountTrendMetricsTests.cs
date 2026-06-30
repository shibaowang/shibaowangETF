using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class AccountTrendMetricsTests
{
    [Fact]
    public void CalculateDailyPnl_RealDailyPnl_UsesStartAssetsForPercent()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:30:00", TotalAssets = 131076.48 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:30:00", TotalAssets = 131787.72 }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            711.24,
            new DateTime(2026, 6, 14));

        Assert.Equal(711.24, metric.Amount!.Value, 2);
        Assert.Equal(0.005426, metric.Ratio!.Value, 6);
    }

    [Fact]
    public void CalculateDailyPnl_TotalAssetsFallback_ExcludesFundingCashFlow()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:30:00", TotalAssets = 100000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:30:00", TotalAssets = 106000 }
        };
        var tradeLogs = new[]
        {
            new TradeLogRecord
            {
                Time = "2026-06-14 10:00:00",
                StrategyCode = "CASH",
                Action = "入金",
                Amount = 5000,
                NetCashImpact = 5000
            }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            tradeLogs,
            realDailyPnl: null,
            new DateTime(2026, 6, 14));

        Assert.Equal(1000, metric.Amount!.Value, 2);
        Assert.Equal(0.01, metric.Ratio!.Value, 4);
    }

    [Fact]
    public void CalculateDailyPnl_InsufficientData_ReturnsEmpty()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:30:00", TotalAssets = 100000 }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            realDailyPnl: null,
            new DateTime(2026, 6, 14));

        Assert.Null(metric.Amount);
        Assert.Null(metric.Ratio);
    }

    [Theory]
    [InlineData(1, FinancialValueTone.Positive)]
    [InlineData(-1, FinancialValueTone.Negative)]
    [InlineData(0, FinancialValueTone.Neutral)]
    public void GetTone_MapsSignToDisplayTone(double value, FinancialValueTone expected)
    {
        Assert.Equal(expected, AccountTrendMetrics.GetTone(value));
    }
}
