using System.Collections.ObjectModel;
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

public partial class T1T6ChartCenterWindow : Window
{
    private static readonly Color WindowBackgroundColor = Color.FromRgb(0x05, 0x0B, 0x14);

    public static readonly TimeSpan LocalRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly LocalDataRepository _repository;
    private readonly Action<T1T6ChartOpenRequest> _openChart;
    private readonly T1T6ChartCenterSnapshotBuilder _snapshotBuilder = new();
    private readonly ObservableCollection<T1T6StrategyRow> _strategyRows = new();
    private readonly DispatcherTimer _localRefreshTimer;
    private readonly WindowWhiteFlashGuard _whiteFlashGuard;
    private T1T6ChartCenterReadModel _lastReadModel;
    private T1T6ChartCenterSnapshot _lastSnapshot;
    private bool _applyingSnapshot;
    private bool _closed;

    public T1T6ChartCenterWindow(
        LocalDataRepository repository,
        Action<T1T6ChartOpenRequest> openChart,
        T1T6ChartCenterReadModel initialReadModel,
        T1T6ChartCenterSnapshot initialSnapshot)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _openChart = openChart ?? throw new ArgumentNullException(nameof(openChart));
        _lastReadModel = initialReadModel ?? throw new ArgumentNullException(nameof(initialReadModel));
        _lastSnapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));

        InitializeComponent();
        _whiteFlashGuard = WindowWhiteFlashGuard.Attach(this, WindowBackgroundColor);
        SourceInitialized += T1T6ChartCenterWindow_SourceInitialized;
        StrategyList.ItemsSource = _strategyRows;
        ApplySnapshot(initialSnapshot, preserveScroll: false);

        _localRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalRefreshInterval
        };
        _localRefreshTimer.Tick += LocalRefreshTimer_Tick;
    }

    private void T1T6ChartCenterWindow_SourceInitialized(object? sender, EventArgs e)
        => TryApplyDarkTitleBar();

    private void T1T6ChartCenterWindow_Loaded(object sender, RoutedEventArgs e)
        => _localRefreshTimer.Start();

    private void T1T6ChartCenterWindow_Closed(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _localRefreshTimer.Stop();
        _localRefreshTimer.Tick -= LocalRefreshTimer_Tick;
        SourceInitialized -= T1T6ChartCenterWindow_SourceInitialized;
        Loaded -= T1T6ChartCenterWindow_Loaded;
        Closed -= T1T6ChartCenterWindow_Closed;
    }

    private void LocalRefreshTimer_Tick(object? sender, EventArgs e)
    {
        T1T6ChartCenterReadModel readModel = _repository.ReadT1T6ChartCenterReadModel();
        if (!string.IsNullOrWhiteSpace(readModel.ReadError))
        {
            LocalReadStatusText.Text = "本地读取失败";
            LocalReadStatusText.ToolTip = readModel.ReadError;
            LocalReadStatusText.Foreground = BrushFrom("#FF5D68");
            return;
        }

        string? selectedStrategyCode = (StrategyList.SelectedItem as T1T6StrategyRow)?.StrategyCode
                                       ?? _lastSnapshot.SelectedStrategyCode;
        int fallbackSelectionIndex = Math.Max(StrategyList.SelectedIndex, 0);
        T1T6ChartCenterSnapshot snapshot = _snapshotBuilder.Build(
            readModel,
            DateTimeOffset.Now,
            selectedStrategyCode,
            fallbackSelectionIndex);
        _lastReadModel = readModel;
        ApplySnapshot(snapshot, preserveScroll: true);
    }

    private void StrategyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSnapshot || StrategyList.SelectedItem is not T1T6StrategyRow selected)
        {
            return;
        }

        T1T6ChartCenterSnapshot snapshot = _snapshotBuilder.Build(
            _lastReadModel,
            DateTimeOffset.Now,
            selected.StrategyCode,
            StrategyList.SelectedIndex);
        ApplySnapshot(snapshot, preserveScroll: true);
    }

    private void OpenChartButton_Click(object sender, RoutedEventArgs e)
    {
        T1T6ChartOpenRequest? request = T1T6ChartCenterSnapshotBuilder.BuildChartOpenRequest(_lastSnapshot.SelectedRow);
        if (request is null)
        {
            return;
        }

        try
        {
            _openChart(request);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "打开图表失败：" + ex.Message,
                "T1-T6看图中心",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplySnapshot(T1T6ChartCenterSnapshot snapshot, bool preserveScroll)
    {
        _applyingSnapshot = true;
        try
        {
            ScrollViewer? strategyScrollViewer = preserveScroll ? FindVisualChild<ScrollViewer>(StrategyList) : null;
            double verticalOffset = strategyScrollViewer?.VerticalOffset ?? 0;
            SynchronizeRows(snapshot.Rows);
            _lastSnapshot = snapshot;
            DataContext = snapshot;

            T1T6StrategyRow? selected = snapshot.SelectedRow is null
                ? null
                : _strategyRows.FirstOrDefault(row => row.StrategyConfigId == snapshot.SelectedRow.StrategyConfigId);
            StrategyList.SelectedItem = selected;
            LocalReadStatusText.Text = snapshot.ReadStatusText;
            LocalReadStatusText.ToolTip = snapshot.ReadError;
            LocalReadStatusText.Foreground = BrushFrom(string.IsNullOrWhiteSpace(snapshot.ReadError) ? "#91A9BB" : "#FF5D68");

            if (strategyScrollViewer is not null)
            {
                strategyScrollViewer.ScrollToVerticalOffset(verticalOffset);
            }
        }
        finally
        {
            _applyingSnapshot = false;
        }
    }

    private void SynchronizeRows(IReadOnlyList<T1T6StrategyRow> rows)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            T1T6StrategyRow desired = rows[index];
            int existingIndex = IndexOfStrategyConfig(desired.StrategyConfigId, index);
            if (existingIndex < 0)
            {
                _strategyRows.Insert(index, desired);
                continue;
            }

            if (existingIndex != index)
            {
                _strategyRows.Move(existingIndex, index);
            }

            _strategyRows[index] = desired;
        }

        while (_strategyRows.Count > rows.Count)
        {
            _strategyRows.RemoveAt(_strategyRows.Count - 1);
        }
    }

    private int IndexOfStrategyConfig(long strategyConfigId, int startIndex)
    {
        for (int index = startIndex; index < _strategyRows.Count; index++)
        {
            if (_strategyRows[index].StrategyConfigId == strategyConfigId)
            {
                return index;
            }
        }

        return -1;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result)
            {
                return result;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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
