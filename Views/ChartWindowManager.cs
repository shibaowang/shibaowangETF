using System.Windows;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public sealed class ChartWindowManager
{
    private readonly Window _owner;
    private readonly ChartSubscriptionService _subscriptions;
    private readonly ChartDataRefreshCoordinator _coordinator;
    private readonly Dictionary<string, SecurityChartWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    public ChartWindowManager(Window owner, ChartSubscriptionService subscriptions, ChartDataRefreshCoordinator coordinator)
    {
        _owner = owner;
        _subscriptions = subscriptions;
        _coordinator = coordinator;
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
            _coordinator.PublishCachedOrBuild(_subscriptions.Subscribe(security, existing.Period, existing.SubPanel));
            return;
        }

        var window = new SecurityChartWindow(security)
        {
            Owner = _owner
        };
        window.PeriodChanged += (_, args) =>
        {
            _subscriptions.UpdatePeriod(security.StrategyCode, args.Period, args.SubPanel);
            _coordinator.PublishCachedOrBuild(_subscriptions.Subscribe(security, args.Period, args.SubPanel));
        };
        window.Closed += (_, _) =>
        {
            _windows.Remove(key);
            _subscriptions.Unsubscribe(security.StrategyCode);
        };
        _windows[key] = window;
        ChartSubscription subscription = _subscriptions.Subscribe(security, window.Period, window.SubPanel);
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
                _windows.Remove(key);
                _subscriptions.Unsubscribe(snapshot.Security.StrategyCode);
                return;
            }

            if (window.Dispatcher.CheckAccess())
            {
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
                            current.UpdateSnapshot(snapshot);
                        }
                    }));
                }
                catch (ObjectDisposedException)
                {
                    _windows.Remove(key);
                    _subscriptions.Unsubscribe(snapshot.Security.StrategyCode);
                }
                catch (InvalidOperationException)
                {
                    _windows.Remove(key);
                    _subscriptions.Unsubscribe(snapshot.Security.StrategyCode);
                }
            }
        }
    }
}
