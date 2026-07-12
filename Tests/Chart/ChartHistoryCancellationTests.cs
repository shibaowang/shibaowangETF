using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class ChartHistoryCancellationTests
{
    private static readonly DateTime LatestTradingDate = new(2026, 7, 10);
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void WindowLifetimes_LinkToApplicationTokenAndRemainIsolated()
    {
        using var application = new CancellationTokenSource();
        using var first = new ChartWindowLifetime(application.Token);
        using var second = new ChartWindowLifetime(application.Token);

        first.Cancel();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
        Assert.False(application.IsCancellationRequested);

        application.Cancel();

        Assert.True(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void SubscriptionPeriodUpdate_PreservesWindowLifetimeToken()
    {
        using var lifetime = new ChartWindowLifetime(CancellationToken.None);
        var subscriptions = new ChartSubscriptionService();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");

        subscriptions.Subscribe(
            security,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            lifetime.Token);
        subscriptions.UpdatePeriod(security.StrategyCode, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Macd);

        ChartSubscription updated = Assert.Single(subscriptions.ActiveSubscriptions);
        Assert.Equal(SecurityChartPeriod.Monthly, updated.Period);
        Assert.Equal(SecurityChartSubPanel.Macd, updated.SubPanel);
        Assert.Equal(lifetime.Token, updated.LifetimeToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingWindowDuringDeepHistory_PreservesCacheAndWritesNothing(bool isIndex)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CancellableHistoryClient();
        client.DeepHandler = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        };
        using var lifetime = new ChartWindowLifetime(CancellationToken.None);
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        ChartSecurityInfo security = isIndex
            ? ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "Index")
            : ChartDataService.CreateSecurityInfo("159941", "ETF");
        KLinePoint[] existing = BusinessDaysEnding(320);
        DateTimeOffset originalUpdatedAt = Now.AddDays(-1);
        cache.SaveDailyKLines(
            security.StrategyCode,
            existing,
            new ChartDataStatus(true, "existing cache", true),
            originalUpdatedAt);
        int historyWrites = 0;
        int checkpointWrites = 0;
        var runtimeLogs = new List<(string Level, string Message)>();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            historyCacheSaver: (_, _, _, _, _) => historyWrites++,
            runtimeLog: (level, _, message) => runtimeLogs.Add((level, message)),
            nowProvider: () => Now,
            historyDepthCheckpointWriter: (_, _) => checkpointWrites++);
        int snapshots = 0;
        coordinator.SnapshotUpdated += (_, _) => snapshots++;
        subscriptions.Subscribe(
            security,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            lifetime.Token);

        Task refresh = coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Cancel();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        ChartKLineCacheEntry entry = Assert.IsType<ChartKLineCacheEntry>(cache.GetDailyKLines(security.StrategyCode));
        Assert.Equal(320, entry.Points.Count);
        Assert.Equal("existing cache", entry.Status.Message);
        Assert.Equal(originalUpdatedAt, entry.UpdatedAt);
        Assert.Equal(0, historyWrites);
        Assert.Equal(0, checkpointWrites);
        Assert.Equal(0, snapshots);
        Assert.DoesNotContain(runtimeLogs, log => log.Level is "WARN" or "ERROR");
        Assert.True(client.LastRequestToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelledAttempt_ReopeningSameSymbolCanRetryAndComplete()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CancellableHistoryClient();
        client.DeepHandler = async (_, token) =>
        {
            if (client.TotalDeepRequests == 1)
            {
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return Result("sz159941", BusinessDaysEnding(3000));
        };
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");
        cache.SaveDailyKLines(
            security.StrategyCode,
            BusinessDaysEnding(320),
            new ChartDataStatus(true, "existing cache", true),
            Now.AddDays(-1));
        int historyWrites = 0;
        int checkpointWrites = 0;
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            historyCacheSaver: (_, _, _, _, _) => historyWrites++,
            nowProvider: () => Now,
            historyDepthCheckpointWriter: (_, _) => checkpointWrites++);

        using (var firstLifetime = new ChartWindowLifetime(CancellationToken.None))
        {
            subscriptions.Subscribe(
                security,
                SecurityChartPeriod.Monthly,
                SecurityChartSubPanel.Volume,
                firstLifetime.Token);
            Task firstRefresh = coordinator.RefreshAsync(
                Array.Empty<MarketQuoteRecord>(),
                Array.Empty<MarketQuoteRecord>(),
                CancellationToken.None);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstLifetime.Cancel();
            await firstRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            subscriptions.Unsubscribe(security.StrategyCode);
        }

        using var reopenedLifetime = new ChartWindowLifetime(CancellationToken.None);
        subscriptions.Subscribe(
            security,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            reopenedLifetime.Token);
        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            CancellationToken.None);

        Assert.Equal(2, client.TotalDeepRequests);
        Assert.Equal(3000, cache.GetDailyKLines(security.StrategyCode)?.Points.Count);
        Assert.Equal(1, historyWrites);
        Assert.Equal(1, checkpointWrites);
    }

    [Fact]
    public async Task CancellingOneSymbol_DoesNotCancelOtherWindowOrPublishClosedSnapshot()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CancellableHistoryClient();
        client.DeepHandler = async (code, token) =>
        {
            if (code.Contains("159941", StringComparison.OrdinalIgnoreCase))
            {
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return Result(code, BusinessDaysEnding(3000));
        };
        using var firstLifetime = new ChartWindowLifetime(CancellationToken.None);
        using var secondLifetime = new ChartWindowLifetime(CancellationToken.None);
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        ChartSecurityInfo first = ChartDataService.CreateSecurityInfo("159941", "ETF1");
        ChartSecurityInfo second = ChartDataService.CreateSecurityInfo("513100", "ETF2");
        cache.SaveDailyKLines(first.StrategyCode, BusinessDaysEnding(320), new ChartDataStatus(true, "first", true), Now);
        cache.SaveDailyKLines(second.StrategyCode, BusinessDaysEnding(320), new ChartDataStatus(true, "second", true), Now);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, client, nowProvider: () => Now);
        var published = new List<string>();
        coordinator.SnapshotUpdated += (_, snapshot) => published.Add(snapshot.Security.StrategyCode);
        subscriptions.Subscribe(first, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume, firstLifetime.Token);
        subscriptions.Subscribe(second, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume, secondLifetime.Token);

        Task refresh = coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        firstLifetime.Cancel();
        subscriptions.Unsubscribe(first.StrategyCode);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, client.GetRequestCount("sz159941"));
        Assert.Equal(1, client.GetRequestCount("sh513100"));
        Assert.DoesNotContain(first.StrategyCode, published);
        Assert.Contains(second.StrategyCode, published);
        Assert.False(secondLifetime.Token.IsCancellationRequested);
        Assert.Equal(3000, cache.GetDailyKLines(second.StrategyCode)?.Points.Count);
    }

    [Fact]
    public async Task ApplicationCancellation_CancelsAllWindowTokensAndStopsRefresh()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CancellableHistoryClient
        {
            DeepHandler = async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
        };
        using var application = new CancellationTokenSource();
        using var firstLifetime = new ChartWindowLifetime(application.Token);
        using var secondLifetime = new ChartWindowLifetime(application.Token);
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        ChartSecurityInfo first = ChartDataService.CreateSecurityInfo("159941", "ETF1");
        ChartSecurityInfo second = ChartDataService.CreateSecurityInfo("513100", "ETF2");
        cache.SaveDailyKLines(first.StrategyCode, BusinessDaysEnding(320), new ChartDataStatus(true, "first", true), Now);
        cache.SaveDailyKLines(second.StrategyCode, BusinessDaysEnding(320), new ChartDataStatus(true, "second", true), Now);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, client, nowProvider: () => Now);
        subscriptions.Subscribe(first, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume, firstLifetime.Token);
        subscriptions.Subscribe(second, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume, secondLifetime.Token);

        Task refresh = coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            application.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        application.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refresh);
        Assert.True(firstLifetime.Token.IsCancellationRequested);
        Assert.True(secondLifetime.Token.IsCancellationRequested);
        Assert.Equal(1, client.TotalDeepRequests);
    }

    [Fact]
    public async Task TemporaryFailure_DoesNotWriteHistoryDepthCheckpoint()
    {
        var client = new CancellableHistoryClient
        {
            DeepHandler = (_, _) => Task.FromException<EastMoneyHistoryFetchResult>(new InvalidOperationException("temporary"))
        };
        using var lifetime = new ChartWindowLifetime(CancellationToken.None);
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "ETF");
        cache.SaveDailyKLines(security.StrategyCode, BusinessDaysEnding(320), new ChartDataStatus(true, "old", true), Now);
        int checkpoints = 0;
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now,
            historyDepthCheckpointWriter: (_, _) => checkpoints++);
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume, lifetime.Token);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            CancellationToken.None);
        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            CancellationToken.None);

        Assert.Equal(1, client.TotalDeepRequests);
        Assert.Equal(0, checkpoints);
    }

    [Fact]
    public void Scheduler_CancelledRequestReleasesOnlySymbolReservation()
    {
        DateTimeOffset now = Now;
        var scheduler = new GlobalMarketRequestScheduler(new Random(0));
        scheduler.BeginTick(now);
        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "159941", now, out _));

        scheduler.ReleaseCancelledRequest(MarketRequestKind.EtfDailyKLine, "159941", now);
        DateTimeOffset nextTick = now.AddSeconds(31);
        scheduler.BeginTick(nextTick);

        Assert.True(scheduler.TryAcquire(MarketRequestKind.EtfDailyKLine, "159941", nextTick, out _));
    }

    [Fact]
    public void WindowManagerAndPagination_UseLinkedCancellationWithoutChangingBusinessRules()
    {
        string manager = ReadRepositoryFile(Path.Combine("Views", "ChartWindowManager.cs"));
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string coordinator = ReadRepositoryFile(Path.Combine("Core", "Services", "ChartDataRefreshCoordinator.cs"));
        string client = ReadRepositoryFile(Path.Combine("Infrastructure", "Market", "MarketDataClient.cs"));

        Assert.Contains("new ChartWindowLifetime(_applicationToken)", manager, StringComparison.Ordinal);
        Assert.Contains("_marketRefreshCts.Token", main, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource(cancellationToken, subscription.LifetimeToken)", coordinator, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", coordinator, StringComparison.Ordinal);
        Assert.Contains("_scheduler?.ReleaseCancelledRequest", coordinator, StringComparison.Ordinal);
        Assert.Contains("DeepHistoryAttemptState.Cancelled", coordinator, StringComparison.Ordinal);
        Assert.Contains("DeepHistoryAttemptState.TemporaryFailure", coordinator, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(delay, cancellationToken)", client, StringComparison.Ordinal);
        Assert.Contains("SendAsync(request, cancellationToken)", client, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", client, StringComparison.Ordinal);

        int cancel = manager.IndexOf("registration.Lifetime.Cancel();", StringComparison.Ordinal);
        int unsubscribe = manager.IndexOf("_subscriptions.Unsubscribe(key);", cancel, StringComparison.Ordinal);
        int remove = manager.IndexOf("_windows.Remove(key);", unsubscribe, StringComparison.Ordinal);
        int dispose = manager.IndexOf("registration.Lifetime.Dispose();", remove, StringComparison.Ordinal);
        int detach = manager.IndexOf("registration.Window.PeriodChanged -=", dispose, StringComparison.Ordinal);
        Assert.True(cancel >= 0 && cancel < unsubscribe && unsubscribe < remove && remove < dispose && dispose < detach);

        Assert.DoesNotContain("SaveTradeLog", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderDraft", coordinator, StringComparison.Ordinal);
    }

    private static KLinePoint[] BusinessDaysEnding(int count)
    {
        var dates = new List<DateTime>(count);
        DateTime date = LatestTradingDate;
        while (dates.Count < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(date);
            }

            date = date.AddDays(-1);
        }

        dates.Reverse();
        return dates.Select((item, index) => new KLinePoint
        {
            Date = item,
            Open = 100 + index * 0.01,
            High = 101 + index * 0.01,
            Low = 99 + index * 0.01,
            Close = 100.5 + index * 0.01,
            Volume = 1000 + index,
            Amount = 2000 + index
        }).ToArray();
    }

    private static EastMoneyHistoryFetchResult Result(string symbol, IReadOnlyList<KLinePoint> points)
    {
        string payload = DailyPayload(points);
        return new EastMoneyHistoryFetchResult
        {
            SecId = symbol,
            Fqt = 1,
            Klt = 101,
            RawPayload = payload,
            PointCount = points.Count,
            IsSourceExhausted = false
        };
    }

    private static string DailyPayload(IEnumerable<KLinePoint> points)
    {
        string[] lines = points.Select(point =>
            $"\"{point.Date:yyyy-MM-dd},{point.Open.ToString("F4", CultureInfo.InvariantCulture)},{point.Close.ToString("F4", CultureInfo.InvariantCulture)},{point.High.ToString("F4", CultureInfo.InvariantCulture)},{point.Low.ToString("F4", CultureInfo.InvariantCulture)},{(point.Volume ?? 0).ToString("F0", CultureInfo.InvariantCulture)},{(point.Amount ?? 0).ToString("F0", CultureInfo.InvariantCulture)}\"").ToArray();
        return "{\"data\":{\"klines\":[" + string.Join(",", lines) + "]}}";
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", relativePath);
    }

    private sealed class CancellableHistoryClient : IChartMarketDataClient
    {
        private readonly Dictionary<string, int> _requestCounts = new(StringComparer.OrdinalIgnoreCase);

        public Func<string, CancellationToken, Task<EastMoneyHistoryFetchResult>> DeepHandler { get; set; }
            = (code, _) => Task.FromResult(Result(code, BusinessDaysEnding(3000)));

        public int TotalDeepRequests { get; private set; }

        public CancellationToken LastRequestToken { get; private set; }

        public int GetRequestCount(string code)
            => _requestCounts.GetValueOrDefault(code);

        public Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
            string secId,
            bool isEtf,
            bool preferDaily,
            CancellationToken cancellationToken)
            => Invoke(secId, cancellationToken);

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryDepthAsync(
            string tencentCode,
            int targetPointCount,
            CancellationToken cancellationToken)
            => Invoke(tencentCode, cancellationToken);

        private Task<EastMoneyHistoryFetchResult> Invoke(string code, CancellationToken cancellationToken)
        {
            TotalDeepRequests++;
            _requestCounts[code] = GetRequestCount(code) + 1;
            LastRequestToken = cancellationToken;
            return DeepHandler(code, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}
