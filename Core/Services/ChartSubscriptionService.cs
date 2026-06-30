using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class ChartSubscriptionService
{
    private readonly Dictionary<string, ChartSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ChartSubscription> ActiveSubscriptions
    {
        get
        {
            lock (_subscriptions)
            {
                return _subscriptions.Values.ToArray();
            }
        }
    }

    public int ActiveSymbolCount
    {
        get
        {
            lock (_subscriptions)
            {
                return _subscriptions.Count;
            }
        }
    }

    public ChartSubscription Subscribe(ChartSecurityInfo security, SecurityChartPeriod period, SecurityChartSubPanel subPanel)
    {
        string key = NormalizeKey(security.StrategyCode);
        var subscription = new ChartSubscription(key, security, period, subPanel, DateTimeOffset.Now);
        lock (_subscriptions)
        {
            _subscriptions[key] = subscription;
        }

        return subscription;
    }

    public void UpdatePeriod(string strategyCode, SecurityChartPeriod period, SecurityChartSubPanel subPanel)
    {
        string key = NormalizeKey(strategyCode);
        lock (_subscriptions)
        {
            if (_subscriptions.TryGetValue(key, out ChartSubscription? current))
            {
                _subscriptions[key] = current with { Period = period, SubPanel = subPanel };
            }
        }
    }

    public void Unsubscribe(string strategyCode)
    {
        string key = NormalizeKey(strategyCode);
        lock (_subscriptions)
        {
            _subscriptions.Remove(key);
        }
    }

    public static string NormalizeKey(string? strategyCode)
        => string.IsNullOrWhiteSpace(strategyCode) ? string.Empty : strategyCode.Trim();
}

public sealed record ChartSubscription(
    string Key,
    ChartSecurityInfo Security,
    SecurityChartPeriod Period,
    SecurityChartSubPanel SubPanel,
    DateTimeOffset SubscribedAt);
