using System.Windows;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public sealed class ChartWindowManager
{
    private readonly Window _owner;
    private readonly ChartSubscriptionService _subscriptions;
    private readonly ChartDataRefreshCoordinator _coordinator;
    private readonly Func<IReadOnlyList<TradeLogRecord>> _tradeLogsProvider;
    private readonly Func<IReadOnlyList<StrategyConfigRecord>> _strategiesProvider;
    private readonly CancellationToken _applicationToken;
    private readonly Dictionary<string, SecurityChartWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChartWindowRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public ChartWindowManager(Window owner, ChartSubscriptionService subscriptions, ChartDataRefreshCoordinator coordinator)
        : this(
            owner,
            subscriptions,
            coordinator,
            () => Array.Empty<TradeLogRecord>(),
            () => Array.Empty<StrategyConfigRecord>(),
            default)
    {
    }

    public ChartWindowManager(
        Window owner,
        ChartSubscriptionService subscriptions,
        ChartDataRefreshCoordinator coordinator,
        Func<IReadOnlyList<TradeLogRecord>> tradeLogsProvider,
        Func<IReadOnlyList<StrategyConfigRecord>> strategiesProvider,
        CancellationToken applicationToken = default)
    {
        _owner = owner;
        _subscriptions = subscriptions;
        _coordinator = coordinator;
        _tradeLogsProvider = tradeLogsProvider;
        _strategiesProvider = strategiesProvider;
        _applicationToken = applicationToken;
        _coordinator.SnapshotUpdated += (_, snapshot) => UpdateWindow(snapshot);
    }

    public int OpenWindowCount => _windows.Count;

    public void OpenOrActivate(ChartSecurityInfo security)
    {
        string key = ChartSubscriptionService.NormalizeKey(security.StrategyCode);
        if (key.Length == 0)
        {
            return;
        }

        if (_windows.TryGetValue(key, out SecurityChartWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            UpdateDisplayContext(existing);
            CancellationToken lifetimeToken = _registrations.TryGetValue(key, out ChartWindowRegistration? existingRegistration)
                ? existingRegistration.Lifetime.Token
                : default;
            _coordinator.PublishCachedOrBuild(_subscriptions.Subscribe(security, existing.Period, existing.SubPanel, lifetimeToken));
            return;
        }

        var lifetime = new ChartWindowLifetime(_applicationToken);
        var window = new SecurityChartWindow(security)
        {
            Owner = _owner
        };
        ChartWindowRegistration? registration = null;
        EventHandler<SecurityChartPeriodChangedEventArgs> periodChangedHandler = (_, args) =>
        {
            _subscriptions.UpdatePeriod(security.StrategyCode, args.Period, args.SubPanel);
            _coordinator.PublishCachedOrBuild(_subscriptions.Subscribe(security, args.Period, args.SubPanel, lifetime.Token));
        };
        EventHandler closedHandler = (_, _) =>
        {
            if (registration is not null)
            {
                RemoveWindow(key, registration);
            }
        };
        registration = new ChartWindowRegistration(window, lifetime, periodChangedHandler, closedHandler);
        window.PeriodChanged += periodChangedHandler;
        window.Closed += closedHandler;
        _windows[key] = window;
        _registrations[key] = registration;
        UpdateDisplayContext(window);
        ChartSubscription subscription = _subscriptions.Subscribe(security, window.Period, window.SubPanel, lifetime.Token);
        window.Show();
        _coordinator.PublishCachedOrBuild(subscription);
    }

    private void UpdateWindow(SecurityChartSnapshot snapshot)
    {
        string key = ChartSubscriptionService.NormalizeKey(snapshot.Security.StrategyCode);
        if (_windows.TryGetValue(key, out SecurityChartWindow? window))
        {
            if (window.IsClosed)
            {
                RemoveWindow(key, window);
                return;
            }

            if (window.Dispatcher.CheckAccess())
            {
                UpdateDisplayContext(window);
                window.UpdateSnapshot(snapshot);
            }
            else
            {
                try
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_windows.TryGetValue(key, out SecurityChartWindow? current)
                            && ReferenceEquals(current, window)
                            && !current.IsClosed)
                        {
                            UpdateDisplayContext(current);
                            current.UpdateSnapshot(snapshot);
                        }
                    }));
                }
                catch (ObjectDisposedException)
                {
                    RemoveWindow(key, window);
                }
                catch (InvalidOperationException)
                {
                    RemoveWindow(key, window);
                }
            }
        }
    }

    private void RemoveWindow(string key, SecurityChartWindow window)
    {
        if (_registrations.TryGetValue(key, out ChartWindowRegistration? registration)
            && ReferenceEquals(registration.Window, window))
        {
            RemoveWindow(key, registration);
        }
    }

    private void RemoveWindow(string key, ChartWindowRegistration registration)
    {
        if (!_windows.TryGetValue(key, out SecurityChartWindow? current)
            || !ReferenceEquals(current, registration.Window))
        {
            return;
        }

        registration.Lifetime.Cancel();
        _subscriptions.Unsubscribe(key);
        _windows.Remove(key);
        _registrations.Remove(key);
        registration.Lifetime.Dispose();
        registration.Window.PeriodChanged -= registration.PeriodChangedHandler;
        registration.Window.Closed -= registration.ClosedHandler;
    }

    private void UpdateDisplayContext(SecurityChartWindow window)
        => window.UpdateTradeContext(_tradeLogsProvider(), _strategiesProvider());

    private sealed record ChartWindowRegistration(
        SecurityChartWindow Window,
        ChartWindowLifetime Lifetime,
        EventHandler<SecurityChartPeriodChangedEventArgs> PeriodChangedHandler,
        EventHandler ClosedHandler);
}
