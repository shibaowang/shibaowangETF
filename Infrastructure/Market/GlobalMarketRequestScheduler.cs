namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public enum MarketRequestKind
{
    EtfQuote,
    IndexQuote,
    SinaFundNav,
    EtfIntradayActive,
    EtfIntradayBackground,
    IndexIntraday,
    EtfDailyKLine,
    IndexDailyHistory,
    EastMoneyHistoryVariant
}

public sealed record MarketRequestDecision(
    bool Allowed,
    string Reason,
    DateTimeOffset? NextAllowedAt = null,
    bool IsCircuitOpen = false);

// LOCKED: All live market requests must pass through this scheduler; do not bypass for chart or quote refreshes.
public sealed class GlobalMarketRequestScheduler
{
    private static readonly TimeSpan BlockedInitialCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BlockedSecondCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BlockedMaxCooldown = TimeSpan.FromMinutes(60);
    private readonly Dictionary<string, DateTimeOffset> _nextAllowedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NextAllowedReason> _nextAllowedReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FailureState> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random;
    private readonly object _sync = new();
    private int _nonQuoteRequestsThisTick;

    public GlobalMarketRequestScheduler(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public int NonQuoteRequestsThisTick => _nonQuoteRequestsThisTick;

    public void BeginTick(DateTimeOffset now)
    {
        lock (_sync)
        {
            _nonQuoteRequestsThisTick = 0;
        }
    }

    public bool TryAcquire(
        MarketRequestKind kind,
        string? symbol,
        DateTimeOffset now,
        out MarketRequestDecision decision)
    {
        MarketRequestProfile profile = BuildProfile(kind, now);
        lock (_sync)
        {
            if (kind == MarketRequestKind.IndexQuote && IsUsEasternTrading(now))
            {
                ReleaseNonTradingThrottle(profile, symbol);
            }

            return TryAcquire(
                profile.Lane,
                profile.Host,
                profile.Endpoint,
                symbol,
                profile.HostMinInterval,
                profile.HostMaxInterval,
                profile.SymbolMinInterval,
                profile.SymbolMaxInterval,
                profile.Reason,
                now,
                profile.CountsAgainstNonQuoteBudget,
                out decision);
        }
    }

    public bool TryAcquireRaw(
        string host,
        string endpoint,
        string? symbol,
        TimeSpan minInterval,
        TimeSpan maxInterval,
        DateTimeOffset now,
        bool countsAgainstNonQuoteBudget,
        out MarketRequestDecision decision)
    {
        lock (_sync)
        {
            return TryAcquire(
                endpoint,
                host,
                endpoint,
                symbol,
                minInterval,
                maxInterval,
                minInterval,
                maxInterval,
                NextAllowedReason.TradingInterval,
                now,
                countsAgainstNonQuoteBudget,
                out decision);
        }
    }

    public void RecordSuccess(MarketRequestKind kind, string? symbol, DateTimeOffset now)
    {
        MarketRequestProfile profile = BuildProfile(kind, now);
        lock (_sync)
        {
            ClearFailure(profile.Lane, profile.Host, profile.Endpoint, symbol);
        }
    }

    public void ReleaseCancelledRequest(MarketRequestKind kind, string? symbol, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        MarketRequestProfile profile = BuildProfile(kind, now);
        lock (_sync)
        {
            string key = SymbolKey(profile.Host, profile.Lane, profile.Endpoint, symbol);
            if (!_nextAllowedReasons.TryGetValue(key, out NextAllowedReason reason)
                || reason == NextAllowedReason.FailureCooldown)
            {
                return;
            }

            _nextAllowedAt.Remove(key);
            _nextAllowedReasons.Remove(key);
        }
    }

    public DateTimeOffset? RecordFailure(MarketRequestKind kind, string? symbol, string error, DateTimeOffset now)
    {
        MarketRequestProfile profile = BuildProfile(kind, now);
        lock (_sync)
        {
            return RecordFailure(profile.Lane, profile.Host, profile.Endpoint, symbol, error, now);
        }
    }

    public void RecordRawSuccess(string host, string endpoint, string? symbol)
    {
        lock (_sync)
        {
            ClearFailure(endpoint, host, endpoint, symbol);
        }
    }

    public DateTimeOffset? RecordRawFailure(string host, string endpoint, string? symbol, string error, DateTimeOffset now)
    {
        lock (_sync)
        {
            return RecordFailure(endpoint, host, endpoint, symbol, error, now);
        }
    }

    public DateTimeOffset? NextAllowedAt(string host, string endpoint, string? symbol)
    {
        lock (_sync)
        {
            DateTimeOffset? hostNext = TryGetNext(HostKey(host, endpoint));
            DateTimeOffset? endpointNext = TryGetNext(EndpointKey(host, endpoint, endpoint));
            DateTimeOffset? symbolNext = string.IsNullOrWhiteSpace(symbol)
                ? null
                : TryGetNext(SymbolKey(host, endpoint, endpoint, symbol));
            return Max(hostNext, endpointNext, symbolNext);
        }
    }

    public static bool IsTransientBlockError(string error)
        => error.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase)
           || error.Contains("RemoteDisconnected", StringComparison.OrdinalIgnoreCase)
           || error.Contains("server closed", StringComparison.OrdinalIgnoreCase)
           || error.Contains("curl_exit=56", StringComparison.OrdinalIgnoreCase)
           || error.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
           || error.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
           || error.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
           || error.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase);

    private bool TryAcquire(
        string lane,
        string host,
        string endpoint,
        string? symbol,
        TimeSpan hostMinInterval,
        TimeSpan hostMaxInterval,
        TimeSpan symbolMinInterval,
        TimeSpan symbolMaxInterval,
        NextAllowedReason reason,
        DateTimeOffset now,
        bool countsAgainstNonQuoteBudget,
        out MarketRequestDecision decision)
    {
        if (countsAgainstNonQuoteBudget && _nonQuoteRequestsThisTick >= 1)
        {
            decision = new MarketRequestDecision(false, "non_quote_tick_budget_exhausted");
            return false;
        }

        DateTimeOffset? nextAllowed = NextAllowedAt(lane, host, endpoint, symbol);
        if (nextAllowed.HasValue && now < nextAllowed.Value)
        {
            decision = new MarketRequestDecision(false, "rate_limited", nextAllowed);
            return false;
        }

        DateTimeOffset hostNext = now.Add(RandomInterval(hostMinInterval, hostMaxInterval));
        DateTimeOffset symbolNext = now.Add(RandomInterval(symbolMinInterval, symbolMaxInterval));
        SetNextAllowed(HostKey(host, lane), hostNext, reason);
        SetNextAllowed(EndpointKey(host, lane, endpoint), hostNext, reason);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            SetNextAllowed(SymbolKey(host, lane, endpoint, symbol), symbolNext, reason);
        }

        if (countsAgainstNonQuoteBudget)
        {
            _nonQuoteRequestsThisTick++;
        }

        decision = new MarketRequestDecision(true, "allowed", Max(hostNext, symbolNext));
        return true;
    }

    private DateTimeOffset? RecordFailure(string lane, string host, string endpoint, string? symbol, string error, DateTimeOffset now)
    {
        if (!IsTransientBlockError(error))
        {
            return null;
        }

        string key = FailureKey(host, lane, endpoint, symbol);
        if (!_failures.TryGetValue(key, out FailureState? state))
        {
            state = new FailureState();
            _failures[key] = state;
        }

        state.Count++;
        TimeSpan cooldown = state.Count <= 1
            ? BlockedInitialCooldown
            : state.Count == 2
                ? BlockedSecondCooldown
                : BlockedMaxCooldown;
        DateTimeOffset until = now.Add(cooldown);
        SetNextAllowed(HostKey(host, lane), until, NextAllowedReason.FailureCooldown);
        SetNextAllowed(EndpointKey(host, lane, endpoint), until, NextAllowedReason.FailureCooldown);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            SetNextAllowed(SymbolKey(host, lane, endpoint, symbol), until, NextAllowedReason.FailureCooldown);
        }

        return until;
    }

    private void ClearFailure(string lane, string host, string endpoint, string? symbol)
    {
        _failures.Remove(FailureKey(host, lane, endpoint, symbol));
    }

    private DateTimeOffset? NextAllowedAt(string lane, string host, string endpoint, string? symbol)
    {
        DateTimeOffset? hostNext = TryGetNext(HostKey(host, lane));
        DateTimeOffset? endpointNext = TryGetNext(EndpointKey(host, lane, endpoint));
        DateTimeOffset? symbolNext = string.IsNullOrWhiteSpace(symbol)
            ? null
            : TryGetNext(SymbolKey(host, lane, endpoint, symbol));
        return Max(hostNext, endpointNext, symbolNext);
    }

    // LOCKED: Accepted index quote lanes. Keep IndexQuote quote/ulist.np isolated from trends2 and push2his lanes.
    private static MarketRequestProfile BuildProfile(MarketRequestKind kind, DateTimeOffset now)
        => kind switch
        {
            MarketRequestKind.EtfQuote => IsAShareTrading(now.LocalDateTime)
                ? SameInterval("quote", "qt.gtimg.cn", "qt", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), false, NextAllowedReason.TradingInterval)
                : SameInterval("quote", "qt.gtimg.cn", "qt", TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), false, NextAllowedReason.NonTradingInterval),
            MarketRequestKind.IndexQuote => IsUsEasternTrading(now)
                ? SameInterval("quote", "push2.eastmoney.com", "ulist.np", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), false, NextAllowedReason.TradingInterval)
                : SameInterval("quote", "push2.eastmoney.com", "ulist.np", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), false, NextAllowedReason.NonTradingInterval),
            MarketRequestKind.SinaFundNav => IsSinaNavWindow(now.LocalDateTime)
                ? SameInterval("fund-nav", "hq.sinajs.cn", "fund_nav", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), false, NextAllowedReason.TradingInterval)
                : SameInterval("fund-nav", "hq.sinajs.cn", "fund_nav", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), false, NextAllowedReason.NonTradingInterval),
            MarketRequestKind.EtfIntradayActive => SplitInterval(
                "intraday",
                "web.ifzq.gtimg.cn",
                "minute/query",
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                true,
                NextAllowedReason.TradingInterval),
            MarketRequestKind.EtfIntradayBackground => SplitInterval(
                "intraday",
                "web.ifzq.gtimg.cn",
                "minute/query",
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60),
                true,
                NextAllowedReason.TradingInterval),
            MarketRequestKind.IndexIntraday => SameInterval("intraday", "push2.eastmoney.com", "trends2", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), true, NextAllowedReason.TradingInterval),
            MarketRequestKind.EtfDailyKLine => SplitInterval(
                "history",
                "web.ifzq.gtimg.cn",
                "fqkline/get",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(1),
                true,
                NextAllowedReason.TradingInterval),
            MarketRequestKind.IndexDailyHistory => SameInterval("history", "push2his.eastmoney.com", "kline/get", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), true, NextAllowedReason.TradingInterval),
            MarketRequestKind.EastMoneyHistoryVariant => SameInterval("history", "push2his.eastmoney.com", "kline/variant", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), false, NextAllowedReason.TradingInterval),
            _ => SameInterval("default", "unknown", "unknown", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), true, NextAllowedReason.TradingInterval)
        };

    private static MarketRequestProfile SameInterval(
        string lane,
        string host,
        string endpoint,
        TimeSpan minInterval,
        TimeSpan maxInterval,
        bool countsAgainstNonQuoteBudget,
        NextAllowedReason reason)
        => SplitInterval(lane, host, endpoint, minInterval, maxInterval, minInterval, maxInterval, countsAgainstNonQuoteBudget, reason);

    private static MarketRequestProfile SplitInterval(
        string lane,
        string host,
        string endpoint,
        TimeSpan hostMinInterval,
        TimeSpan hostMaxInterval,
        TimeSpan symbolMinInterval,
        TimeSpan symbolMaxInterval,
        bool countsAgainstNonQuoteBudget,
        NextAllowedReason reason)
        => new(
            lane,
            host,
            endpoint,
            hostMinInterval,
            hostMaxInterval,
            symbolMinInterval,
            symbolMaxInterval,
            countsAgainstNonQuoteBudget,
            reason);

    private static bool IsAShareTrading(DateTime time)
        => time.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
           && CrossETF.Terminal.UiShell.Reference.Core.Services.IntradayTradingTimeAxis.IsTradingTime(time);

    private static bool IsSinaNavWindow(DateTime time)
    {
        TimeSpan value = time.TimeOfDay;
        return value >= new TimeSpan(19, 0, 0) && value <= new TimeSpan(21, 30, 0);
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

        TimeSpan value = eastern.TimeOfDay;
        return value >= new TimeSpan(9, 30, 0) && value <= new TimeSpan(16, 0, 0);
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

    private TimeSpan RandomInterval(TimeSpan minInterval, TimeSpan maxInterval)
    {
        if (maxInterval <= minInterval)
        {
            return minInterval;
        }

        int minMs = (int)Math.Min(int.MaxValue, minInterval.TotalMilliseconds);
        int maxMs = (int)Math.Min(int.MaxValue, maxInterval.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(_random.Next(minMs, maxMs + 1));
    }

    private DateTimeOffset? TryGetNext(string key)
        => _nextAllowedAt.TryGetValue(key, out DateTimeOffset value) ? value : null;

    private void SetNextAllowed(string key, DateTimeOffset value, NextAllowedReason reason)
    {
        _nextAllowedAt[key] = value;
        _nextAllowedReasons[key] = reason;
    }

    private void ReleaseNonTradingThrottle(MarketRequestProfile profile, string? symbol)
    {
        RemoveIfNonTradingThrottle(HostKey(profile.Host, profile.Lane));
        RemoveIfNonTradingThrottle(EndpointKey(profile.Host, profile.Lane, profile.Endpoint));
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            RemoveIfNonTradingThrottle(SymbolKey(profile.Host, profile.Lane, profile.Endpoint, symbol));
        }
    }

    private void RemoveIfNonTradingThrottle(string key)
    {
        if (_nextAllowedReasons.TryGetValue(key, out NextAllowedReason reason)
            && reason == NextAllowedReason.NonTradingInterval)
        {
            _nextAllowedAt.Remove(key);
            _nextAllowedReasons.Remove(key);
        }
    }

    private static DateTimeOffset? Max(params DateTimeOffset?[] values)
    {
        DateTimeOffset? result = null;
        foreach (DateTimeOffset? value in values)
        {
            if (value.HasValue && (!result.HasValue || value.Value > result.Value))
            {
                result = value;
            }
        }

        return result;
    }

    private static string HostKey(string host, string lane)
        => "host-lane:" + host + ":" + lane;

    private static string EndpointKey(string host, string lane, string endpoint)
        => "endpoint:" + host + ":" + lane + ":" + endpoint;

    private static string SymbolKey(string host, string lane, string endpoint, string symbol)
        => "symbol:" + host + ":" + lane + ":" + endpoint + ":" + symbol;

    private static string FailureKey(string host, string lane, string endpoint, string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? EndpointKey(host, lane, endpoint)
            : SymbolKey(host, lane, endpoint, symbol);

    private sealed record MarketRequestProfile(
        string Lane,
        string Host,
        string Endpoint,
        TimeSpan HostMinInterval,
        TimeSpan HostMaxInterval,
        TimeSpan SymbolMinInterval,
        TimeSpan SymbolMaxInterval,
        bool CountsAgainstNonQuoteBudget,
        NextAllowedReason Reason);

    private enum NextAllowedReason
    {
        TradingInterval,
        NonTradingInterval,
        FailureCooldown
    }

    private sealed class FailureState
    {
        public int Count { get; set; }
    }
}
