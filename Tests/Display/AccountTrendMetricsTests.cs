using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class AccountTrendMetricsTests
{
    [Fact]
    public void CalculateDailyPnl_IgnoresMarketDayDailyPnlAndUsesNaturalDaySnapshots()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:30:00", TotalAssets = 131076.48 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 10:30:00", TotalAssets = 131787.72 }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            9999,
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

    [Fact]
    public void CalculateDailyPnl_BeijingNaturalDay_UsesHalfOpenBoundary()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 0, CreatedAt = "2026-06-13 23:59:59", TotalAssets = 90000 },
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 00:00:00", TotalAssets = 100000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 23:59:59", TotalAssets = 101234 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-15 00:00:00", TotalAssets = 200000 }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            realDailyPnl: 999999,
            new DateTime(2026, 6, 14, 21, 30, 0));

        Assert.Equal(1234, metric.Amount!.Value, 2);
        Assert.Equal(0.01234, metric.Ratio!.Value, 5);
    }

    [Fact]
    public void CalculateDailyPnl_UsQuoteAfterMidnight_BelongsToNewBeijingDay()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 21:30:00", TotalAssets = 100000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 23:59:59", TotalAssets = 100500 },
            new AccountReplaySnapshotRecord { Id = 3, CreatedAt = "2026-06-15 00:00:00", TotalAssets = 100800 },
            new AccountReplaySnapshotRecord { Id = 4, CreatedAt = "2026-06-15 00:30:00", TotalAssets = 101300 }
        };

        DailyPnlMetric previousDay = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            realDailyPnl: null,
            new DateTime(2026, 6, 14, 23, 59, 59));
        DailyPnlMetric nextDay = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            realDailyPnl: null,
            new DateTime(2026, 6, 15, 0, 30, 0));

        Assert.Equal(500, previousDay.Amount!.Value, 2);
        Assert.Equal(500, nextDay.Amount!.Value, 2);
    }

    [Fact]
    public void CalculateDailyPnl_AsharePostCloseSnapshot_RemainsInSameNaturalDay()
    {
        var snapshots = new[]
        {
            new AccountReplaySnapshotRecord { Id = 1, CreatedAt = "2026-06-14 09:30:00", TotalAssets = 100000 },
            new AccountReplaySnapshotRecord { Id = 2, CreatedAt = "2026-06-14 15:03:39", TotalAssets = 100880 }
        };

        DailyPnlMetric metric = AccountTrendMetrics.CalculateDailyPnl(
            snapshots,
            Array.Empty<TradeLogRecord>(),
            realDailyPnl: null,
            new DateTime(2026, 6, 14, 15, 3, 39));

        Assert.Equal(880, metric.Amount!.Value, 2);
    }

    [Theory]
    [InlineData(1, FinancialValueTone.Positive)]
    [InlineData(-1, FinancialValueTone.Negative)]
    [InlineData(0, FinancialValueTone.Neutral)]
    public void GetTone_MapsSignToDisplayTone(double value, FinancialValueTone expected)
    {
        Assert.Equal(expected, AccountTrendMetrics.GetTone(value));
    }

    [Fact]
    public void CalculateExternalFundingCashFlow_UsesBeijingHalfOpenDay()
    {
        var records = new[]
        {
            Funding("2026-06-13 23:59:59", "入金", 100),
            Funding("2026-06-14 00:00:00", "入金", 1000),
            Funding("2026-06-14 23:59:59", "出金", 200),
            Funding("2026-06-15 00:00:00", "入金", 3000)
        };

        double flow = AccountTrendMetrics.CalculateExternalFundingCashFlow(records, new DateTime(2026, 6, 14));

        Assert.Equal(800, flow, 2);
    }

    private static TradeLogRecord Funding(string time, string action, double amount)
        => new()
        {
            Time = time,
            StrategyCode = "CASH",
            Action = action,
            Amount = amount,
            NetCashImpact = action == "出金" ? -amount : amount
        };
}
