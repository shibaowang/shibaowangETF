using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class ChartCache
{
    private readonly Dictionary<string, ChartIntradayCacheEntry> _intraday = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChartKLineCacheEntry> _dailyKLines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SecurityChartSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public void SaveIntraday(string strategyCode, IReadOnlyList<IntradayPoint> points, ChartDataStatus status, DateTimeOffset updatedAt)
    {
        string key = ChartSubscriptionService.NormalizeKey(strategyCode);
        if (key.Length == 0)
        {
            return;
        }

        IntradayPoint[] pointArray = points.ToArray();
        lock (_intraday)
        {
            if (_intraday.TryGetValue(key, out ChartIntradayCacheEntry? existing)
                && existing.Points.Count > 0
                && (pointArray.Length == 0 || !status.IsReady))
            {
                _intraday[key] = existing with
                {
                    Status = new ChartDataStatus(
                        true,
                        "真实分时缓存",
                        true,
                        status.IsRateLimited,
                        status.IsCircuitOpen),
                    UpdatedAt = updatedAt
                };
                return;
            }

            if (pointArray.Length > 0 && !status.IsReady)
            {
                status = new ChartDataStatus(
                    true,
                    "真实分时缓存",
                    true,
                    status.IsRateLimited,
                    status.IsCircuitOpen);
            }

            _intraday[key] = new ChartIntradayCacheEntry(pointArray, status, updatedAt);
        }
    }

    public ChartIntradayCacheEntry? GetIntraday(string strategyCode)
    {
        string key = ChartSubscriptionService.NormalizeKey(strategyCode);
        lock (_intraday)
        {
            return _intraday.TryGetValue(key, out ChartIntradayCacheEntry? entry) ? entry : null;
        }
    }

    public void SaveDailyKLines(string strategyCode, IReadOnlyList<KLinePoint> points, ChartDataStatus status, DateTimeOffset updatedAt)
    {
        string key = ChartSubscriptionService.NormalizeKey(strategyCode);
        if (key.Length == 0)
        {
            return;
        }

        KLinePoint[] pointArray = points.ToArray();
        lock (_dailyKLines)
        {
            if (_dailyKLines.TryGetValue(key, out ChartKLineCacheEntry? existing)
                && existing.Points.Count > 0
                && (pointArray.Length == 0 || !status.IsReady))
            {
                _dailyKLines[key] = existing with
                {
                    Status = new ChartDataStatus(
                        true,
                        "使用最近真实日K缓存",
                        true,
                        status.IsRateLimited,
                        status.IsCircuitOpen),
                    UpdatedAt = updatedAt
                };
                return;
            }

            if (pointArray.Length > 0 && !status.IsReady)
            {
                status = new ChartDataStatus(
                    true,
                    "使用最近真实日K缓存",
                    true,
                    status.IsRateLimited,
                    status.IsCircuitOpen);
            }

            _dailyKLines[key] = new ChartKLineCacheEntry(pointArray, status, updatedAt);
        }
    }

    public ChartKLineCacheEntry? GetDailyKLines(string strategyCode)
    {
        string key = ChartSubscriptionService.NormalizeKey(strategyCode);
        lock (_dailyKLines)
        {
            return _dailyKLines.TryGetValue(key, out ChartKLineCacheEntry? entry) ? entry : null;
        }
    }

    public void SaveSnapshot(SecurityChartSnapshot snapshot)
    {
        string key = SnapshotKey(snapshot.Security.StrategyCode, snapshot.Period, snapshot.SubPanel);
        lock (_snapshots)
        {
            _snapshots[key] = snapshot;
        }
    }

    public SecurityChartSnapshot? GetSnapshot(string strategyCode, SecurityChartPeriod period, SecurityChartSubPanel subPanel)
    {
        string key = SnapshotKey(strategyCode, period, subPanel);
        lock (_snapshots)
        {
            return _snapshots.TryGetValue(key, out SecurityChartSnapshot? snapshot) ? snapshot : null;
        }
    }

    private static string SnapshotKey(string strategyCode, SecurityChartPeriod period, SecurityChartSubPanel subPanel)
        => ChartSubscriptionService.NormalizeKey(strategyCode) + "|" + period + "|" + subPanel;
}

public sealed record ChartIntradayCacheEntry(
    IReadOnlyList<IntradayPoint> Points,
    ChartDataStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record ChartKLineCacheEntry(
    IReadOnlyList<KLinePoint> Points,
    ChartDataStatus Status,
    DateTimeOffset UpdatedAt);
