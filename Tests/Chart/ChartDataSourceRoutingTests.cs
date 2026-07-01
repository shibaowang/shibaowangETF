using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class ChartDataSourceRoutingTests
{
    [Fact]
    public async Task EtfIntraday_UsesTencentMinuteQuery()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(security, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(0, client.GetIntradayRequestCount(security.EastMoneySecId));
        Assert.Equal(1, client.GetTencentIntradayRequestCount("sz159941"));
        ChartIntradayCacheEntry? entry = cache.GetIntraday("159941");
        Assert.NotNull(entry);
        Assert.True(entry!.Status.IsReady);
        Assert.NotEmpty(entry.Points);
    }

    [Fact]
    public async Task IndexIntraday_StillUsesEastMoney()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 22:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(security, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, client.GetIntradayRequestCount(security.EastMoneySecId));
        Assert.True(cache.GetIntraday("100.NDX100")?.Status.IsReady);
    }

    [Fact]
    public async Task EtfDailyWithoutDailyLike_UsesTencentQfqDaily()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(0, client.GetHistoryRequestCount(security.EastMoneySecId));
        Assert.Equal(1, client.GetTencentHistoryRequestCount("sz159941"));
        ChartKLineCacheEntry? entry = cache.GetDailyKLines("159941");
        Assert.NotNull(entry);
        Assert.True(entry!.Status.IsReady);
        Assert.Equal(220, entry.Points.Count);
    }

    [Fact]
    public async Task EtfDailyWithDailyLikeCache_SkipsTencentRequestAndKeepsDisplayReady()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "纳指科技ETF景顺");
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        MarketQuoteRecord history = new()
        {
            Symbol = "159509",
            MarketType = "ETF",
            Source = MarketSources.EastMoneyHistory,
            RawPayload = DailyHistoryPayload(220),
            ReceivedAt = "2026-06-24 09:30:00"
        };

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { history }, CancellationToken.None);

        Assert.Equal(0, client.GetHistoryRequestCount(security.EastMoneySecId));
        Assert.Equal(0, client.GetTencentHistoryRequestCount("sz159509"));
        SecurityChartSnapshot? snapshot = cache.GetSnapshot("159509", SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.MainStatus.IsReady);
        Assert.Equal(180, snapshot.KLines.Count);
    }

    [Fact]
    public async Task IndexDaily_StillUsesEastMoneyHistory()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳斯达克科技指数");
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, client.GetHistoryRequestCount(security.EastMoneySecId));
        Assert.False(client.GetLastHistoryIsEtf(security.EastMoneySecId));
        Assert.True(cache.GetDailyKLines("251.NDXTMC")?.Status.IsReady);
    }

    [Fact]
    public async Task WeeklyAndMonthly_DoNotRequestNetworkAndUseDailyLikeHistory()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        subscriptions.Subscribe(security, SecurityChartPeriod.Weekly, SecurityChartSubPanel.Volume);
        MarketQuoteRecord history = new()
        {
            Symbol = "159941",
            MarketType = "ETF",
            Source = MarketSources.TencentHistory,
            RawPayload = DailyHistoryPayload(220),
            ReceivedAt = "2026-06-24 09:30:00"
        };

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { history }, CancellationToken.None);

        Assert.Equal(0, client.GetHistoryRequestCount(security.EastMoneySecId));
        Assert.Equal(0, client.GetTencentHistoryRequestCount("sz159941"));
        SecurityChartSnapshot? snapshot = cache.GetSnapshot("159941", SecurityChartPeriod.Weekly, SecurityChartSubPanel.Volume);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.MainStatus.IsReady);
        Assert.NotEmpty(snapshot.KLines);
    }

    [Fact]
    public async Task EtfIntraday_AfterCloseMissingCacheRequestsTencentMinuteCatchUp()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 15:08:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(security, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, client.GetTencentIntradayRequestCount("sz159941"));
        SecurityChartSnapshot? snapshot = cache.GetSnapshot("159941", SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.MainStatus.IsReady);
    }

    [Fact]
    public async Task EtfIntraday_AfterClosePartialCacheStillRateLimitsRepeatedCatchUp()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-24 17:13:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => now);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");
        cache.SaveIntraday(
            security.StrategyCode,
            new[] { Intraday(new DateTime(2026, 6, 24, 14, 20, 0), 1.60) },
            new ChartDataStatus(true, "real intraday cache", true),
            DateTimeOffset.Parse("2026-06-24 14:20:00", CultureInfo.InvariantCulture));
        subscriptions.Subscribe(security, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        now = now.AddSeconds(5);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, client.GetTencentIntradayRequestCount("sz159941"));
    }

    [Fact]
    public async Task EtfIntraday_AfterCloseCompleteCacheDoesNotRequestTencentMinute()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 15:08:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");
        cache.SaveIntraday(
            security.StrategyCode,
            new[]
            {
                Intraday(new DateTime(2026, 6, 24, 14, 57, 0), 1.60),
                Intraday(new DateTime(2026, 6, 24, 14, 58, 0), 1.61)
            },
            new ChartDataStatus(true, "real intraday cache", true),
            DateTimeOffset.Parse("2026-06-24 14:58:00", CultureInfo.InvariantCulture));
        subscriptions.Subscribe(security, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(0, client.GetTencentIntradayRequestCount("sz159941"));
    }

    [Fact]
    public async Task BackgroundEtfIntraday_RequestsAtMostOneSymbolPerRefresh()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo first = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        ChartSecurityInfo second = ChartDataService.CreateSecurityInfo("513300", "纳斯达克ETF华夏");

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { first, second },
            CancellationToken.None);

        int totalRequests = client.GetTencentIntradayRequestCount("sz159941")
                            + client.GetTencentIntradayRequestCount("sh513300");
        Assert.Equal(1, totalRequests);
    }

    [Fact]
    public async Task ActiveChartSymbol_IsNotRequestedAgainByBackgroundIntradayRefresh()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new CountingChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture));
        ChartSecurityInfo active = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        ChartSecurityInfo background = ChartDataService.CreateSecurityInfo("513300", "纳斯达克ETF华夏");
        subscriptions.Subscribe(active, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { active, background },
            CancellationToken.None);

        Assert.Equal(1, client.GetTencentIntradayRequestCount("sz159941"));
    }

    private sealed class CountingChartMarketDataClient : IChartMarketDataClient
    {
        private readonly Dictionary<string, int> _intradayCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _historyCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _tencentIntradayCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _tencentHistoryCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _lastHistoryIsEtf = new(StringComparer.OrdinalIgnoreCase);

        public int GetIntradayRequestCount(string secId)
            => _intradayCounts.TryGetValue(secId, out int count) ? count : 0;

        public int GetHistoryRequestCount(string secId)
            => _historyCounts.TryGetValue(secId, out int count) ? count : 0;

        public int GetTencentIntradayRequestCount(string tencentCode)
            => _tencentIntradayCounts.TryGetValue(tencentCode, out int count) ? count : 0;

        public int GetTencentHistoryRequestCount(string tencentCode)
            => _tencentHistoryCounts.TryGetValue(tencentCode, out int count) ? count : 0;

        public bool GetLastHistoryIsEtf(string secId)
            => _lastHistoryIsEtf.TryGetValue(secId, out bool isEtf) && isEtf;

        public Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
        {
            _intradayCounts[secId] = GetIntradayRequestCount(secId) + 1;
            return Task.FromResult(new EastMoneyIntradayFetchResult(
                secId,
                "{}",
                new[]
                {
                    new IntradayPoint
                    {
                        Time = new DateTime(2026, 6, 24, 10, 0, 0),
                        Price = 100,
                        Volume = 1000
                    }
                },
                DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture)));
        }

        public Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
        {
            _tencentIntradayCounts[tencentCode] = GetTencentIntradayRequestCount(tencentCode) + 1;
            return Task.FromResult(new EastMoneyIntradayFetchResult(
                tencentCode,
                "{}",
                new[]
                {
                    new IntradayPoint
                    {
                        Time = new DateTime(2026, 6, 24, 10, 0, 0),
                        Price = 100,
                        Volume = 1000
                    }
                },
                DateTimeOffset.Parse("2026-06-24 10:00:00", CultureInfo.InvariantCulture)));
        }

        public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
            string secId,
            bool isEtf,
            bool preferDaily,
            CancellationToken cancellationToken)
        {
            _historyCounts[secId] = GetHistoryRequestCount(secId) + 1;
            _lastHistoryIsEtf[secId] = isEtf;
            string payload = DailyHistoryPayload(220);
            return Task.FromResult(new EastMoneyHistoryFetchResult
            {
                SecId = secId,
                Fqt = isEtf ? 1 : 0,
                Klt = 101,
                RawPayload = payload,
                High = 120,
                PointCount = 220
            });
        }

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken)
        {
            _tencentHistoryCounts[tencentCode] = GetTencentHistoryRequestCount(tencentCode) + 1;
            string payload = DailyHistoryPayload(220);
            return Task.FromResult(new EastMoneyHistoryFetchResult
            {
                SecId = tencentCode,
                Fqt = 1,
                Klt = 101,
                RawPayload = payload,
                High = 120,
                PointCount = 220
            });
        }

        public void Dispose()
        {
        }
    }

    private static string DailyHistoryPayload(int count)
    {
        string[] lines = Enumerable.Range(0, count)
            .Select(index =>
            {
                DateTime date = new DateTime(2025, 1, 1).AddDays(index);
                double close = 100 + index * 0.01;
                return $"\"{date:yyyy-MM-dd},{close:F2},{close:F2},{close + 1:F2},{close - 1:F2},1000,2000\"";
            })
            .ToArray();
        return "{\"data\":{\"klines\":[" + string.Join(",", lines) + "]}}";
    }

    private static IntradayPoint Intraday(DateTime time, double price)
        => new()
        {
            Time = time,
            Price = price,
            Volume = 100
        };
}
