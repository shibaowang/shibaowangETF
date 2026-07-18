using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class CapitalPositionWindow : Window
{
    private static readonly Color CapitalWindowBackgroundColor = Color.FromRgb(0x05, 0x0B, 0x14);

    public static readonly TimeSpan LocalRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly LocalDataRepository _repository;
    private readonly CapitalPositionSnapshotBuilder _snapshotBuilder = new();
    private readonly DispatcherTimer _localRefreshTimer;
    private readonly WindowWhiteFlashGuard _whiteFlashGuard;
    private CapitalPositionSnapshot? _lastSnapshot;
    private bool _closed;

    public CapitalPositionWindow(LocalDataRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        _whiteFlashGuard = WindowWhiteFlashGuard.Attach(this, CapitalWindowBackgroundColor);
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

    private void CapitalPositionWindow_Loaded(object sender, RoutedEventArgs e)
        => _localRefreshTimer.Start();

    private void CapitalPositionWindow_Closed(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _localRefreshTimer.Stop();
        _localRefreshTimer.Tick -= LocalRefreshTimer_Tick;
        Loaded -= CapitalPositionWindow_Loaded;
        Closed -= CapitalPositionWindow_Closed;
    }

    private void LocalRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!_closed)
        {
            ReloadLocalSnapshot();
        }
    }

    private void ReloadLocalSnapshot()
    {
        try
        {
            CapitalPositionReadModel readModel = _repository.ReadCapitalPositionReadModel();
            CapitalPositionSnapshot snapshot = _snapshotBuilder.Build(readModel, DateTimeOffset.Now);
            ApplySnapshot(snapshot);
            LocalReadStatusText.Foreground = BrushFrom("#91A9BB");
            LocalReadStatusText.Text = snapshot.HasAccount ? "本地只读快照正常" : "暂无账户回放结果";
            LocalReadStatusText.ToolTip = null;
        }
        catch (Exception ex)
        {
            LocalReadStatusText.Foreground = BrushFrom("#FF5D68");
            LocalReadStatusText.Text = "本地读取失败（保留上次成功数据）";
            LocalReadStatusText.ToolTip = ex.Message;
        }
    }

    private void ApplySnapshot(CapitalPositionSnapshot snapshot)
    {
        string? selectedEtfKey = (EtfPositionGrid.SelectedItem as CapitalPositionEtfRow) is { } etf
            ? etf.StrategyCode + "|" + etf.ActualCode
            : null;
        string? selectedOtcKey = (OtcPositionGrid.SelectedItem as CapitalPositionOtcRow) is { } otc
            ? otc.StrategyCode + "|" + otc.FundCode
            : null;

        if (_lastSnapshot is null
            || !string.Equals(_lastSnapshot.SnapshotKey, snapshot.SnapshotKey, StringComparison.Ordinal))
        {
            DataContext = snapshot;
            StrategyAllocationGrid.ItemsSource = snapshot.StrategyRows;
            EtfPositionGrid.ItemsSource = snapshot.EtfRows;
            OtcPositionGrid.ItemsSource = snapshot.OtcRows;
            _lastSnapshot = snapshot;
            if (selectedEtfKey is not null)
            {
                EtfPositionGrid.SelectedItem = snapshot.EtfRows.FirstOrDefault(row => row.StrategyCode + "|" + row.ActualCode == selectedEtfKey);
            }

            if (selectedOtcKey is not null)
            {
                OtcPositionGrid.SelectedItem = snapshot.OtcRows.FirstOrDefault(row => row.StrategyCode + "|" + row.FundCode == selectedOtcKey);
            }
        }

        ReadAtText.Text = "本地读取时间：" + snapshot.ReadAtText;
        EtfEmptyText.Visibility = snapshot.HasEtfRows ? Visibility.Collapsed : Visibility.Visible;
        OtcEmptyText.Visibility = snapshot.HasOtcRows ? Visibility.Collapsed : Visibility.Visible;
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
