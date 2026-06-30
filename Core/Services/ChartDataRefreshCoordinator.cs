using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

// LOCKED: Chart live refresh must remain cache-first, scheduler-gated, and non-mutating for TradeLog/order drafts.
public sealed class ChartDataRefreshCoordinator : IDisposable
{
    private static readonly TimeSpan IntradayRequestInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BackgroundIntradayRequestInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IndexIntradayCatchUpRequestInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan KLineRequestInterval = TimeSpan.FromMinutes(5);
    private const int BackgroundIntradayMaxRequestsPerTick = 1;

    private readonly ChartSubscriptionService _subscriptions;
    private readonly ChartCache _cache;
    private readonly IChartMarketDataClient _client;
    private readonly GlobalMarketRequestScheduler? _scheduler;
    private readonly IChartIntradayCacheStore? _intradayCacheStore;
    private readonly Action<string, string, double?, string, string>? _historyCacheSaver;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly Dictionary<string, DateTimeOffset> _lastIntradayRequestAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastKLineRequestAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _indexIntradayCatchUpAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarketCircuitBreaker> _breakers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Action<string, string, string>? _runtimeLog;
    private IReadOnlyList<MarketQuoteRecord> _lastQuotes = Array.Empty<MarketQuoteRecord>();
    private IReadOnlyList<MarketQuoteRecord> _lastHistory = Array.Empty<MarketQuoteRecord>();
    private bool _disposed;

    public ChartDataRefreshCoordinator(
        ChartSubscriptionService subscriptions,
        ChartCache cache,
        IChartMarketDataClient? client = null,
        IChartIntradayCacheStore? intradayCacheStore = null,
        Action<string, string, double?, string, string>? historyCacheSaver = null,
        Action<string, string, string>? runtimeLog = null,
        GlobalMarketRequestScheduler? scheduler = null,
        Func<DateTimeOffset>? nowProvider = null)
    {
        _subscriptions = subscriptions;
        _cache = cache;
        _scheduler = scheduler;
        _client = client ?? new MarketDataClient(scheduler);
        _intradayCacheStore = intradayCacheStore;
        _historyCacheSaver = historyCacheSaver;
        _runtimeLog = runtimeLog;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
    }

    public event EventHandler<SecurityChartSnapshot>? SnapshotUpdated;

    public async Task RefreshAsync(
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketQuoteRecord> history,
        CancellationToken cancellationToken)
        => await RefreshAsync(
            quotes,
            history,
            Array.Empty<ChartSecurityInfo>(),
            cancellationToken).ConfigureAwait(false);

    public async Task RefreshAsync(
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketQuoteRecord> history,
        IReadOnlyList<ChartSecurityInfo> backgroundIntradaySecurities,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        _lastQuotes = quotes;
        _lastHistory = history;
        ChartSubscription[] active = _subscriptions.ActiveSubscriptions.ToArray();
        ChartSecurityInfo[] backgroundSecurities = NormalizeBackgroundSecurities(backgroundIntradaySecurities);
        bool entered;
        try
        {
            entered = (active.Length > 0 || backgroundSecurities.Length > 0)
                      && await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!entered)
        {
            return;
        }

        try
        {
            var activeIntradaySymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChartSubscription subscription in active
                         .GroupBy(item => item.Security.StrategyCode, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.Last()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshSubscriptionAsync(subscription, quotes, history, activeIntradaySymbols, cancellationToken).ConfigureAwait(false);
            }

            await RefreshBackgroundIntradayCacheAsync(backgroundSecurities, activeIntradaySymbols, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // The application may be closing while a chart refresh is completing.
            }
        }
    }

    public void PublishCachedOrBuild(ChartSubscription subscription)
    {
        if (_disposed)
        {
            return;
        }

        SecurityChartSnapshot snapshot = BuildSnapshot(subscription);
        _cache.SaveSnapshot(snapshot);
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private async Task RefreshSubscriptionAsync(
        ChartSubscription subscription,
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketQuoteRecord> history,
        ISet<string> activeIntradaySymbols,
        CancellationToken cancellationToken)
    {
        if (subscription.Period == SecurityChartPeriod.Intraday)
        {
            activeIntradaySymbols.Add(NormalizeStrategyCode(subscription.Security.StrategyCode));
            MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(
                subscription.Security.InstrumentType,
                MarketDataPurpose.IntradayChart);
            if (route.AllowNetworkRequest
                && route.Provider == MarketDataProvider.EastMoney)
            {
                await TryRefreshIntradayAsync(
                    subscription.Security,
                    IntradayRequestInterval,
                    "指数真实分时数据",
                    cancellationToken).ConfigureAwait(false);
            }
            else if (route.AllowNetworkRequest
                     && route.Provider == MarketDataProvider.Tencent)
            {
                await TryRefreshTencentIntradayAsync(
                    subscription.Security,
                    IntradayRequestInterval,
                    "腾讯真实分时数据",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                UseRouteCacheOrIntradayUnavailable(subscription.Security, route, _nowProvider());
            }
        }

        if (subscription.Period is SecurityChartPeriod.Weekly or SecurityChartPeriod.Monthly)
        {
            MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(
                subscription.Security.InstrumentType,
                MarketDataPurpose.DailyHistory);
            if (!HasDailyCache(subscription.Security.StrategyCode)
                && !HasDailyHistory(subscription.Security, history))
            {
                UseRouteCacheOrDailyUnavailable(subscription.Security, route, _nowProvider());
            }
        }

        if (subscription.Period == SecurityChartPeriod.Daily)
        {
            MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(
                subscription.Security.InstrumentType,
                MarketDataPurpose.DailyHistory);
            if (HasDailyCache(subscription.Security.StrategyCode)
                || HasDailyHistory(subscription.Security, history))
            {
                // DailyLike cache is enough for display; do not request the network on every chart open.
            }
            else if (route.AllowNetworkRequest
                && route.Provider == MarketDataProvider.EastMoney)
            {
                await TryRefreshDailyKLineAsync(subscription, cancellationToken).ConfigureAwait(false);
            }
            else if (route.AllowNetworkRequest
                     && route.Provider == MarketDataProvider.Tencent)
            {
                await TryRefreshTencentDailyKLineAsync(subscription, cancellationToken).ConfigureAwait(false);
            }
            else if (!HasDailyCache(subscription.Security.StrategyCode)
                     && !HasDailyHistory(subscription.Security, history))
            {
                UseRouteCacheOrDailyUnavailable(subscription.Security, route, _nowProvider());
            }
        }

        SecurityChartSnapshot snapshot = BuildSnapshot(subscription);
        _cache.SaveSnapshot(snapshot);
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private SecurityChartSnapshot BuildSnapshot(ChartSubscription subscription)
        => ChartDataService.BuildSnapshot(
            subscription.Security,
            subscription.Period,
            subscription.SubPanel,
            _lastQuotes,
            _lastHistory,
            GetIntradayForSnapshot(subscription.Security.StrategyCode),
            _cache.GetDailyKLines(subscription.Security.StrategyCode));

    private async Task RefreshBackgroundIntradayCacheAsync(
        IReadOnlyList<ChartSecurityInfo> securities,
        ISet<string> activeIntradaySymbols,
        CancellationToken cancellationToken)
    {
        if (securities.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _nowProvider();
        int attempted = 0;
        foreach (ChartSecurityInfo security in securities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedCode = NormalizeStrategyCode(security.StrategyCode);
            if (activeIntradaySymbols.Contains(normalizedCode))
            {
                continue;
            }

            MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(
                security.InstrumentType,
                MarketDataPurpose.IntradayChart);
            if (!route.AllowNetworkRequest
                || (route.Provider != MarketDataProvider.EastMoney
                    && route.Provider != MarketDataProvider.Tencent))
            {
                continue;
            }

            if (!ShouldRequestIntradayLive(security, now))
            {
                continue;
            }

            bool requested = route.Provider == MarketDataProvider.Tencent
                ? await TryRefreshTencentIntradayAsync(
                    security,
                    BackgroundIntradayRequestInterval,
                    "后台腾讯真实分时缓存",
                    cancellationToken).ConfigureAwait(false)
                : await TryRefreshIntradayAsync(
                    security,
                    BackgroundIntradayRequestInterval,
                    "后台指数真实分时缓存",
                    cancellationToken).ConfigureAwait(false);
            if (!requested)
            {
                continue;
            }

            attempted++;
            if (attempted >= BackgroundIntradayMaxRequestsPerTick)
            {
                return;
            }
        }
    }

    private async Task<bool> TryRefreshIntradayAsync(
        ChartSecurityInfo security,
        TimeSpan requestInterval,
        string successStatusMessage,
        CancellationToken cancellationToken)
    {
        string key = MarketSources.EastMoneyIntraday + ":" + security.StrategyCode;
        DateTimeOffset now = _nowProvider();
        if (!ShouldRequestIntradayLive(security, now))
        {
            if (security.InstrumentType == ChartInstrumentType.Index
                && ShouldCatchUpIndexIntraday(security.StrategyCode, now, out string catchUpReason))
            {
                return await TryCatchUpIndexIntradayAsync(
                    security,
                    key,
                    catchUpReason,
                    cancellationToken).ConfigureAwait(false);
            }

            ChartDataStatus cacheStatus = new(true, "非交易时段，使用最近真实分时缓存", true);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(false, "非交易时段无可用分时缓存"),
                    now);
            }

            return false;
        }

        if (!CanRequest(key, MarketRequestKind.IndexIntraday, security.StrategyCode, requestInterval, now, out ChartDataStatus blockedStatus))
        {
            ChartDataStatus cacheStatus = blockedStatus.IsCircuitOpen
                ? blockedStatus with { Message = "指数分时接口熔断中，使用最近真实分时缓存" }
                : blockedStatus with { Message = "指数分时接口限频中，使用最近真实分时缓存" };
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                ChartDataStatus status = blockedStatus.IsCircuitOpen
                    ? blockedStatus with { Message = "指数分时接口熔断中，无可用分时缓存" }
                    : blockedStatus with { Message = "指数分时接口限频中，无可用分时缓存" };
                _cache.SaveIntraday(security.StrategyCode, Array.Empty<IntradayPoint>(), status, now);
            }

            return false;
        }

        try
        {
            EastMoneyIntradayFetchResult result = await _client.FetchEastMoneyIntradayAsync(security.EastMoneySecId, cancellationToken).ConfigureAwait(false);
            _cache.SaveIntraday(
                security.StrategyCode,
                result.Points,
                new ChartDataStatus(true, successStatusMessage),
                result.FetchedAt);
            TryPersistIntradayPayload(security, result);
            GetBreaker(key).RecordSuccess();
            _scheduler?.RecordSuccess(MarketRequestKind.IndexIntraday, security.StrategyCode, _nowProvider());
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string message = FormatException(ex);
            now = _nowProvider();
            if (IsNoDataTrends(message))
            {
                if (!TryUsePersistedIntradayCache(
                        security.StrategyCode,
                        new ChartDataStatus(true, "使用最近真实分时缓存", true),
                        now))
                {
                    _cache.SaveIntraday(
                        security.StrategyCode,
                        Array.Empty<IntradayPoint>(),
                        new ChartDataStatus(
                            false,
                            IsNonTradingTime(now.LocalDateTime)
                                ? "非交易时段无指数分时数据"
                                : "指数分时接口失败，无可用分时缓存"),
                        now);
                }

                return true;
            }

            DateTimeOffset? cooldown = GetBreaker(key).RecordFailure(message, now);
            cooldown = Max(cooldown, _scheduler?.RecordFailure(MarketRequestKind.IndexIntraday, security.StrategyCode, message, now));
            ChartDataStatus failureCacheStatus = new(
                true,
                cooldown.HasValue ? "指数分时接口熔断中，使用最近真实分时缓存" : "使用最近真实分时缓存",
                true,
                IsCircuitOpen: cooldown.HasValue);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, failureCacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(
                        false,
                        cooldown.HasValue ? "指数分时接口熔断中，无可用分时缓存" : "指数分时接口失败，无可用分时缓存",
                        IsCircuitOpen: cooldown.HasValue),
                    now);
            }

            _runtimeLog?.Invoke("WARN", "SecurityChart", security.StrategyCode + " 指数分时数据刷新失败：" + message);
            return true;
        }
    }

    private async Task<bool> TryCatchUpIndexIntradayAsync(
        ChartSecurityInfo security,
        string baseKey,
        string catchUpReason,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _nowProvider();
        string catchUpKey = baseKey + ":CATCHUP";
        DateOnly latestCompletedDate = IndexIntradayCacheCompletenessService.GetLatestCompletedUsTradeDate(now);
        string attemptKey = NormalizeStrategyCode(security.StrategyCode) + "|" + latestCompletedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_indexIntradayCatchUpAttempts.Contains(attemptKey))
        {
            ChartDataStatus attemptedStatus = new(
                true,
                "非交易时段，指数分时补拉已尝试；使用最近真实分时缓存，最新价来自quote",
                true);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, attemptedStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(false, "非交易时段无可用指数分时缓存"),
                    now);
            }

            return false;
        }

        if (!CanRequest(catchUpKey, MarketRequestKind.IndexIntraday, security.StrategyCode, IndexIntradayCatchUpRequestInterval, now, out ChartDataStatus blockedStatus))
        {
            ChartDataStatus cacheStatus = blockedStatus with
            {
                Message = blockedStatus.IsCircuitOpen
                    ? "指数分时补拉熔断中，使用最近真实分时缓存"
                    : "指数分时补拉限频中，使用最近真实分时缓存",
                IsUsingCache = true
            };
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    blockedStatus with
                    {
                        Message = blockedStatus.IsCircuitOpen
                            ? "指数分时补拉熔断中，无可用分时缓存"
                            : "指数分时补拉限频中，无可用分时缓存"
                    },
                    now);
            }

            return false;
        }

        _indexIntradayCatchUpAttempts.Add(attemptKey);
        try
        {
            EastMoneyIntradayFetchResult result = await _client.FetchEastMoneyIntradayAsync(security.EastMoneySecId, cancellationToken).ConfigureAwait(false);
            IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(result.Points, now);
            string statusMessage = completeness.IsCompleteSession
                ? "指数完整交易日分时补拉缓存"
                : "指数分时补拉返回真实缓存，但仍不完整";
            _cache.SaveIntraday(
                security.StrategyCode,
                result.Points,
                new ChartDataStatus(true, statusMessage, true),
                result.FetchedAt);
            TryPersistIntradayPayload(security, result);
            GetBreaker(baseKey).RecordSuccess();
            _scheduler?.RecordSuccess(MarketRequestKind.IndexIntraday, security.StrategyCode, _nowProvider());
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string message = FormatException(ex);
            now = _nowProvider();
            DateTimeOffset? cooldown = IsNoDataTrends(message)
                ? null
                : Max(
                    GetBreaker(baseKey).RecordFailure(message, now),
                    _scheduler?.RecordFailure(MarketRequestKind.IndexIntraday, security.StrategyCode, message, now));
            ChartDataStatus cacheStatus = new(
                true,
                "指数分时补拉失败，保留最近真实分时缓存；最新价来自quote",
                true,
                IsCircuitOpen: cooldown.HasValue);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(
                        false,
                        "指数分时补拉失败，无可用分时缓存",
                        IsCircuitOpen: cooldown.HasValue),
                    now);
            }

            _runtimeLog?.Invoke("WARN", "SecurityChart", security.StrategyCode + " 指数分时补拉失败：" + message + "；reason=" + catchUpReason);
            return true;
        }
    }

    private async Task<bool> TryRefreshTencentIntradayAsync(
        ChartSecurityInfo security,
        TimeSpan requestInterval,
        string successStatusMessage,
        CancellationToken cancellationToken)
    {
        string key = MarketSources.TencentIntraday + ":" + security.StrategyCode;
        DateTimeOffset now = _nowProvider();
        if (!ShouldRequestIntradayLive(security, now))
        {
            ChartDataStatus cacheStatus = new(true, "非交易时段，使用最近真实分时缓存", true);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(false, "非交易时段无可用分时缓存"),
                    now);
            }

            return false;
        }

        MarketRequestKind requestKind = requestInterval <= IntradayRequestInterval
            ? MarketRequestKind.EtfIntradayActive
            : MarketRequestKind.EtfIntradayBackground;
        if (!CanRequest(key, requestKind, security.StrategyCode, requestInterval, now, out ChartDataStatus blockedStatus))
        {
            ChartDataStatus cacheStatus = blockedStatus.IsCircuitOpen
                ? blockedStatus with { Message = "腾讯分时接口熔断中，使用最近真实分时缓存" }
                : blockedStatus with { Message = "腾讯分时接口限频中，使用最近真实分时缓存" };
            if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
            {
                ChartDataStatus status = blockedStatus.IsCircuitOpen
                    ? blockedStatus with { Message = "腾讯分时接口熔断中，无可用分时缓存" }
                    : blockedStatus with { Message = "腾讯分时接口限频中，无可用分时缓存" };
                _cache.SaveIntraday(security.StrategyCode, Array.Empty<IntradayPoint>(), status, now);
            }

            return false;
        }

        try
        {
            string tencentCode = ResolveTencentCode(security);
            EastMoneyIntradayFetchResult result = await _client.FetchTencentIntradayAsync(tencentCode, cancellationToken).ConfigureAwait(false);
            _cache.SaveIntraday(
                security.StrategyCode,
                result.Points,
                new ChartDataStatus(true, successStatusMessage),
                result.FetchedAt);
            TryPersistIntradayPayload(
                security,
                result,
                MarketSources.TencentIntraday,
                MarketSources.TencentIntradayQuality);
            GetBreaker(key).RecordSuccess();
            _scheduler?.RecordSuccess(requestKind, security.StrategyCode, _nowProvider());
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string message = FormatException(ex);
            now = _nowProvider();
            if (IsNoMinuteData(message))
            {
                if (!TryUsePersistedIntradayCache(
                        security.StrategyCode,
                        new ChartDataStatus(true, "使用最近真实分时缓存", true),
                        now))
                {
                    _cache.SaveIntraday(
                        security.StrategyCode,
                        Array.Empty<IntradayPoint>(),
                        new ChartDataStatus(
                            false,
                            IsNonTradingTime(now.LocalDateTime)
                                ? "非交易时段无腾讯分时数据"
                                : "腾讯分时接口失败，无可用分时缓存"),
                        now);
                }

                return true;
            }

            DateTimeOffset? cooldown = GetBreaker(key).RecordFailure(message, now);
            cooldown = Max(cooldown, _scheduler?.RecordFailure(requestKind, security.StrategyCode, message, now));
            ChartDataStatus failureCacheStatus = new(
                true,
                cooldown.HasValue ? "腾讯分时接口熔断中，使用最近真实分时缓存" : "使用最近真实分时缓存",
                true,
                IsCircuitOpen: cooldown.HasValue);
            if (!TryUsePersistedIntradayCache(security.StrategyCode, failureCacheStatus, now))
            {
                _cache.SaveIntraday(
                    security.StrategyCode,
                    Array.Empty<IntradayPoint>(),
                    new ChartDataStatus(
                        false,
                        cooldown.HasValue ? "腾讯分时接口熔断中，无可用分时缓存" : "腾讯分时接口失败，无可用分时缓存",
                        IsCircuitOpen: cooldown.HasValue),
                    now);
            }

            _runtimeLog?.Invoke("WARN", "SecurityChart", security.StrategyCode + " 腾讯分时数据刷新失败：" + message);
            return true;
        }
    }

    private ChartIntradayCacheEntry? GetIntradayForSnapshot(string strategyCode)
    {
        ChartIntradayCacheEntry? memory = _cache.GetIntraday(strategyCode);
        if (memory is not null)
        {
            return memory;
        }

        ChartIntradayCacheEntry? persisted = _intradayCacheStore?.ReadLatestChartIntradayCache(strategyCode);
        if (persisted?.Points.Count > 0)
        {
            _cache.SaveIntraday(strategyCode, persisted.Points, persisted.Status, persisted.UpdatedAt);
            return _cache.GetIntraday(strategyCode) ?? persisted;
        }

        return null;
    }

    private bool TryUsePersistedIntradayCache(string strategyCode, ChartDataStatus status, DateTimeOffset updatedAt)
    {
        ChartIntradayCacheEntry? memory = _cache.GetIntraday(strategyCode);
        if (memory?.Points.Count > 0)
        {
            _cache.SaveIntraday(strategyCode, memory.Points, status, updatedAt);
            return true;
        }

        ChartIntradayCacheEntry? persisted = _intradayCacheStore?.ReadLatestChartIntradayCache(strategyCode);
        if (persisted?.Points.Count > 0)
        {
            _cache.SaveIntraday(strategyCode, persisted.Points, status, updatedAt);
            return true;
        }

        return false;
    }

    private bool ShouldCatchUpIndexIntraday(string strategyCode, DateTimeOffset now, out string reason)
    {
        reason = "cache is complete";
        if (IndexIntradayCacheCompletenessService.IsUsTradingSession(now))
        {
            reason = "US session is trading";
            return false;
        }

        ChartIntradayCacheEntry? cacheEntry = GetIntradayForSnapshot(strategyCode);
        IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(
            cacheEntry?.Points ?? Array.Empty<IntradayPoint>(),
            now);
        reason = completeness.Reason;
        return completeness.ShouldCatchUp;
    }

    private void UseRouteCacheOrIntradayUnavailable(
        ChartSecurityInfo security,
        MarketDataSourceRoute route,
        DateTimeOffset now)
    {
        ChartDataStatus cacheStatus = new(true, route.StatusMessage + "，使用最近真实分时缓存", true);
        if (!TryUsePersistedIntradayCache(security.StrategyCode, cacheStatus, now))
        {
            _cache.SaveIntraday(
                security.StrategyCode,
                Array.Empty<IntradayPoint>(),
                new ChartDataStatus(false, route.StatusMessage + "，无可用分时缓存"),
                now);
        }
    }

    private void UseRouteCacheOrDailyUnavailable(
        ChartSecurityInfo security,
        MarketDataSourceRoute route,
        DateTimeOffset now)
    {
        ChartKLineCacheEntry? existing = _cache.GetDailyKLines(security.StrategyCode);
        if (existing?.Points.Count > 0)
        {
            _cache.SaveDailyKLines(
                security.StrategyCode,
                existing.Points,
                new ChartDataStatus(true, route.StatusMessage + "，使用最近真实日K缓存", true),
                now);
            return;
        }

        _cache.SaveDailyKLines(
            security.StrategyCode,
            Array.Empty<KLinePoint>(),
            new ChartDataStatus(false, route.StatusMessage + "，无可用DailyLike日K缓存"),
            now);
    }

    private void TryPersistIntradayPayload(
        ChartSecurityInfo security,
        EastMoneyIntradayFetchResult result,
        string source = MarketSources.EastMoneyIntraday,
        string quality = "REAL_TRENDS2")
    {
        if (_intradayCacheStore is null
            || string.IsNullOrWhiteSpace(result.RawPayload)
            || result.Points.Count == 0)
        {
            return;
        }

        try
        {
            _intradayCacheStore.SaveChartIntradayCache(
                security.StrategyCode,
                security.ActualCode,
                result.RawPayload,
                result.FetchedAt,
                source,
                quality);
        }
        catch (Exception ex)
        {
            _runtimeLog?.Invoke(
                "WARN",
                "SecurityChart",
                security.StrategyCode + " 分时缓存写入失败：" + FormatException(ex));
        }
    }

    private async Task TryRefreshDailyKLineAsync(ChartSubscription subscription, CancellationToken cancellationToken)
    {
        string key = MarketSources.EastMoneyHistory + ":CHART:" + subscription.Security.StrategyCode;
        DateTimeOffset now = _nowProvider();
        if (!CanRequest(key, MarketRequestKind.IndexDailyHistory, subscription.Security.StrategyCode, KLineRequestInterval, now, out ChartDataStatus blockedStatus))
        {
            if (_cache.GetDailyKLines(subscription.Security.StrategyCode) is null)
            {
                ChartDataStatus status = blockedStatus.IsCircuitOpen
                    ? blockedStatus with { Message = "K线接口熔断中，无可用DailyLike日K缓存" }
                    : blockedStatus with { Message = "K线接口限频中，无可用DailyLike日K缓存" };
                _cache.SaveDailyKLines(subscription.Security.StrategyCode, Array.Empty<KLinePoint>(), status, now);
            }

            return;
        }

        try
        {
            EastMoneyHistoryFetchResult result = await _client.FetchEastMoneyHistoryAsync(
                subscription.Security.EastMoneySecId,
                isEtf: subscription.Security.InstrumentType == ChartInstrumentType.Etf,
                preferDaily: true,
                cancellationToken).ConfigureAwait(false);
            MarketHistoryQualityInfo quality = MarketHistoryQuality.Analyze(result.RawPayload);
            if (quality.Frequency != MarketHistoryFrequency.DailyLike)
            {
                string statusMessage = quality.Frequency switch
                {
                    MarketHistoryFrequency.MonthlyLike => "接口返回月线数据，未作为DailyLike日K使用",
                    MarketHistoryFrequency.WeeklyLike => "接口返回稀疏数据，DailyLike日K不可用",
                    _ => "接口返回无效数据，DailyLike日K不可用"
                };
                _cache.SaveDailyKLines(
                    subscription.Security.StrategyCode,
                    Array.Empty<KLinePoint>(),
                    new ChartDataStatus(false, statusMessage),
                    now);
                _runtimeLog?.Invoke("WARN", "SecurityChart", subscription.Security.StrategyCode + " " + statusMessage);
                return;
            }

            IReadOnlyList<KLinePoint> points = KLineAggregator.FromHistoryPoints(EastMoneyHistoryParser.ParsePoints(result.RawPayload));
            _cache.SaveDailyKLines(
                subscription.Security.StrategyCode,
                points,
                new ChartDataStatus(points.Count > 0, points.Count > 0 ? "真实日K接口缓存" : "日K数据暂不可用", true),
                now);
            GetBreaker(key).RecordSuccess();
            _scheduler?.RecordSuccess(MarketRequestKind.IndexDailyHistory, subscription.Security.StrategyCode, _nowProvider());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string message = FormatException(ex);
            now = _nowProvider();
            DateTimeOffset? cooldown = GetBreaker(key).RecordFailure(message, now);
            cooldown = Max(cooldown, _scheduler?.RecordFailure(MarketRequestKind.IndexDailyHistory, subscription.Security.StrategyCode, message, now));
            ChartKLineCacheEntry? existing = _cache.GetDailyKLines(subscription.Security.StrategyCode);
            ChartDataStatus status = existing?.Points.Count > 0
                ? new ChartDataStatus(true, "使用最近真实日K缓存", true, IsCircuitOpen: cooldown.HasValue)
                : new ChartDataStatus(false, cooldown.HasValue ? "K线接口熔断中，无可用DailyLike日K缓存" : "接口失败，无可用DailyLike日K缓存", IsCircuitOpen: cooldown.HasValue);
            _cache.SaveDailyKLines(
                subscription.Security.StrategyCode,
                existing?.Points ?? Array.Empty<KLinePoint>(),
                status,
                now);
            _runtimeLog?.Invoke("WARN", "SecurityChart", subscription.Security.StrategyCode + " 日K数据刷新失败：" + message);
        }
    }

    private async Task TryRefreshTencentDailyKLineAsync(ChartSubscription subscription, CancellationToken cancellationToken)
    {
        string key = MarketSources.TencentHistory + ":CHART:" + subscription.Security.StrategyCode;
        DateTimeOffset now = _nowProvider();
        if (!CanRequest(key, MarketRequestKind.EtfDailyKLine, subscription.Security.StrategyCode, KLineRequestInterval, now, out ChartDataStatus blockedStatus))
        {
            if (_cache.GetDailyKLines(subscription.Security.StrategyCode) is null)
            {
                ChartDataStatus status = blockedStatus.IsCircuitOpen
                    ? blockedStatus with { Message = "腾讯日K接口熔断中，无可用DailyLike日K缓存" }
                    : blockedStatus with { Message = "腾讯日K接口限频中，无可用DailyLike日K缓存" };
                _cache.SaveDailyKLines(subscription.Security.StrategyCode, Array.Empty<KLinePoint>(), status, now);
            }

            return;
        }

        try
        {
            string tencentCode = ResolveTencentCode(subscription.Security);
            EastMoneyHistoryFetchResult result = await _client.FetchTencentDailyHistoryAsync(
                tencentCode,
                cancellationToken).ConfigureAwait(false);
            MarketHistoryQualityInfo quality = MarketHistoryQuality.Analyze(result.RawPayload);
            if (quality.Frequency != MarketHistoryFrequency.DailyLike)
            {
                string statusMessage = quality.Frequency switch
                {
                    MarketHistoryFrequency.MonthlyLike => "腾讯接口返回月线数据，未作为DailyLike日K使用",
                    MarketHistoryFrequency.WeeklyLike => "腾讯接口返回稀疏数据，DailyLike日K不可用",
                    _ => "腾讯接口返回无效数据，DailyLike日K不可用"
                };
                _cache.SaveDailyKLines(
                    subscription.Security.StrategyCode,
                    Array.Empty<KLinePoint>(),
                    new ChartDataStatus(false, statusMessage),
                    now);
                _runtimeLog?.Invoke("WARN", "SecurityChart", subscription.Security.StrategyCode + " " + statusMessage);
                return;
            }

            IReadOnlyList<KLinePoint> points = KLineAggregator.FromHistoryPoints(EastMoneyHistoryParser.ParsePoints(result.RawPayload));
            _cache.SaveDailyKLines(
                subscription.Security.StrategyCode,
                points,
                new ChartDataStatus(points.Count > 0, points.Count > 0 ? "腾讯真实DailyLike日K缓存" : "日K数据暂不可用", true),
                now);
            TryPersistDailyHistoryPayload(subscription.Security, result, MarketSources.TencentHistory);
            GetBreaker(key).RecordSuccess();
            _scheduler?.RecordSuccess(MarketRequestKind.EtfDailyKLine, subscription.Security.StrategyCode, _nowProvider());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string message = FormatException(ex);
            now = _nowProvider();
            DateTimeOffset? cooldown = GetBreaker(key).RecordFailure(message, now);
            cooldown = Max(cooldown, _scheduler?.RecordFailure(MarketRequestKind.EtfDailyKLine, subscription.Security.StrategyCode, message, now));
            ChartKLineCacheEntry? existing = _cache.GetDailyKLines(subscription.Security.StrategyCode);
            ChartDataStatus status = existing?.Points.Count > 0
                ? new ChartDataStatus(true, "使用最近真实日K缓存", true, IsCircuitOpen: cooldown.HasValue)
                : new ChartDataStatus(false, cooldown.HasValue ? "腾讯日K接口熔断中，无可用DailyLike日K缓存" : "腾讯接口失败，无可用DailyLike日K缓存", IsCircuitOpen: cooldown.HasValue);
            _cache.SaveDailyKLines(
                subscription.Security.StrategyCode,
                existing?.Points ?? Array.Empty<KLinePoint>(),
                status,
                now);
            _runtimeLog?.Invoke("WARN", "SecurityChart", subscription.Security.StrategyCode + " 腾讯日K数据刷新失败：" + message);
        }
    }

    private void TryPersistDailyHistoryPayload(
        ChartSecurityInfo security,
        EastMoneyHistoryFetchResult result,
        string source)
    {
        if (_historyCacheSaver is null
            || string.IsNullOrWhiteSpace(result.RawPayload)
            || result.PointCount <= 0)
        {
            return;
        }

        try
        {
            _historyCacheSaver(
                security.StrategyCode,
                "ETF",
                result.High,
                result.RawPayload,
                source);
        }
        catch (Exception ex)
        {
            _runtimeLog?.Invoke(
                "WARN",
                "SecurityChart",
                security.StrategyCode + " 日K缓存写入失败：" + FormatException(ex));
        }
    }

    private bool CanRequest(
        string key,
        MarketRequestKind requestKind,
        string symbol,
        TimeSpan interval,
        DateTimeOffset now,
        out ChartDataStatus blockedStatus)
    {
        MarketCircuitBreaker breaker = GetBreaker(key);
        if (!breaker.CanRequest(now))
        {
            blockedStatus = new ChartDataStatus(false, "接口熔断中", IsCircuitOpen: true);
            return false;
        }

        if (_scheduler is not null
            && !_scheduler.TryAcquire(requestKind, symbol, now, out MarketRequestDecision schedulerDecision))
        {
            blockedStatus = new ChartDataStatus(
                false,
                schedulerDecision.Reason == "non_quote_tick_budget_exhausted" ? "接口调度排队中" : "接口限频中",
                IsRateLimited: true,
                IsCircuitOpen: schedulerDecision.IsCircuitOpen);
            return false;
        }

        bool hasIntradayLast = _lastIntradayRequestAt.TryGetValue(key, out DateTimeOffset intradayLast);
        bool hasKLineLast = _lastKLineRequestAt.TryGetValue(key, out DateTimeOffset klineLast);
        if (hasIntradayLast || hasKLineLast)
        {
            DateTimeOffset last = hasIntradayLast && (!hasKLineLast || intradayLast > klineLast)
                ? intradayLast
                : klineLast;
            if (now - last < interval)
            {
                blockedStatus = new ChartDataStatus(false, "接口限频中", IsRateLimited: true);
                return false;
            }
        }

        if (key.StartsWith(MarketSources.EastMoneyIntraday, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(MarketSources.TencentIntraday, StringComparison.OrdinalIgnoreCase))
        {
            _lastIntradayRequestAt[key] = now;
        }
        else
        {
            _lastKLineRequestAt[key] = now;
        }

        blockedStatus = new ChartDataStatus(true, "允许请求");
        return true;
    }

    private static ChartSecurityInfo[] NormalizeBackgroundSecurities(IReadOnlyList<ChartSecurityInfo> securities)
        => securities
            .Where(item => !string.IsNullOrWhiteSpace(item.StrategyCode)
                           && !string.IsNullOrWhiteSpace(item.EastMoneySecId))
            .GroupBy(item => NormalizeStrategyCode(item.StrategyCode), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static string NormalizeStrategyCode(string strategyCode)
        => strategyCode.Trim();

    private static string ResolveTencentCode(ChartSecurityInfo security)
    {
        string code = !string.IsNullOrWhiteSpace(security.ActualCode)
            ? security.ActualCode
            : security.StrategyCode;
        return MarketSymbolNormalizer.NormalizeTencentEtf(code).RawCode;
    }

    private static bool ShouldRequestIntradayLive(ChartSecurityInfo security, DateTimeOffset now)
    {
        return security.InstrumentType == ChartInstrumentType.Index
            ? IsUsEasternTrading(now)
            : IsAShareTrading(now.LocalDateTime);
    }

    private static bool IsAShareTrading(DateTime local)
    {
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return IntradayTradingTimeAxis.IsTradingTime(local);
    }

    private static bool IsUsEasternTrading(DateTimeOffset now)
    {
        if (!TryFindTimeZone("Eastern Standard Time", "America/New_York", out TimeZoneInfo? easternZone)
            || easternZone is null)
        {
            return false;
        }

        DateTime eastern = TimeZoneInfo.ConvertTime(now, easternZone).DateTime;
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        TimeSpan time = eastern.TimeOfDay;
        return time >= new TimeSpan(9, 30, 0) && time <= new TimeSpan(16, 0, 0);
    }

    private static bool TryFindTimeZone(string windowsId, string ianaId, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        zone = null;
        return false;
    }

    private static bool HasDailyHistory(ChartSecurityInfo security, IReadOnlyList<MarketQuoteRecord> history)
    {
        string marketType = security.InstrumentType == ChartInstrumentType.Index ? "INDEX" : "ETF";
        return history
            .Where(item => string.Equals(item.MarketType, marketType, StringComparison.OrdinalIgnoreCase)
                           && (string.Equals(item.Symbol, security.ActualCode, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(item.Symbol, security.StrategyCode, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(item.RawCode, security.EastMoneySecId, StringComparison.OrdinalIgnoreCase)))
            .Any(item => MarketHistoryQuality.IsDailyLike(item.RawPayload));
    }

    private bool HasDailyCache(string strategyCode)
        => _cache.GetDailyKLines(strategyCode)?.Points.Count > 0;

    private MarketCircuitBreaker GetBreaker(string key)
    {
        if (!_breakers.TryGetValue(key, out MarketCircuitBreaker? breaker))
        {
            breaker = new MarketCircuitBreaker(3, TimeSpan.FromMinutes(10));
            _breakers[key] = breaker;
        }

        return breaker;
    }

    private static string FormatException(Exception exception)
    {
        var messages = new List<string>();
        Exception? current = exception;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message, StringComparer.Ordinal))
            {
                messages.Add(current.Message);
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages);
    }

    private static DateTimeOffset? Max(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        return first.Value >= second.Value ? first : second;
    }

    public void Dispose()
    {
        _disposed = true;
        _client.Dispose();
        _refreshLock.Dispose();
    }

    private static bool IsNoDataTrends(string message)
        => message.Contains("no data.trends", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoMinuteData(string message)
        => message.Contains("no minute data", StringComparison.OrdinalIgnoreCase)
           || message.Contains("no qfqday data", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonTradingTime(DateTime time)
        => time.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
           || !IntradayTradingTimeAxis.IsTradingTime(time);
}
