using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class AccountTrendMetrics
{
    private const double Epsilon = 0.000001;

    public static DailyPnlMetric CalculateDailyPnl(
        IReadOnlyList<AccountReplaySnapshotRecord> snapshots,
        IEnumerable<TradeLogRecord> tradeLogs,
        double? realDailyPnl,
        DateTime today)
    {
        AccountReplaySnapshotRecord? latest = LatestSnapshot(snapshots);
        AccountReplaySnapshotRecord? firstToday = FirstSnapshotOfDay(snapshots, today.Date);

        if (HasFiniteValue(realDailyPnl))
        {
            double amount = realDailyPnl!.Value;
            return new DailyPnlMetric(amount, CalculateRatio(amount, firstToday?.TotalAssets, latest?.TotalAssets));
        }

        if (latest is null || firstToday is null || latest.Id == firstToday.Id)
        {
            return DailyPnlMetric.Empty;
        }

        if (HasFiniteValue(latest.TotalPnl) && HasFiniteValue(firstToday.TotalPnl))
        {
            double amount = latest.TotalPnl!.Value - firstToday.TotalPnl!.Value;
            return new DailyPnlMetric(amount, CalculateRatio(amount, firstToday.TotalAssets, latest.TotalAssets));
        }

        if (HasFiniteValue(latest.TotalAssets) && HasFiniteValue(firstToday.TotalAssets))
        {
            double externalCashFlow = CalculateExternalFundingCashFlow(tradeLogs, today.Date);
            double amount = latest.TotalAssets!.Value - firstToday.TotalAssets!.Value - externalCashFlow;
            return new DailyPnlMetric(amount, CalculateRatio(amount, firstToday.TotalAssets, latest.TotalAssets));
        }

        return DailyPnlMetric.Empty;
    }

    public static FinancialValueTone GetTone(double? value)
    {
        if (!HasFiniteValue(value))
        {
            return FinancialValueTone.Empty;
        }

        double amount = value.GetValueOrDefault();
        if (amount > Epsilon)
        {
            return FinancialValueTone.Positive;
        }

        return amount < -Epsilon ? FinancialValueTone.Negative : FinancialValueTone.Neutral;
    }

    public static double CalculateExternalFundingCashFlow(IEnumerable<TradeLogRecord> tradeLogs, DateTime day)
    {
        double total = 0;
        foreach (TradeLogRecord record in tradeLogs)
        {
            if (!DateTime.TryParse(record.Time, out DateTime time) || time.Date != day)
            {
                continue;
            }

            string action = record.Action?.Trim() ?? string.Empty;
            if (action == "入金")
            {
                total += Math.Abs(record.NetCashImpact) > Epsilon
                    ? record.NetCashImpact
                    : record.Amount - record.Fee;
            }
            else if (action == "出金")
            {
                total += Math.Abs(record.NetCashImpact) > Epsilon
                    ? record.NetCashImpact
                    : -(record.Amount + record.Fee);
            }
        }

        return total;
    }

    private static AccountReplaySnapshotRecord? LatestSnapshot(IEnumerable<AccountReplaySnapshotRecord> snapshots)
        => snapshots
            .Where(snapshot => HasFiniteValue(snapshot.TotalAssets)
                               || HasFiniteValue(snapshot.TotalPnl)
                               || HasFiniteValue(snapshot.TotalUnrealizedPnl))
            .OrderBy(snapshot => ParseSortTime(snapshot.CreatedAt))
            .ThenBy(snapshot => snapshot.Id)
            .LastOrDefault();

    private static AccountReplaySnapshotRecord? FirstSnapshotOfDay(IEnumerable<AccountReplaySnapshotRecord> snapshots, DateTime day)
        => snapshots
            .Where(snapshot => ParseSortTime(snapshot.CreatedAt).Date == day)
            .OrderBy(snapshot => ParseSortTime(snapshot.CreatedAt))
            .ThenBy(snapshot => snapshot.Id)
            .FirstOrDefault();

    private static double? CalculateRatio(double amount, double? firstTotalAssets, double? latestTotalAssets)
    {
        double? denominator = HasFiniteValue(firstTotalAssets) && firstTotalAssets!.Value > Epsilon
            ? firstTotalAssets.Value
            : HasFiniteValue(latestTotalAssets) && latestTotalAssets!.Value - amount > Epsilon
                ? latestTotalAssets.Value - amount
                : null;

        return denominator.HasValue ? amount / denominator.Value : null;
    }

    private static DateTime ParseSortTime(string? value)
        => DateTime.TryParse(value, out DateTime parsed) ? parsed : DateTime.MinValue;

    private static bool HasFiniteValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
}

public sealed record DailyPnlMetric(double? Amount, double? Ratio)
{
    public static DailyPnlMetric Empty { get; } = new(null, null);
}

public enum FinancialValueTone
{
    Empty,
    Neutral,
    Positive,
    Negative
}
