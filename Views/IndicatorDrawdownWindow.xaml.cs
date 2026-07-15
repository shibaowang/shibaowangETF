using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class IndicatorDrawdownWindow : Window
{
    private static readonly Color IndicatorWindowBackgroundColor = Color.FromRgb(0x05, 0x0B, 0x14);
    public static readonly TimeSpan LocalRefreshInterval = TimeSpan.FromSeconds(2);
    public const int HistoryMetadataCheckTickInterval = 15;

    private readonly LocalDataRepository _repository;
    private readonly IndicatorDrawdownSnapshotBuilder _snapshotBuilder = new();
    private readonly IndicatorDrawdownMetricsBuilder _metricsBuilder = new();
    private readonly DispatcherTimer _localRefreshTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private IndicatorDrawdownSnapshot? _lastSnapshot;
    private Dictionary<string, string> _historyMetadataSignatures = new(StringComparer.OrdinalIgnoreCase);
    private string _activeFilter = IndicatorDrawdownSnapshotBuilder.AllFilter;
    private int _refreshTick;
    private bool _historyRefreshRunning;
    private bool _closed;

    public IndicatorDrawdownWindow(LocalDataRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        SourceInitialized += IndicatorDrawdownWindow_SourceInitialized;
        _localRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalRefreshInterval
        };
        _localRefreshTimer.Tick += LocalRefreshTimer_Tick;
        LoadInitialSnapshotBeforeShow();
    }

    private void IndicatorDrawdownWindow_SourceInitialized(object? sender, EventArgs e)
    {
        TryApplyDarkTitleBar();
        ApplyDarkHwndBackground();
    }

    private void IndicatorDrawdownWindow_Loaded(object sender, RoutedEventArgs e)
        => _localRefreshTimer.Start();

    private void IndicatorDrawdownWindow_Closed(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _localRefreshTimer.Stop();
        _localRefreshTimer.Tick -= LocalRefreshTimer_Tick;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        SourceInitialized -= IndicatorDrawdownWindow_SourceInitialized;
        Loaded -= IndicatorDrawdownWindow_Loaded;
        Closed -= IndicatorDrawdownWindow_Closed;
    }

    private void LoadInitialSnapshotBeforeShow()
    {
        IndicatorDrawdownReadModel model = _repository.ReadIndicatorDrawdownReadModel();
        if (!string.IsNullOrWhiteSpace(model.ReadError))
        {
            DateTimeOffset failedAt = model.ReadAt == default ? DateTimeOffset.Now : model.ReadAt;
            _lastSnapshot = new IndicatorDrawdownSnapshot
            {
                GeneratedAt = failedAt,
                HistoryCheckedAt = failedAt
            };
            ApplySnapshot(_lastSnapshot);
            LocalReadStatusText.Foreground = BrushFrom("#FF5D68");
            LocalReadStatusText.Text = "本地只读快照读取失败";
            LocalReadStatusText.ToolTip = model.ReadError;
            HistoryCheckStatusText.Text = "历史签名检查 --";
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        _lastSnapshot = _snapshotBuilder.Build(model, now);
        _historyMetadataSignatures = BuildMetadataSignatureMap(model.Instruments, model.HistoryMetadata);
        ApplySnapshot(_lastSnapshot);
        LocalReadStatusText.Foreground = BrushFrom("#91A9BB");
        LocalReadStatusText.Text = $"本地快照 {_lastSnapshot.LocalReadTimeText}";
        LocalReadStatusText.ToolTip = null;
        HistoryCheckStatusText.Text = $"历史签名检查 {_lastSnapshot.HistoryCheckTimeText}";
    }

    private async void LocalRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        bool includeHistoryMetadata = ++_refreshTick % HistoryMetadataCheckTickInterval == 0;
        IndicatorDrawdownRealtimeReadModel realtime = _repository.ReadIndicatorDrawdownRealtimeState(includeHistoryMetadata);
        if (!string.IsNullOrWhiteSpace(realtime.ReadError) || _lastSnapshot is null)
        {
            LocalReadStatusText.Foreground = BrushFrom("#FF5D68");
            LocalReadStatusText.Text = "本地只读快照读取失败，保留上次结果";
            LocalReadStatusText.ToolTip = realtime.ReadError;
            return;
        }

        HashSet<string> previousKeys = _lastSnapshot.Rows
            .Select(row => row.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> validKeys = realtime.Instruments
            .Select(instrument => instrument.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IndicatorDrawdownInstrument[] addedInstruments = realtime.Instruments
            .Where(instrument => !previousKeys.Contains(instrument.Key))
            .ToArray();
        _lastSnapshot = _snapshotBuilder.RefreshRealtime(_lastSnapshot, realtime, DateTimeOffset.Now);
        ApplySnapshot(_lastSnapshot);
        LocalReadStatusText.Foreground = BrushFrom("#91A9BB");
        LocalReadStatusText.Text = $"本地快照 {_lastSnapshot.LocalReadTimeText}";
        LocalReadStatusText.ToolTip = null;
        if (!includeHistoryMetadata)
        {
            Dictionary<string, string> retainedMetadata = _historyMetadataSignatures
                .Where(item => validKeys.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            _historyMetadataSignatures = retainedMetadata;
            if (addedInstruments.Length > 0)
            {
                await RefreshChangedHistoryAsync(
                    addedInstruments,
                    realtime,
                    validKeys,
                    retainedMetadata).ConfigureAwait(true);
            }

            return;
        }

        Dictionary<string, string> nextMetadata = BuildMetadataSignatureMap(realtime.Instruments, realtime.HistoryMetadata);
        IndicatorDrawdownInstrument[] changed = realtime.Instruments
            .Where(instrument => !_historyMetadataSignatures.TryGetValue(instrument.Key, out string? previous)
                                 || !nextMetadata.TryGetValue(instrument.Key, out string? current)
                                 || !string.Equals(previous, current, StringComparison.Ordinal))
            .ToArray();

        if (changed.Length == 0)
        {
            _historyMetadataSignatures = nextMetadata;
            _lastSnapshot = _snapshotBuilder.ReplaceRows(
                _lastSnapshot,
                Array.Empty<IndicatorDrawdownRow>(),
                realtime.ReadAt,
                DateTimeOffset.Now,
                validKeys);
            ApplySnapshot(_lastSnapshot);
            HistoryCheckStatusText.Foreground = BrushFrom("#91A9BB");
            HistoryCheckStatusText.Text = $"历史签名检查 {_lastSnapshot.HistoryCheckTimeText}";
            return;
        }

        await RefreshChangedHistoryAsync(changed, realtime, validKeys, nextMetadata).ConfigureAwait(true);
    }

    private async Task RefreshChangedHistoryAsync(
        IReadOnlyCollection<IndicatorDrawdownInstrument> changed,
        IndicatorDrawdownRealtimeReadModel realtime,
        IReadOnlySet<string> validKeys,
        Dictionary<string, string> nextMetadata)
    {
        if (_historyRefreshRunning)
        {
            return;
        }

        _historyRefreshRunning = true;
        CancellationToken token = _lifetimeCancellation.Token;
        try
        {
            IndicatorDrawdownRow[] replacements = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<IndicatorDrawdownHistoryCandidate> history = _repository.ReadIndicatorDrawdownHistoryCandidates(changed);
                return changed.Select(instrument =>
                {
                    token.ThrowIfCancellationRequested();
                    return _metricsBuilder.Build(
                        instrument,
                        history,
                        realtime.Quotes,
                        realtime.SourceStatuses,
                        DateTimeOffset.Now);
                }).ToArray();
            }, token).ConfigureAwait(true);

            if (_closed || token.IsCancellationRequested || _lastSnapshot is null)
            {
                return;
            }

            _historyMetadataSignatures = nextMetadata;
            _lastSnapshot = _snapshotBuilder.ReplaceRows(
                _lastSnapshot,
                replacements,
                realtime.ReadAt,
                DateTimeOffset.Now,
                validKeys);
            ApplySnapshot(_lastSnapshot);
            HistoryCheckStatusText.Foreground = BrushFrom("#91A9BB");
            HistoryCheckStatusText.Text = $"历史签名检查 {_lastSnapshot.HistoryCheckTimeText}";
        }
        catch (OperationCanceledException)
        {
            // Window lifetime cancellation deliberately leaves the last immutable snapshot in place.
        }
        catch
        {
            if (!_closed)
            {
                HistoryCheckStatusText.Foreground = BrushFrom("#FF5D68");
                HistoryCheckStatusText.Text = "历史签名读取失败，保留上次结果";
            }
        }
        finally
        {
            _historyRefreshRunning = false;
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

    private void DrawdownGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateSelectedDetail(DrawdownGrid.SelectedItem as IndicatorDrawdownRow);

    private void ApplySnapshot(IndicatorDrawdownSnapshot snapshot)
    {
        string? selectedKey = (DrawdownGrid.SelectedItem as IndicatorDrawdownRow)?.Key;
        List<SortDescription> sortDescriptions = DrawdownGrid.Items.SortDescriptions.ToList();
        DataContext = snapshot;
        IReadOnlyList<IndicatorDrawdownRow> filtered = IndicatorDrawdownSnapshotBuilder.FilterRows(
            snapshot.Rows,
            _activeFilter,
            SearchTextBox.Text);
        DrawdownGrid.ItemsSource = filtered;
        foreach (SortDescription sortDescription in sortDescriptions)
        {
            DrawdownGrid.Items.SortDescriptions.Add(sortDescription);
        }

        IndicatorDrawdownRow? selected = filtered.FirstOrDefault(row => row.Key == selectedKey)
                                         ?? filtered.FirstOrDefault();
        DrawdownGrid.SelectedItem = selected;
        FilteredCountText.Text = $"当前显示 {filtered.Count} / {snapshot.TotalCount}";
        UpdateSelectedDetail(selected);
    }

    private void ApplyLocalFilter()
    {
        if (_lastSnapshot is null || DrawdownGrid is null || SearchTextBox is null)
        {
            return;
        }

        ApplySnapshot(_lastSnapshot);
    }

    private void UpdateSelectedDetail(IndicatorDrawdownRow? row)
    {
        if (row is null)
        {
            SelectedInstrumentText.Text = "--";
            SelectedCategoryText.Text = "--";
            SelectedStrategyText.Text = "--";
            SelectedHistorySourceText.Text = "--";
            SelectedQuoteSourceText.Text = "--";
            SelectedHistoryRangeText.Text = "--";
            SelectedHistoryPointCountText.Text = "--";
            SelectedHistoricalHighText.Text = "--";
            SelectedCurrentDrawdownText.Text = "--";
            SelectedMaximumDrawdownText.Text = "--";
            SelectedMaximumIntervalText.Text = "--";
            SelectedStatusText.Text = "--";
            SelectedStatusText.Foreground = (Brush)FindResource("IndicatorMutedBrush");
            SelectedStatusDetailText.Text = "请选择一行查看详情";
            return;
        }

        SelectedInstrumentText.Text = $"{row.Name}（{row.Code}）";
        SelectedCategoryText.Text = DisplayValue(row.Category);
        SelectedStrategyText.Text = DisplayValue(row.StrategyCodes);
        (string historySource, string quoteSource) = SplitDataSourceText(row.DataSourceText);
        SelectedHistorySourceText.Text = historySource;
        SelectedQuoteSourceText.Text = quoteSource;
        SelectedHistoryRangeText.Text = row.HistoryRangeText;
        SelectedHistoryPointCountText.Text = row.HistoryPointCountText;
        SelectedHistoricalHighText.Text = row.HistoricalHighCloseText == "--" && row.HistoricalHighDateText == "--"
            ? "--"
            : $"{row.HistoricalHighCloseText}（{row.HistoricalHighDateText}）";
        SelectedCurrentDrawdownText.Text = row.CurrentDrawdownText;
        SelectedMaximumDrawdownText.Text = row.MaximumDrawdownText;
        SelectedMaximumIntervalText.Text = row.MaximumDrawdownIntervalText;
        SelectedStatusText.Text = DisplayValue(row.DataStatus);
        SelectedStatusText.Foreground = ResolveStatusBrush(row.DataStatus);
        string note = string.IsNullOrWhiteSpace(row.HistorySelectionNote) ? row.DataStatusDetail : row.HistorySelectionNote + "；" + row.DataStatusDetail;
        SelectedStatusDetailText.Text = DisplayValue(note);
    }

    private static (string HistorySource, string QuoteSource) SplitDataSourceText(string dataSourceText)
    {
        string[] parts = dataSourceText.Split(" / ", 2, StringSplitOptions.TrimEntries);
        return (DisplayValue(parts.ElementAtOrDefault(0)), DisplayValue(parts.ElementAtOrDefault(1)));
    }

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private Brush ResolveStatusBrush(string status)
        => (Brush)FindResource(status switch
        {
            "正常" => "IndicatorNormalBrush",
            "数据不足" or "历史滞后" or "熔断冷却" or "限频" => "IndicatorWarningBrush",
            "数据源异常" or "实时行情缺失" or "无历史" or "数据损坏" => "IndicatorErrorBrush",
            _ => "IndicatorTextBrush"
        });

    private static Dictionary<string, string> BuildMetadataSignatureMap(
        IEnumerable<IndicatorDrawdownInstrument> instruments,
        IEnumerable<IndicatorDrawdownHistoryMetadata> metadata)
    {
        IndicatorDrawdownHistoryMetadata[] allMetadata = metadata.ToArray();
        return instruments.ToDictionary(
            instrument => instrument.Key,
            instrument => string.Join(";", allMetadata
                .Where(item => string.Equals(item.MarketType, instrument.MarketType, StringComparison.OrdinalIgnoreCase)
                               && SymbolsEqual(item.Symbol, instrument.Code, instrument.MarketType))
                .Select(item => item.MetadataSignature)
                .OrderBy(value => value, StringComparer.Ordinal)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool SymbolsEqual(string left, string right, string marketType)
    {
        string normalizedLeft = string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase)
            ? new string(left.Where(char.IsDigit).ToArray())
            : left.Trim();
        string normalizedRight = string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase)
            ? new string(right.Where(char.IsDigit).ToArray())
            : right.Trim();
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
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

    private void ApplyDarkHwndBackground()
    {
        try
        {
            if (PresentationSource.FromVisual(this) is HwndSource source
                && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = IndicatorWindowBackgroundColor;
            }
        }
        catch
        {
            // The deep client background remains in place if the HWND background cannot be changed.
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
