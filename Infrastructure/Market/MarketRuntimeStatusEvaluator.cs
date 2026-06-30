using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public enum MarketRuntimeConnectionState
{
    Connected,
    Partial,
    MarketError,
    NotConfigured
}

public sealed record MarketRuntimeStatusEvaluation(
    MarketRuntimeConnectionState State,
    string? LastSuccessAt,
    string? Error,
    bool HistoryFailureIgnoredByValidCache);

public static class MarketRuntimeStatusEvaluator
{
    public static MarketRuntimeStatusEvaluation Evaluate(
        IReadOnlyList<MarketSourceStatusRecord> statuses,
        bool localConfigured,
        bool hasValidCoreHistoryCache)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        MarketSourceStatusRecord[] realtimeStatuses = statuses
            .Where(status => IsRealtimeSource(status.Source))
            .ToArray();
        MarketSourceStatusRecord[] statusScope = realtimeStatuses.Length > 0
            ? realtimeStatuses
            : statuses.ToArray();

        int realtimeSuccessCount = statusScope.Count(IsOk);
        int realtimeFailureCount = statusScope.Count(IsFailure);
        bool historyFailure = statuses.Any(status => string.Equals(status.Source, MarketSources.EastMoneyHistory, StringComparison.OrdinalIgnoreCase)
                                                     && IsFailure(status));
        bool ignoreHistoryFailure = historyFailure && hasValidCoreHistoryCache;
        string? lastSuccess = statuses
            .Select(status => status.LastSuccessAt)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value, StringComparer.Ordinal)
            .FirstOrDefault();

        if (realtimeSuccessCount > 0 && realtimeFailureCount == 0 && (!historyFailure || ignoreHistoryFailure))
        {
            return new MarketRuntimeStatusEvaluation(MarketRuntimeConnectionState.Connected, lastSuccess, null, ignoreHistoryFailure);
        }

        if (realtimeSuccessCount > 0)
        {
            return new MarketRuntimeStatusEvaluation(
                MarketRuntimeConnectionState.Partial,
                lastSuccess,
                FirstError(statuses),
                ignoreHistoryFailure);
        }

        if (realtimeFailureCount > 0 || (historyFailure && !ignoreHistoryFailure))
        {
            return new MarketRuntimeStatusEvaluation(
                MarketRuntimeConnectionState.MarketError,
                lastSuccess,
                FirstError(statuses),
                ignoreHistoryFailure);
        }

        return new MarketRuntimeStatusEvaluation(
            MarketRuntimeConnectionState.NotConfigured,
            lastSuccess,
            localConfigured ? null : "no market source status",
            ignoreHistoryFailure);
    }

    private static bool IsRealtimeSource(string source)
        => string.Equals(source, MarketSources.Tencent, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, MarketSources.EastMoney, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, MarketSources.SinaFund, StringComparison.OrdinalIgnoreCase);

    private static bool IsOk(MarketSourceStatusRecord status)
        => string.Equals(status.Status, "OK", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailure(MarketSourceStatusRecord status)
        => string.Equals(status.Status, "ERROR", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "COOLDOWN", StringComparison.OrdinalIgnoreCase);

    private static string? FirstError(IEnumerable<MarketSourceStatusRecord> statuses)
        => statuses.Select(status => status.LastError)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
