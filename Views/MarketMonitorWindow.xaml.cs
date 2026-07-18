using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class MarketMonitorWindow : Window
{
    private static readonly Color MarketMonitorWindowBackgroundColor = Color.FromRgb(0x05, 0x0B, 0x14);

    public static readonly TimeSpan LocalRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly LocalDataRepository _repository;
    private readonly MarketMonitorSnapshotBuilder _snapshotBuilder = new();
    private readonly DispatcherTimer _localRefreshTimer;
    private readonly WindowWhiteFlashGuard _whiteFlashGuard;
    private MarketMonitorSnapshot? _lastSnapshot;
    private string _activeFilter = MarketMonitorSnapshotBuilder.AllFilter;
    private bool _closed;

    public MarketMonitorWindow(LocalDataRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        _whiteFlashGuard = WindowWhiteFlashGuard.Attach(this, MarketMonitorWindowBackgroundColor);
        SourceInitialized += (_, _) =>
        {
            TryApplyDarkTitleBar();
        };
        _localRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalRefreshInterval
        };
        _localRefreshTimer.Tick += LocalRefreshTimer_Tick;
        ReloadLocalSnapshot();
    }

    private void MarketMonitorWindow_Loaded(object sender, RoutedEventArgs e)
        => _localRefreshTimer.Start();

    private void MarketMonitorWindow_Closed(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _localRefreshTimer.Stop();
        _localRefreshTimer.Tick -= LocalRefreshTimer_Tick;
        Loaded -= MarketMonitorWindow_Loaded;
        Closed -= MarketMonitorWindow_Closed;
    }

    private void LocalRefreshTimer_Tick(object? sender, EventArgs e)
        => ReloadLocalSnapshot();

    private void ReloadLocalSnapshot()
    {
        try
        {
            MarketMonitorSnapshot snapshot = _snapshotBuilder.Build(
                _repository.ReadStrategyConfigs(),
                _repository.ReadPositionStates(),
                _repository.ReadOtcChannels(),
                _repository.ReadMarketQuoteCache(),
                _repository.ReadMarketSourceStatuses(),
                DateTimeOffset.Now);

            _lastSnapshot = snapshot;
            DataContext = snapshot;
            SourceStatusGrid.ItemsSource = snapshot.SourceRows;
            ApplyLocalFilter();
            LocalReadStatusText.Foreground = BrushFrom("#91A9BB");
            LocalReadStatusText.Text = $"本地快照 {snapshot.GeneratedAt:HH:mm:ss}";
        }
        catch
        {
            LocalReadStatusText.Foreground = BrushFrom("#FF5D68");
            LocalReadStatusText.Text = "本地行情读取失败";
        }
    }

    private void FilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string filter })
        {
            _activeFilter = filter;
            ApplyLocalFilter();
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyLocalFilter();

    private void ApplyLocalFilter()
    {
        if (_lastSnapshot is null || QuoteGrid is null)
        {
            return;
        }

        IReadOnlyList<MarketMonitorQuoteRow> filtered = MarketMonitorSnapshotBuilder.FilterRows(
            _lastSnapshot.QuoteRows,
            _activeFilter,
            SearchTextBox?.Text);
        QuoteGrid.ItemsSource = filtered;
        FilteredCountText.Text = $"当前显示 {filtered.Count} / {_lastSnapshot.TotalCount}";
    }

    private void TryApplyDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, Marshal.SizeOf<int>());
            _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, Marshal.SizeOf<int>());
            int border = ToColorRef(Color.FromRgb(0x0B, 0x25, 0x38));
            _ = DwmSetWindowAttribute(hwnd, 34, ref border, Marshal.SizeOf<int>());
        }
        catch
        {
            // Unsupported DWM attributes leave the native frame unchanged.
        }
    }

    private static int ToColorRef(Color color)
        => color.R | (color.G << 8) | (color.B << 16);

    private static SolidColorBrush BrushFrom(string value)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
