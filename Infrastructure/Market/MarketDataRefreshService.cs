using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public sealed class MarketDataRefreshService : IDisposable
{
    private static readonly TimeSpan ErrorLogMinInterval = TimeSpan.FromMinutes(2);
    private const int MinimumDailyIndexHistoryPointCount = 100;
    private static readonly HashSet<string> DailyIndexHistorySecIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "251.NDXTMC",
        "100.NDX100"
    };

    private readonly LocalDataRepository _repository;
    private readonly MarketDataClient _client;
    private readonly GlobalMarketRequestScheduler _scheduler;
    private readonly Dictionary<string, MarketCircuitBreaker> _breakers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastErrorLogAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public MarketDataRefreshService(LocalDataRepository repository, GlobalMarketRequestScheduler? scheduler = null)
    {
        _repository = repository;
        _scheduler = scheduler ?? new GlobalMarketRequestScheduler();
        _client = new MarketDataClient(_scheduler);
    }

    public async Task RefreshAsync(
        IReadOnlyList<StrategyConfigRecord> strategies,
        IReadOnlyList<PositionStateRecord> positions,
        IReadOnlyList<OtcChannelRecord> otcChannels,
        CancellationToken cancellationToken)
    {
        if (!_refreshLock.Wait(0))
        {
            return;
        }

        try
        {
            await RefreshTencentEtfsAsync(strategies, positions, cancellationToken).ConfigureAwait(false);
            await RefreshEastMoneyRealtimeAsync(strategies, cancellationToken).ConfigureAwait(false);
            await RefreshSinaFundsAsync(otcChannels, cancellationToken).ConfigureAwait(false);
            await RefreshHistoryAsync(strategies, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshTencentEtfsAsync(
        IReadOnlyList<StrategyConfigRecord> strategies,
        IReadOnlyList<PositionStateRecord> positions,
        CancellationToken cancellationToken)
    {
        MarketWatchItem[] items = BuildTencentEtfItems(strategies, positions);
        if (items.Length == 0)
        {
            return;
        }

        await RefreshSourceAsync(
            MarketSources.Tencent,
            MarketRequestKind.EtfQuote,
            null,
            TimeSpan.FromSeconds(6),
            TimeSpan.FromSeconds(10),
            () => _client.FetchTencentEtfQuotesAsync(items, cancellationToken),
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshEastMoneyRealtimeAsync(IReadOnlyList<StrategyConfigRecord> strategies, CancellationToken cancellationToken)
    {
        MarketWatchItem[] items = BuildEastMoneyItems(strategies);
        await RefreshSourceAsync(
            MarketSources.EastMoney,
            MarketRequestKind.IndexQuote,
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            () => _client.FetchEastMoneyQuotesAsync(items, cancellationToken),
            quotes => LogMissingEastMoneyItems(items, quotes),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshSinaFundsAsync(IReadOnlyList<OtcChannelRecord> otcChannels, CancellationToken cancellationToken)
    {
        string[] codes = otcChannels
            .Where(channel => channel.Enabled)
            .Select(channel => MarketSymbolNormalizer.DigitsOnly(channel.OtcCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (codes.Length == 0)
        {
            return;
        }

        await RefreshSourceAsync(
            MarketSources.SinaFund,
            MarketRequestKind.SinaFundNav,
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
            () => _client.FetchSinaFundQuotesAsync(codes, cancellationToken),
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshHistoryAsync(IReadOnlyList<StrategyConfigRecord> strategies, CancellationToken cancellationToken)
    {
        foreach (string secId in new[] { "251.NDXTMC", "100.NDX100" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldRefreshIndexHistory(secId))
            {
                await RefreshHistoryOneAsync(secId, "INDEX", secId, null, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (StrategyConfigRecord strategy in strategies.Where(strategy => strategy.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string etfCode = MarketSymbolNormalizer.DigitsOnly(strategy.Code);
            if (MarketDataSourceRouter.ShouldRefreshEtfHistoryWithEastMoney()
                && !string.IsNullOrWhiteSpace(etfCode)
                && _repository.ReadTodayMarketHistory(etfCode, "ETF") is null)
            {
                string secId = MarketSymbolNormalizer.NormalizeEastMoneyEtfSecId(etfCode);
                await RefreshHistoryOneAsync(etfCode, "ETF", secId, strategy.EtfHigh, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(strategy.IndexSecId))
            {
                string indexSecId = MarketSymbolNormalizer.NormalizeEastMoneySecId(strategy.IndexSecId, true);
                if (ShouldRefreshIndexHistory(indexSecId))
                {
                    await RefreshHistoryOneAsync(indexSecId, "INDEX", indexSecId, strategy.IndexHigh, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private bool ShouldRefreshIndexHistory(string secId)
    {
        MarketQuoteRecord? cached = _repository.ReadTodayMarketHistory(secId, "INDEX");
        if (cached is null)
        {
            return true;
        }

        if (!DailyIndexHistorySecIds.Contains(secId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cached.RawPayload))
        {
            return true;
        }

        try
        {
            return EastMoneyHistoryParser.ParsePoints(cached.RawPayload).Count < MinimumDailyIndexHistoryPointCount;
        }
        catch
        {
            return true;
        }
    }

    private async Task RefreshHistoryOneAsync(
        string symbol,
        string marketType,
        string secId,
        double? existingHigh,
        CancellationToken cancellationToken)
    {
        string sourceKey = MarketSources.EastMoneyHistory + ":" + symbol;
        DateTimeOffset now = DateTimeOffset.Now;
        if (TryRaiseCachedHistoryHighFromRealtimeQuote(symbol, marketType, existingHigh))
        {
            SaveSourceStatus(MarketSources.EastMoneyHistory, "OK", LocalDatabase.NowText(), null, 0, null);
            return;
        }

        if (!_scheduler.TryAcquire(MarketRequestKind.IndexDailyHistory, symbol, now, out MarketRequestDecision schedulerDecision))
        {
            SaveSourceStatus(MarketSources.EastMoneyHistory, "RATE_LIMIT", null, schedulerDecision.Reason, 0, schedulerDecision.NextAllowedAt);
            return;
        }

        MarketCircuitBreaker breaker = GetBreaker(sourceKey);
        if (!breaker.CanRequest(now))
        {
            SaveSourceStatus(MarketSources.EastMoneyHistory, "COOLDOWN", null, breaker.LastError, breaker.FailureCount, breaker.CooldownUntil);
            return;
        }

        try
        {
            EastMoneyHistoryFetchResult history = await _client.FetchEastMoneyHistoryAsync(secId, marketType == "ETF", cancellationToken).ConfigureAwait(false);
            if (history.High <= 0)
            {
                throw new InvalidOperationException(symbol + " 历史 K 线未返回可用高点。");
            }

            double finalHigh = marketType == "ETF"
                ? history.High
                : existingHigh.HasValue ? Math.Max(existingHigh.Value, history.High) : history.High;
            MarketQuoteRecord? realtime = _repository.ReadMarketQuoteCache()
                .FirstOrDefault(quote => string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase));
            if (realtime?.Price is double currentPrice)
            {
                finalHigh = Math.Max(finalHigh, currentPrice);
            }

            _repository.SaveMarketHistory(symbol, marketType, finalHigh, history.RawPayload);
            breaker.RecordSuccess();
            _scheduler.RecordSuccess(MarketRequestKind.IndexDailyHistory, symbol, DateTimeOffset.Now);
            SaveSourceStatus(MarketSources.EastMoneyHistory, "OK", LocalDatabase.NowText(), null, 0, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string error = FormatException(ex);
            DateTimeOffset? cooldown = breaker.RecordFailure(error, now);
            cooldown = Max(cooldown, _scheduler.RecordFailure(MarketRequestKind.IndexDailyHistory, symbol, error, now));
            SaveSourceStatus(MarketSources.EastMoneyHistory, cooldown is null ? "ERROR" : "COOLDOWN", null, error, breaker.FailureCount, cooldown);
            WriteErrorLog(MarketSources.EastMoneyHistory, "历史高点刷新失败", symbol + ": " + error);
        }
    }

    private bool TryRaiseCachedHistoryHighFromRealtimeQuote(string symbol, string marketType, double? existingHigh)
    {
        MarketQuoteRecord? realtime = _repository.ReadMarketQuoteCache()
            .FirstOrDefault(quote => string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase));
        if (realtime?.Price is not double currentPrice || currentPrice <= 0)
        {
            return false;
        }

        MarketQuoteRecord? cached = _repository.ReadLatestMarketHistory(symbol, marketType);
        if (cached is null || string.IsNullOrWhiteSpace(cached.RawPayload))
        {
            return false;
        }

        double cachedHigh = cached.HighValue ?? 0d;
        double baselineHigh = existingHigh.HasValue ? Math.Max(existingHigh.Value, cachedHigh) : cachedHigh;
        if (currentPrice <= baselineHigh)
        {
            return false;
        }

        string source = string.IsNullOrWhiteSpace(cached.Source)
            ? MarketSources.EastMoneyHistory
            : cached.Source;
        _repository.SaveMarketHistory(symbol, marketType, currentPrice, cached.RawPayload, source);
        return true;
    }

    private async Task RefreshSourceAsync(
        string source,
        MarketRequestKind requestKind,
        string? symbol,
        TimeSpan minInterval,
        TimeSpan maxInterval,
        Func<Task<IReadOnlyList<MarketQuoteRecord>>> fetch,
        Action<IReadOnlyList<MarketQuoteRecord>>? afterSuccess,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (!_scheduler.TryAcquire(requestKind, symbol, now, out MarketRequestDecision schedulerDecision))
        {
            if (schedulerDecision.IsCircuitOpen)
            {
                SaveSourceStatus(source, "COOLDOWN", null, schedulerDecision.Reason, 0, schedulerDecision.NextAllowedAt);
            }

            return;
        }

        MarketCircuitBreaker breaker = GetBreaker(source);
        if (!breaker.CanRequest(now))
        {
            SaveSourceStatus(source, "COOLDOWN", null, breaker.LastError, breaker.FailureCount, breaker.CooldownUntil);
            return;
        }

        try
        {
            IReadOnlyList<MarketQuoteRecord> quotes = await fetch().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (quotes.Count == 0)
            {
                throw new InvalidOperationException(source + " 未返回可用行情。");
            }

            _repository.SaveMarketQuotes(quotes);
            afterSuccess?.Invoke(quotes);
            breaker.RecordSuccess();
            _scheduler.RecordSuccess(requestKind, symbol, DateTimeOffset.Now);
            SaveSourceStatus(source, "OK", LocalDatabase.NowText(), null, 0, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string error = FormatException(ex);
            DateTimeOffset? cooldown = breaker.RecordFailure(error, now);
            cooldown = Max(cooldown, _scheduler.RecordFailure(requestKind, symbol, error, now));
            SaveSourceStatus(source, cooldown is null ? "ERROR" : "COOLDOWN", null, error, breaker.FailureCount, cooldown);
            WriteErrorLog(source, "行情请求失败", error);
        }
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

    private void SaveSourceStatus(string source, string status, string? successAt, string? error, int failureCount, DateTimeOffset? cooldownUntil)
    {
        _repository.SaveMarketSourceStatus(new MarketSourceStatusRecord
        {
            Source = source,
            Status = status,
            LastSuccessAt = successAt,
            LastFailureAt = error is null ? null : LocalDatabase.NowText(),
            FailureCount = failureCount,
            CooldownUntil = cooldownUntil?.ToString("yyyy-MM-dd HH:mm:ss"),
            LastError = error,
            UpdatedAt = LocalDatabase.NowText()
        });
    }

    private void WriteErrorLog(string source, string message, string detail)
    {
        string key = source + ":" + detail;
        DateTimeOffset now = DateTimeOffset.Now;
        if (_lastErrorLogAt.TryGetValue(key, out DateTimeOffset last) && now - last < ErrorLogMinInterval)
        {
            return;
        }

        _lastErrorLogAt[key] = now;
        _repository.WriteRuntimeLog("ERROR", source, message, detail);
    }

    private void LogMissingEastMoneyItems(IReadOnlyList<MarketWatchItem> requestedItems, IReadOnlyList<MarketQuoteRecord> quotes)
    {
        foreach (MarketWatchItem item in requestedItems)
        {
            bool returned = quotes.Any(quote => string.Equals(quote.RawCode, item.RawCode, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(quote.Symbol, item.Symbol, StringComparison.OrdinalIgnoreCase));
            if (!returned)
            {
                WriteErrorLog(MarketSources.EastMoney, "东方财富行情字段缺失", item.DisplayName + " secid 未返回：" + item.RawCode);
            }
        }
    }

    private MarketCircuitBreaker GetBreaker(string source)
    {
        if (!_breakers.TryGetValue(source, out MarketCircuitBreaker? breaker))
        {
            breaker = new MarketCircuitBreaker(3, TimeSpan.FromMinutes(10));
            _breakers[source] = breaker;
        }

        return breaker;
    }

    private static MarketWatchItem[] BuildTencentEtfItems(IReadOnlyList<StrategyConfigRecord> strategies, IReadOnlyList<PositionStateRecord> positions)
    {
        IEnumerable<(string Code, string Name)> strategyCodes = strategies
            .Where(strategy => strategy.Enabled)
            .Select(strategy => (strategy.Code, strategy.Name));
        IEnumerable<(string Code, string Name)> positionCodes = positions
            .Where(position => string.Equals(position.Source, "场内ETF", StringComparison.Ordinal))
            .Select(position => (position.ActualCode, position.ActualCode));

        return strategyCodes.Concat(positionCodes)
            .Select(item => MarketSymbolNormalizer.NormalizeTencentEtf(item.Code, item.Name))
            .Where(item => item.Symbol.Length == 6)
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static MarketWatchItem[] BuildEastMoneyItems(IReadOnlyList<StrategyConfigRecord> strategies)
    {
        var items = new List<MarketWatchItem>(MarketSymbolNormalizer.DefaultTopBarItems());
        foreach (StrategyConfigRecord strategy in strategies.Where(strategy => strategy.Enabled))
        {
            if (!string.IsNullOrWhiteSpace(strategy.IndexSecId))
            {
                string indexSecId = MarketSymbolNormalizer.NormalizeEastMoneySecId(strategy.IndexSecId, true);
                items.Add(MarketSymbolNormalizer.NormalizeEastMoneySecId(indexSecId, strategy.Code + " 跟踪指数", "INDEX"));
            }
        }

        return items
            .GroupBy(item => item.RawCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public void Dispose()
    {
        _client.Dispose();
        _refreshLock.Dispose();
    }
}
