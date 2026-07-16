using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference;

public partial class MainWindow : Window
{
    private const double BaseDesignWidth = 2560;
    private const double BaseDesignHeight = 1440;
    private const double MaxExpandedDesignWidth = 2860;

    private static readonly Color Red = ColorFrom("#EF4444");
    private static readonly Color Green = ColorFrom("#84CC16");
    private static readonly Color Blue = ColorFrom("#3B82F6");
    private static readonly Color Orange = ColorFrom("#F59E0B");
    private static readonly Color Text = ColorFrom("#E5EEF8");
    private static readonly Color Muted = ColorFrom("#9CAFC3");
    private static readonly Color Border = ColorFrom("#18324A");
    private const double EtfHeaderDragThreshold = 6;
    private bool _refreshQueued;
    private int[] _etfColumnOrder = Array.Empty<int>();
    private IReadOnlyList<string> _visibleEtfColumnKeys = EtfDecisionColumnSettings.DefaultVisibleKeys;
    private IReadOnlyList<string> _pinnedEtfSymbols = Array.Empty<string>();
    private readonly Dictionary<string, CheckBox> _etfColumnChooserBoxes = new(StringComparer.OrdinalIgnoreCase);
    private int _etfDragSourceColumn = -1;
    private int _etfDragTargetColumn = -1;
    private bool _etfIsDragging;
    private Point _etfDragStart;
    private int _etfSortSourceColumn = -1;
    private bool _etfSortDescending;
    private readonly Dictionary<string, string> _etfOrderDraftTooltips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _etfCellValueSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _etfRowHasPosition = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _navigationBackgrounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _navigationLabels = new(StringComparer.Ordinal);
    private string? _selectedNavigationName;
    private string? _selectedEtfStrategyCode;
    private string? _lastPinnedFeedbackSymbol;
    private DateTimeOffset _lastPinnedFeedbackAt;
    private readonly LocalDatabase _database;
    private readonly LocalDataRepository _repository;
    private readonly GlobalMarketRequestScheduler _marketRequestScheduler = new();
    private readonly MarketDataRefreshService _marketRefreshService;
    private readonly AccountReplayService _accountReplayService = new();
    private readonly StrategyDecisionService _strategyDecisionService = new();
    private readonly OrderDraftService _orderDraftService = new();
    private readonly AlertRuleEvaluator _alertRuleEvaluator = new();
    private readonly PushPlusAlertSender _pushPlusAlertSender = new();
    private readonly VoiceAlertPlayer _voiceAlertPlayer = new();
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private readonly ChartSubscriptionService _chartSubscriptions = new();
    private readonly ChartCache _chartCache = new();
    private readonly ChartDataRefreshCoordinator _chartRefreshCoordinator;
    private readonly ChartWindowManager _chartWindowManager;
    private readonly CancellationTokenSource _marketRefreshCts = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly RuntimeHealthMonitor _runtimeHealthMonitor;
    private readonly Random _random = new();
    private IReadOnlyList<StrategyConfigRecord> _strategies = Array.Empty<StrategyConfigRecord>();
    private IReadOnlyList<PositionStateRecord> _positions = Array.Empty<PositionStateRecord>();
    private IReadOnlyList<OtcChannelRecord> _otcChannels = Array.Empty<OtcChannelRecord>();
    private IReadOnlyList<TradeLogRecord> _tradeLogs = Array.Empty<TradeLogRecord>();
    private IReadOnlyList<MarketQuoteRecord> _marketQuotes = Array.Empty<MarketQuoteRecord>();
    private IReadOnlyList<MarketQuoteRecord> _marketHistory = Array.Empty<MarketQuoteRecord>();
    private IReadOnlyList<MarketSourceStatusRecord> _marketStatuses = Array.Empty<MarketSourceStatusRecord>();
    private IReadOnlyList<AccountReplaySnapshotRecord> _accountReplaySnapshots = Array.Empty<AccountReplaySnapshotRecord>();
    private IReadOnlyList<PositionReplayStateRecord> _replayPositions = Array.Empty<PositionReplayStateRecord>();
    private IReadOnlyList<OtcPositionReplayStateRecord> _replayOtcPositions = Array.Empty<OtcPositionReplayStateRecord>();
    private IReadOnlyList<StrategyDecisionStateRecord> _strategyDecisions = Array.Empty<StrategyDecisionStateRecord>();
    private IReadOnlyList<OrderDraftStateRecord> _orderDrafts = Array.Empty<OrderDraftStateRecord>();
    private IReadOnlyList<OrderDraftLegStateRecord> _orderDraftLegs = Array.Empty<OrderDraftLegStateRecord>();
    private IReadOnlyList<OrderFinalizationStateRecord> _orderFinalizations = Array.Empty<OrderFinalizationStateRecord>();
    private IReadOnlyList<RuntimeLogRecord> _runtimeLogs = Array.Empty<RuntimeLogRecord>();
    private BasePositionSettings _basePositionSettings = BasePositionSettings.Default();
    private HotkeySettings _hotkeySettings = HotkeySettings.Default;
    private AlertSettings _alertSettings = AlertSettings.Default;
    private string _hotkeyRegistrationStatus = "未保存";
    private WindowState _windowStateBeforeHotkeyHide = WindowState.Normal;
    private AccountStateRecord? _accountState;
    private AccountReplayStateRecord? _accountReplayState;
    private string _runtimeStatus = "未配置：暂无策略或账户数据";
    private Color _runtimeStatusColor = Orange;
    private long _lastRefreshElapsedMs;
    private string? _lastAccountReplaySignature;
    private bool _accountReplayQueued;
    private string? _lastStrategyDecisionSignature;
    private bool _strategyDecisionQueued;
    private string? _lastOrderDraftSignature;
    private bool _orderDraftQueued;
    private bool _alertDeliveryQueued;
    private MarketMonitorWindow? _marketMonitorWindow;
    private IndicatorDrawdownWindow? _indicatorDrawdownWindow;
    private CapitalPositionWindow? _capitalPositionWindow;
    private T1T6ChartCenterWindow? _t1T6ChartCenterWindow;
    private RiskCenterWindow? _riskCenterWindow;
    private readonly Dictionary<string, DateTimeOffset> _strategyRuntimeLogLastAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _orderRuntimeLogLastAt = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        _database = new LocalDatabase();
        _repository = new LocalDataRepository(_database);
        if (Application.Current is App app)
        {
            app.CompleteDatabaseStartup(_repository);
        }

        _marketRefreshService = new MarketDataRefreshService(_repository, _marketRequestScheduler);
        _chartRefreshCoordinator = new ChartDataRefreshCoordinator(
            _chartSubscriptions,
            _chartCache,
            intradayCacheStore: _repository,
            historyCacheSaver: (symbol, marketType, highValue, rawPayload, source)
                => _repository.SaveMarketHistory(symbol, marketType, highValue, rawPayload, source),
            runtimeLog: (level, module, message) => TryWriteRuntimeLog(level, module, "走势图数据刷新失败", message),
            scheduler: _marketRequestScheduler,
            historyDepthCheckpointReader: key => _repository.ReadAppSetting(key),
            historyDepthCheckpointWriter: (key, value) => _repository.SaveAppSetting(key, value));
        InitializeComponent();
        VersionText.Text = BuildVersionDisplayText();
        _runtimeHealthMonitor = RuntimeHealthMonitor.CreateDefault(ResolveDisplayVersion(), Dispatcher);
        _chartWindowManager = new ChartWindowManager(
            this,
            _chartSubscriptions,
            _chartRefreshCoordinator,
            () => _tradeLogs,
            () => _strategies,
            _marketRefreshCts.Token);
        LoadEtfColumnSettings();
        LoadEtfPinnedSymbols();
        LoadHotkeySettings();
        _refreshTimer.Tick += RefreshTimer_Tick;
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowProc);
            RegisterConfiguredHotkey();
        };
        Loaded += (_, _) =>
        {
            _runtimeHealthMonitor.Start();
            UpdateDesignSurfaceWidth();
            BuildNavigation();
            RefreshLocalDataAndUi();
            ScheduleNextRefresh();
            WriteDiagnostics();
            if (Application.Current is App app)
            {
                app.ShowDatabaseStartupNotifications(this);
            }
        };
        Closed += (_, _) =>
        {
            _runtimeHealthMonitor.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _runtimeHealthMonitor.Dispose();
            _refreshTimer.Stop();
            _marketRefreshCts.Cancel();
            _globalHotkeyService.Dispose();
            _pushPlusAlertSender.Dispose();
            _voiceAlertPlayer.Dispose();
            _marketRefreshService.Dispose();
            _chartRefreshCoordinator.Dispose();
            _marketRefreshCts.Dispose();
        };
    }

    private void LoadEtfColumnSettings()
    {
        try
        {
            string? raw = _repository.ReadAppSetting(EtfDecisionColumnSettings.SettingKey);
            EtfDecisionColumnParseResult result = EtfDecisionColumnSettings.ParseVisibleColumns(raw);
            _visibleEtfColumnKeys = result.VisibleKeys;
            if (!string.IsNullOrWhiteSpace(raw) && result.UsedDefault)
            {
                TryWriteRuntimeLog("WARN", "MainWindow", "ETF 决策表列配置无效，已回退默认列", raw);
            }
            else if (result.IgnoredUnknown || result.RestoredRequired)
            {
                TryWriteRuntimeLog("WARN", "MainWindow", "ETF 决策表列配置已自动修正", raw ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            _visibleEtfColumnKeys = EtfDecisionColumnSettings.DefaultVisibleKeys;
            TryWriteRuntimeLog("ERROR", "MainWindow", "ETF 决策表列配置读取失败", ex.ToString());
        }
    }

    private void LoadEtfPinnedSymbols()
    {
        try
        {
            string? raw = _repository.ReadAppSetting(EtfDecisionColumnSettings.PinnedSymbolsSettingKey);
            _pinnedEtfSymbols = EtfDecisionColumnSettings.ParsePinnedSymbols(raw);
        }
        catch (Exception ex)
        {
            _pinnedEtfSymbols = Array.Empty<string>();
            TryWriteRuntimeLog("ERROR", "MainWindow", "ETF 决策表置顶配置读取失败", ex.ToString());
        }
    }

    private void LoadHotkeySettings()
    {
        try
        {
            _hotkeySettings = _repository.ReadHotkeySettings();
            _hotkeyRegistrationStatus = _hotkeySettings.Enabled ? "未保存" : "未启用";
        }
        catch (Exception ex)
        {
            _hotkeySettings = HotkeySettings.Default;
            _hotkeyRegistrationStatus = "注册失败";
            TryWriteRuntimeLog("ERROR", "MainWindow", "界面快捷键配置读取失败", ex.ToString());
        }
    }

    private void CustomizeEtfColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateEtfColumnChooser(_visibleEtfColumnKeys);
        EtfColumnChooserStatusText.Text = string.Empty;
        ShowEtfColumnChooserOverlay();
    }

    private void PopulateEtfColumnChooser(IEnumerable<string> selectedKeys)
    {
        HashSet<string> selected = EtfDecisionColumnSettings.NormalizeVisibleKeys(selectedKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> required = EtfDecisionColumnSettings.RequiredKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Style checkBoxStyle = (Style)EtfColumnChooserOverlay.FindResource("EtfColumnDialogCheckBoxStyle");

        _etfColumnChooserBoxes.Clear();
        EtfColumnChooserItems.Items.Clear();
        foreach (EtfDecisionColumnDefinition column in EtfDecisionColumnSettings.AllColumns)
        {
            bool isRequired = required.Contains(column.Key);
            var checkBox = new CheckBox
            {
                Content = column.HeaderText,
                Tag = column.Key,
                IsChecked = selected.Contains(column.Key) || isRequired,
                IsEnabled = !isRequired,
                Style = checkBoxStyle,
                ToolTip = isRequired ? "必选列，不能隐藏。" : null
            };
            _etfColumnChooserBoxes[column.Key] = checkBox;
            EtfColumnChooserItems.Items.Add(checkBox);
        }
    }

    private void EtfColumnSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox checkBox in _etfColumnChooserBoxes.Values)
        {
            checkBox.IsChecked = true;
        }

        EtfColumnChooserStatusText.Text = "已选择全部字段，保存后立即应用。";
    }

    private void EtfColumnRestoreDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        SetEtfColumnChooserSelection(EtfDecisionColumnSettings.DefaultVisibleKeys);
        EtfColumnChooserStatusText.Text = "已恢复默认核心列，保存后立即应用。";
    }

    private void EtfColumnCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideEtfColumnChooserOverlay();
    }

    private void EtfColumnSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string[] selectedKeys = _etfColumnChooserBoxes
                .Where(pair => pair.Value.IsChecked == true)
                .Select(pair => pair.Key)
                .ToArray();
            _visibleEtfColumnKeys = EtfDecisionColumnSettings.NormalizeVisibleKeys(selectedKeys);
            _repository.SaveAppSetting(
                EtfDecisionColumnSettings.SettingKey,
                EtfDecisionColumnSettings.SerializeVisibleColumns(_visibleEtfColumnKeys));
            ResetEtfDragState();
            BuildEtfTable();
            HideEtfColumnChooserOverlay();
        }
        catch (Exception ex)
        {
            EtfColumnChooserStatusText.Text = "列配置保存失败，请稍后重试。";
            TryWriteRuntimeLog("ERROR", "MainWindow", "ETF 决策表列配置保存失败", ex.ToString());
        }
    }

    private void SetEtfColumnChooserSelection(IEnumerable<string> keys)
    {
        HashSet<string> selected = EtfDecisionColumnSettings.NormalizeVisibleKeys(keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> required = EtfDecisionColumnSettings.RequiredKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, CheckBox checkBox) in _etfColumnChooserBoxes)
        {
            checkBox.IsChecked = selected.Contains(key) || required.Contains(key);
        }
    }

    private void ShowEtfColumnChooserOverlay()
    {
        EtfColumnChooserOverlay.Visibility = Visibility.Visible;
        EtfColumnChooserOverlay.Opacity = 0;
        if (EtfColumnChooserPanel.RenderTransform is TranslateTransform transform)
        {
            transform.X = 12;
            transform.BeginAnimation(TranslateTransform.XProperty, CreateDoubleAnimation(0, 190));
        }

        EtfColumnChooserOverlay.BeginAnimation(UIElement.OpacityProperty, CreateDoubleAnimation(1, 170));
    }

    private void HideEtfColumnChooserOverlay()
    {
        if (EtfColumnChooserOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        DoubleAnimation fadeOut = CreateDoubleAnimation(0, 150);
        fadeOut.Completed += (_, _) =>
        {
            EtfColumnChooserOverlay.Visibility = Visibility.Collapsed;
            EtfColumnChooserOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            EtfColumnChooserOverlay.Opacity = 0;
        };

        if (EtfColumnChooserPanel.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, CreateDoubleAnimation(12, 150));
        }

        EtfColumnChooserOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void RootShell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDesignSurfaceWidth();
        QueueResponsiveRedraw();
    }

    private void UpdateDesignSurfaceWidth()
    {
        if (RootShell.ActualWidth <= 0 || RootShell.ActualHeight <= 0)
        {
            return;
        }

        double workAreaAspect = RootShell.ActualWidth / RootShell.ActualHeight;
        double targetWidth = Math.Clamp(BaseDesignHeight * workAreaAspect, BaseDesignWidth, MaxExpandedDesignWidth);

        if (Math.Abs(DesignSurface.Width - targetWidth) > 0.5)
        {
            DesignSurface.Width = targetWidth;
        }
    }

    private void QueueResponsiveRedraw()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            DrawSparklines();
            DrawDrawdownCharts();
            BuildEtfTable();
            BuildOrderDraftPanel();
            BuildTradeLog();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshLocalDataAndUi();
        ScheduleNextRefresh();
    }

    private void ScheduleNextRefresh()
    {
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(2000, 4001));
        _refreshTimer.Start();
    }

    private void RefreshLocalDataAndUi()
    {
        _runtimeHealthMonitor.NotifyUiRefreshStarted();
        var healthStopwatch = Stopwatch.StartNew();
        bool refreshSucceeded = false;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            UpdateClock();

            bool localReadSucceeded = true;
            try
            {
                _database.Initialize();
                _strategies = _repository.ReadStrategyConfigs();
                _accountState = _repository.ReadLatestAccountState();
                _positions = _repository.ReadPositionStates();
                _otcChannels = _repository.ReadOtcChannels();
                _tradeLogs = _repository.ReadTradeLogs();
                _marketQuotes = _repository.ReadMarketQuoteCache();
                _marketHistory = _repository.ReadMarketHistoryCache();
                _marketStatuses = _repository.ReadMarketSourceStatuses();
                _accountReplayState = _repository.ReadLatestAccountReplayState();
                _accountReplaySnapshots = _repository.ReadAccountReplaySnapshots();
                _replayPositions = _repository.ReadPositionReplayStates();
                _replayOtcPositions = _repository.ReadOtcPositionReplayStates();
                _basePositionSettings = _repository.ReadBasePositionSettings();
                _alertSettings = _repository.ReadAlertSettings();
                _strategyDecisions = _repository.ReadStrategyDecisionStates();
                _orderDrafts = _repository.ReadOrderDraftStates();
                _orderDraftLegs = _repository.ReadOrderDraftLegStates();
                _orderFinalizations = _repository.ReadOrderFinalizationStates();
                _runtimeLogs = _repository.ReadRecentRuntimeLogs(60);
                stopwatch.Stop();
                _lastRefreshElapsedMs = stopwatch.ElapsedMilliseconds;

                bool configured = _strategies.Count > 0 || _accountState is not null;
                UpdateMarketRuntimeStatus(configured);
                ApplyAccountReplayRuntimeStatus();
                _marketRequestScheduler.BeginTick(DateTimeOffset.Now);
                QueueAccountReplayIfNeeded();
                QueueStrategyDecisionIfNeeded();
                QueueOrderDraftIfNeeded();
                if (!RuntimeMode.IsSmokeMode())
                {
                    QueueAlertDeliveryIfNeeded();
                    QueueChartRefresh();
                    QueueMarketRefresh();
                }
            }
            catch (Exception ex)
            {
                localReadSucceeded = false;
                stopwatch.Stop();
                _lastRefreshElapsedMs = stopwatch.ElapsedMilliseconds;
                _runtimeStatus = "错误：本地数据库读取失败";
                _runtimeStatusColor = Red;
                TryWriteRuntimeLog("ERROR", "MainWindow", "本地数据库读取失败", ex.ToString());
            }

            UpdateRuntimeStatus();
            UpdateTopMarketQuotes();
            UpdateAccountCards();
            UpdateOtcPanel();
            DrawSparklines();
            DrawRing();
            DrawPool();
            DrawDrawdownCharts();
            BuildEtfTable();
            BuildOrderDraftPanel();
            BuildTradeLog();
            refreshSucceeded = localReadSucceeded;
        }
        finally
        {
            healthStopwatch.Stop();
            _runtimeHealthMonitor.NotifyUiRefreshCompleted(refreshSucceeded, healthStopwatch.Elapsed);
        }
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        ClockText.Text = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        WeekdayText.Text = CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetDayName(now.DayOfWeek);
    }

    private void QueueMarketRefresh()
    {
        if (_marketRefreshCts.IsCancellationRequested || RuntimeMode.IsSmokeMode())
        {
            return;
        }

        IReadOnlyList<StrategyConfigRecord> strategies = _strategies;
        IReadOnlyList<PositionStateRecord> positions = _positions;
        IReadOnlyList<OtcChannelRecord> otcChannels = _otcChannels;
        _ = Task.Run(async () =>
        {
            try
            {
                await _marketRefreshService.RefreshAsync(strategies, positions, otcChannels, _marketRefreshCts.Token).ConfigureAwait(false);
                _ = Dispatcher.BeginInvoke(new Action(RefreshIndexQuoteDependentUiIfChanged), DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TryWriteRuntimeLog("ERROR", "MarketDataRefreshService", "后台行情刷新异常", ex.ToString());
            }
        });
    }

    // LOCKED: Accepted index quote/drawdown behavior. Refresh top cards and drawdown charts from local quote cache only.
    private void RefreshIndexQuoteDependentUiIfChanged()
    {
        IReadOnlyList<MarketQuoteRecord> latestQuotes = _repository.ReadMarketQuoteCache();
        if (!IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(_marketQuotes, latestQuotes))
        {
            return;
        }

        _marketQuotes = latestQuotes;
        UpdateTopMarketQuotes();
        DrawDrawdownCharts();
    }

    private void QueueChartRefresh()
    {
        if (_marketRefreshCts.IsCancellationRequested || RuntimeMode.IsSmokeMode())
        {
            return;
        }

        IReadOnlyList<MarketQuoteRecord> quotes = _marketQuotes.ToArray();
        IReadOnlyList<MarketQuoteRecord> history = _marketHistory.ToArray();
        IReadOnlyList<ChartSecurityInfo> backgroundSecurities = BuildEnabledChartSecurities(_strategies);
        if (_chartSubscriptions.ActiveSymbolCount == 0 && backgroundSecurities.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _chartRefreshCoordinator.RefreshAsync(quotes, history, backgroundSecurities, _marketRefreshCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TryWriteRuntimeLog("ERROR", "SecurityChart", "走势图后台刷新异常", ex.ToString());
            }
        });
    }

    private static IReadOnlyList<ChartSecurityInfo> BuildEnabledChartSecurities(IReadOnlyList<StrategyConfigRecord> strategies)
    {
        IEnumerable<ChartSecurityInfo> etfs = strategies
            .Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Code))
            .Select(item => ChartDataService.CreateSecurityInfo(item.Code, item.Name));

        return etfs
            .Concat(new[]
            {
                ChartDataService.CreateIndexSecurityInfo(IndexDrawdownChartSeriesBuilder.LeftChartSymbol, "纳指科技指数"),
                ChartDataService.CreateIndexSecurityInfo(IndexDrawdownChartSeriesBuilder.RightChartSymbol, "纳斯达克100")
            })
            .GroupBy(item => item.StrategyCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private void QueueAccountReplayIfNeeded()
    {
        string signature = BuildAccountReplaySignature();
        if (_accountReplayQueued || string.Equals(signature, _lastAccountReplaySignature, StringComparison.Ordinal))
        {
            return;
        }

        _accountReplayQueued = true;
        _lastAccountReplaySignature = signature;
        TradeLogRecord[] tradeLogs = _tradeLogs.ToArray();
        MarketQuoteRecord[] marketQuotes = _marketQuotes.ToArray();

        _ = Task.Run(() =>
        {
            using var _ = AppOperationContext.Begin("主界面后台账户回放");
            AccountReplayResult result = _accountReplayService.Replay(tradeLogs, marketQuotes);
            _repository.SaveAccountReplayResult(result);
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _accountReplayQueued = false;
                if (task.IsFaulted)
                {
                    _lastAccountReplaySignature = null;
                    Exception exception = task.Exception?.Flatten() is Exception flattened
                        ? flattened
                        : new InvalidOperationException("未知账户回放后台异常");
                    AppExceptionLogger.WriteRuntime("ERROR", "主界面后台账户回放", "账户回放后台异常", exception);
                    TryWriteRuntimeLog("ERROR", "AccountReplay", "账户回放后台异常", exception.ToString());
                    _runtimeStatus = "财务异常：账户回放后台异常";
                    _runtimeStatusColor = Red;
                    UpdateRuntimeStatus();
                    return;
                }

                RefreshLocalDataAndUi();
            }));
        });
    }

    private string BuildAccountReplaySignature()
    {
        var builder = new StringBuilder();
        builder.Append("T:");
        foreach (TradeLogRecord record in _tradeLogs.OrderBy(record => record.Id).ThenBy(record => record.Time, StringComparer.Ordinal))
        {
            builder.Append(record.Id).Append(',')
                .Append(record.Time).Append(',')
                .Append(record.StrategyCode).Append(',')
                .Append(record.ActualCode).Append(',')
                .Append(record.Action).Append(',')
                .Append(record.Price.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Amount.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Fee.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.NetCashImpact.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.CashBalance.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("Q:");
        foreach (MarketQuoteRecord quote in _marketQuotes.OrderBy(quote => quote.MarketType, StringComparer.Ordinal)
                     .ThenBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(quote => quote.Source, StringComparer.Ordinal))
        {
            builder.Append(quote.MarketType).Append(',')
                .Append(quote.Symbol).Append(',')
                .Append(quote.Source).Append(',')
                .Append(quote.Price?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(quote.LastClose?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(quote.ReceivedAt).Append(';');
        }

        return builder.ToString();
    }

    private void QueueStrategyDecisionIfNeeded()
    {
        string signature = BuildStrategyDecisionSignature();
        if (_strategyDecisionQueued || string.Equals(signature, _lastStrategyDecisionSignature, StringComparison.Ordinal))
        {
            return;
        }

        _strategyDecisionQueued = true;
        _lastStrategyDecisionSignature = signature;
        var input = new StrategyDecisionCalculationInput
        {
            Strategies = _strategies.ToArray(),
            AccountState = _accountState,
            AccountReplayState = _accountReplayState,
            PositionStates = _positions.ToArray(),
            PositionReplayStates = _replayPositions.ToArray(),
            OtcPositionReplayStates = _replayOtcPositions.ToArray(),
            OtcChannels = _otcChannels.ToArray(),
            TradeLogs = _tradeLogs.ToArray(),
            MarketQuotes = _marketQuotes.ToArray(),
            MarketHistory = _marketHistory.ToArray(),
            BasePositionSettings = _basePositionSettings
        };

        _ = Task.Run(() =>
        {
            using var _ = AppOperationContext.Begin("主界面后台策略决策");
            StrategyDecisionCalculationResult result = _strategyDecisionService.Calculate(input);
            _repository.SaveStrategyDecisionStates(result.Decisions);
            return result;
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _strategyDecisionQueued = false;
                if (task.IsFaulted)
                {
                    _lastStrategyDecisionSignature = null;
                    Exception exception = task.Exception?.Flatten() is Exception flattened
                        ? flattened
                        : new InvalidOperationException("未知策略决策后台异常");
                    TryWriteRuntimeLog("ERROR", "StrategyDecision", "策略计算异常", exception.ToString());
                    _runtimeStatus = "策略异常：" + ShortText(exception.Message, 46);
                    _runtimeStatusColor = Red;
                    UpdateRuntimeStatus();
                    return;
                }

                foreach (StrategyDecisionRuntimeWarning warning in task.Result.Warnings)
                {
                    TryWriteStrategyRuntimeLog(warning);
                }

                RefreshLocalDataAndUi();
            }));
        });
    }

    private void QueueOrderDraftIfNeeded()
    {
        string signature = BuildOrderDraftSignature();
        if (_orderDraftQueued || string.Equals(signature, _lastOrderDraftSignature, StringComparison.Ordinal))
        {
            return;
        }

        _orderDraftQueued = true;
        _lastOrderDraftSignature = signature;
        var input = new OrderDraftCalculationInput
        {
            StrategyDecisions = _strategyDecisions.ToArray(),
            AccountReplayState = _accountReplayState,
            PositionReplayStates = _replayPositions.ToArray(),
            OtcPositionReplayStates = _replayOtcPositions.ToArray(),
            OtcChannels = _otcChannels.ToArray(),
            TradeLogs = _tradeLogs.ToArray(),
            MarketQuotes = _marketQuotes.ToArray()
        };

        _ = Task.Run(() =>
        {
            using var _ = AppOperationContext.Begin("主界面后台委托草案");
            OrderDraftCalculationResult result = _orderDraftService.Calculate(input);
            _repository.SaveOrderDraftStates(result.Drafts, result.Legs);
            return result;
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _orderDraftQueued = false;
                if (task.IsFaulted)
                {
                    _lastOrderDraftSignature = null;
                    Exception exception = task.Exception?.Flatten() is Exception flattened
                        ? flattened
                        : new InvalidOperationException("未知委托草案后台异常");
                    TryWriteRuntimeLog("ERROR", "OrderDraft", "委托草案计算异常", exception.ToString());
                    _runtimeStatus = "委托草案异常：" + ShortText(exception.Message, 42);
                    _runtimeStatusColor = Red;
                    UpdateRuntimeStatus();
                    return;
                }

                foreach (OrderDraftRuntimeWarning warning in task.Result.Warnings)
                {
                    TryWriteOrderRuntimeLog(warning);
                }

                RefreshLocalDataAndUi();
            }));
        });
    }

    private void QueueAlertDeliveryIfNeeded()
    {
        if (RuntimeMode.IsSmokeMode())
        {
            return;
        }

        AlertSettings settings = AlertSettings.Normalize(_alertSettings);
        if (_alertDeliveryQueued || (!settings.PushPlusEnabled && !settings.VoiceEnabled))
        {
            return;
        }

        IReadOnlyList<RuntimeLogRecord> runtimeLogsForAlerts = ReadRuntimeLogsForAlertDelivery(
            out long runtimeLogCursorToSave,
            out bool shouldAdvanceRuntimeLogCursor);
        var input = new AlertRuleEvaluationInput
        {
            StrategyDecisions = _strategyDecisions.ToArray(),
            OrderDrafts = _orderDrafts.ToArray(),
            OrderDraftLegs = _orderDraftLegs.ToArray(),
            MarketStatuses = _marketStatuses.ToArray(),
            RuntimeLogs = runtimeLogsForAlerts,
            AccountReplayState = _accountReplayState
        };

        _alertDeliveryQueued = true;
        _ = Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<AlertEvent> alerts = _alertRuleEvaluator.Evaluate(input);
                if (shouldAdvanceRuntimeLogCursor)
                {
                    _repository.SaveRuntimeLogAlertCursor(runtimeLogCursorToSave);
                }

                var deliveryService = new AlertDeliveryService(_repository, _pushPlusAlertSender, _voiceAlertPlayer);
                await deliveryService.DeliverAsync(alerts, settings, _marketRefreshCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TryWriteRuntimeLog("ERROR", "AlertDelivery", "后台预警分发异常", ex.Message);
            }
        }).ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke(new Action(() => _alertDeliveryQueued = false));
        });
    }

    private IReadOnlyList<RuntimeLogRecord> ReadRuntimeLogsForAlertDelivery(out long cursorToSave, out bool shouldAdvanceCursor)
    {
        cursorToSave = 0;
        shouldAdvanceCursor = false;
        bool initialized = _repository.InitializeRuntimeLogAlertCursorIfMissing(out long cursor);
        if (initialized)
        {
            cursorToSave = cursor;
            return Array.Empty<RuntimeLogRecord>();
        }

        IReadOnlyList<RuntimeLogRecord> logs = _repository.ReadRuntimeLogsAfterId(cursor, 100);
        if (logs.Count == 0)
        {
            cursorToSave = cursor;
            return logs;
        }

        cursorToSave = logs.Max(log => log.Id);
        shouldAdvanceCursor = cursorToSave > cursor;
        return logs;
    }

    private string BuildOrderDraftSignature()
    {
        var builder = new StringBuilder();
        builder.Append("D:");
        foreach (StrategyDecisionStateRecord decision in _strategyDecisions.OrderBy(decision => decision.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(decision => decision.Id))
        {
            builder.Append(decision.Id).Append(',')
                .Append(decision.CalculatedAt).Append(',')
                .Append(decision.StrategyCode).Append(',')
                .Append(decision.ActionInstruction).Append(',')
                .Append(decision.StrategyStatus).Append(',')
                .Append(decision.PreferredSource).Append(',')
                .Append(decision.TargetAmount?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(decision.SuggestedPrice?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(decision.RealSniperPool?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(decision.BaseCurrentCost?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(decision.BaseTargetAmount?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(decision.IsActionable ? "1" : "0").Append(';');
        }

        builder.Append("A:");
        if (_accountReplayState is not null)
        {
            builder.Append(_accountReplayState.CalculatedAt).Append(',')
                .Append(_accountReplayState.CashBalance?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(_accountReplayState.TotalPositionCost?.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("P:");
        foreach (PositionReplayStateRecord position in _replayPositions.OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(position.StrategyCode).Append(',')
                .Append(position.ActualCode).Append(',')
                .Append(position.Source).Append(',')
                .Append(position.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CostAmount.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.AverageCost.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.MarketPrice?.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("OP:");
        foreach (OtcPositionReplayStateRecord position in _replayOtcPositions.OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(position.StrategyCode).Append(',')
                .Append(position.ActualCode).Append(',')
                .Append(position.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CostAmount.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.Nav?.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("O:");
        foreach (OtcChannelRecord channel in _otcChannels.OrderBy(channel => channel.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(channel => channel.OtcCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(channel.Id).Append(',')
                .Append(channel.StrategyCode).Append(',')
                .Append(channel.OtcCode).Append(',')
                .Append(channel.ClassType).Append(',')
                .Append(channel.Enabled ? "1" : "0").Append(',')
                .Append(channel.DailyLimit.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(channel.Priority).Append(',')
                .Append(channel.MinBuy.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("T:");
        foreach (TradeLogRecord record in _tradeLogs.OrderBy(record => record.Id).ThenBy(record => record.Time, StringComparer.Ordinal))
        {
            builder.Append(record.Id).Append(',')
                .Append(record.Time).Append(',')
                .Append(record.StrategyCode).Append(',')
                .Append(record.ActualCode).Append(',')
                .Append(record.Action).Append(',')
                .Append(record.Amount.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("Q:");
        foreach (MarketQuoteRecord quote in _marketQuotes.OrderBy(quote => quote.MarketType, StringComparer.Ordinal).ThenBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(quote.MarketType).Append(',')
                .Append(quote.Symbol).Append(',')
                .Append(quote.Price?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(quote.ReceivedAt).Append(';');
        }

        return builder.ToString();
    }

    private void TryWriteOrderRuntimeLog(OrderDraftRuntimeWarning warning)
    {
        string key = $"{warning.Level}|{warning.Module}|{warning.Message}|{warning.Detail}";
        DateTimeOffset now = DateTimeOffset.Now;
        if (_orderRuntimeLogLastAt.TryGetValue(key, out DateTimeOffset lastAt)
            && now - lastAt < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _orderRuntimeLogLastAt[key] = now;
        TryWriteRuntimeLog(warning.Level, warning.Module, warning.Message, warning.Detail);
    }

    private string BuildStrategyDecisionSignature()
    {
        var builder = new StringBuilder();
        builder.Append(BuildAccountReplaySignature());
        builder.Append("B:")
            .Append(_basePositionSettings.Mode).Append(',')
            .Append(_basePositionSettings.Ratio.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(_basePositionSettings.FixedAmount.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        builder.Append("S:");
        foreach (StrategyConfigRecord strategy in _strategies.OrderBy(strategy => strategy.Code, StringComparer.OrdinalIgnoreCase).ThenBy(strategy => strategy.Id))
        {
            builder.Append(strategy.Id).Append(',')
                .Append(strategy.Code).Append(',')
                .Append(strategy.Name).Append(',')
                .Append(strategy.IndexSecId).Append(',')
                .Append(strategy.ExtraPrice?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.TakeProfitPrice?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.SellRatio?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.AddPremiumLimit?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T1Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T2Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T3Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T4Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T5Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.T6Weight?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(strategy.Enabled ? "1" : "0").Append(';');
        }

        builder.Append("A:");
        if (_accountState is not null)
        {
            builder.Append(_accountState.Principal.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(_accountState.CashBalance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(_accountState.UpdatedAt).Append(';');
        }

        if (_accountReplayState is not null)
        {
            builder.Append(_accountReplayState.ReplayStatus).Append(',')
                .Append(_accountReplayState.ReplayError).Append(',')
                .Append(_accountReplayState.CashBalance?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(_accountReplayState.Principal?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(_accountReplayState.CalculatedAt).Append(';');
        }

        builder.Append("P:");
        foreach (PositionStateRecord position in _positions.OrderBy(position => position.Id))
        {
            builder.Append(position.Id).Append(',')
                .Append(position.StrategyCode).Append(',')
                .Append(position.ActualCode).Append(',')
                .Append(position.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CostAmount.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("RP:");
        foreach (PositionReplayStateRecord position in _replayPositions.OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(position.StrategyCode).Append(',')
                .Append(position.ActualCode).Append(',')
                .Append(position.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CostAmount.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.MarketValue?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.ReturnRate?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CalculatedAt).Append(';');
        }

        builder.Append("OP:");
        foreach (OtcPositionReplayStateRecord position in _replayOtcPositions.OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(position.StrategyCode).Append(',')
                .Append(position.ActualCode).Append(',')
                .Append(position.Quantity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CostAmount.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.MarketValue?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.ReturnRate?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(position.CalculatedAt).Append(';');
        }

        builder.Append("O:");
        foreach (OtcChannelRecord channel in _otcChannels.OrderBy(channel => channel.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(channel => channel.OtcCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(channel.Id).Append(',')
                .Append(channel.StrategyCode).Append(',')
                .Append(channel.OtcCode).Append(',')
                .Append(channel.Enabled ? "1" : "0").Append(',')
                .Append(channel.Priority).Append(';');
        }

        builder.Append("H:");
        foreach (MarketQuoteRecord history in _marketHistory.OrderBy(history => history.MarketType, StringComparer.Ordinal).ThenBy(history => history.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(history.MarketType).Append(',')
                .Append(history.Symbol).Append(',')
                .Append(history.HighValue?.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(history.ReceivedAt).Append(';');
        }

        return builder.ToString();
    }

    private void TryWriteStrategyRuntimeLog(StrategyDecisionRuntimeWarning warning)
    {
        string key = $"{warning.Level}|{warning.Module}|{warning.Message}|{warning.Detail}";
        DateTimeOffset now = DateTimeOffset.Now;
        if (_strategyRuntimeLogLastAt.TryGetValue(key, out DateTimeOffset lastAt)
            && now - lastAt < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _strategyRuntimeLogLastAt[key] = now;
        TryWriteRuntimeLog(warning.Level, warning.Module, warning.Message, warning.Detail);
    }

    private void ApplyAccountReplayRuntimeStatus()
    {
        if (_accountReplayState?.ReplayStatus == "财务异常")
        {
            _runtimeStatus = "财务异常：" + ShortText(_accountReplayState.ReplayError, 46);
            _runtimeStatusColor = Red;
        }
    }

    private void UpdateMarketRuntimeStatus(bool localConfigured)
    {
        MarketRuntimeStatusEvaluation evaluation = MarketRuntimeStatusEvaluator.Evaluate(
            _marketStatuses,
            localConfigured,
            HasValidCoreIndexHistoryCache());

        switch (evaluation.State)
        {
            case MarketRuntimeConnectionState.Connected:
                _runtimeStatus = "已连接：真实行情 " + (evaluation.LastSuccessAt ?? "--");
                _runtimeStatusColor = Green;
                return;
            case MarketRuntimeConnectionState.Partial:
                _runtimeStatus = "部分连接：真实行情 " + (evaluation.LastSuccessAt ?? "--");
                _runtimeStatusColor = Orange;
                return;
            case MarketRuntimeConnectionState.MarketError:
                _runtimeStatus = "行情异常：" + (string.IsNullOrWhiteSpace(evaluation.Error) ? "接口请求失败" : evaluation.Error);
                _runtimeStatusColor = Red;
                return;
            default:
                _runtimeStatus = localConfigured ? "未配置：等待真实行情连接" : "未配置：暂无策略或行情缓存";
                _runtimeStatusColor = Orange;
                return;
        }
    }

    private void UpdateRuntimeStatus()
    {
        var brush = BrushFrom(_runtimeStatusColor);
        TopStatusLight.Fill = brush;
        TopStatusText.Text = _runtimeStatus.StartsWith("已连接", StringComparison.Ordinal) ? "已连接" :
            _runtimeStatus.StartsWith("部分连接", StringComparison.Ordinal) ? "部分连接" :
            _runtimeStatus.StartsWith("财务异常", StringComparison.Ordinal) ? "财务异常" :
            _runtimeStatus.StartsWith("行情异常", StringComparison.Ordinal) || _runtimeStatus.StartsWith("错误", StringComparison.Ordinal) ? "行情异常" : "未配置";
        TopStatusText.Foreground = brush;
        SideStatusText.Text = _runtimeStatus;
        SideStatusText.ToolTip = _runtimeStatus;
        SideStatusText.Foreground = brush;
        RefreshLatencyText.Text = $"本地刷新 {_lastRefreshElapsedMs} ms";
    }

    private string? LatestMarketSuccessTime()
        => _marketStatuses
            .Select(status => status.LastSuccessAt)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value, StringComparer.Ordinal)
            .Select(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                ? parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : value)
            .FirstOrDefault();

    private void UpdateTopMarketQuotes()
    {
        SetTopMarketQuote("1.000300", TopHs300PriceText, TopHs300ChangeText);
        SetTopMarketQuote("1.000905", TopCsi500PriceText, TopCsi500ChangeText);
        SetTopMarketQuote("100.HSI", TopHsiPriceText, TopHsiChangeText);
        SetTopMarketQuote("100.NDX100", TopNdqPriceText, TopNdqChangeText);
        SetTopMarketQuote("251.NDXTMC", TopIxicPriceText, TopIxicChangeText);
        SetTopMarketQuote("133.USDCNH", TopFxPriceText, TopFxChangeText);
    }

    // LOCKED: Top quote cards show numeric values or "--" only; no per-card status words.
    private void SetTopMarketQuote(string symbol, TextBlock priceText, TextBlock changeText)
    {
        MarketQuoteRecord? quote = FindMarketQuote(symbol, null);
        if (quote?.Price is not double price)
        {
            priceText.Text = "--";
            changeText.Text = "  --";
            priceText.Foreground = BrushFrom(Muted);
            changeText.Foreground = BrushFrom(Muted);
            return;
        }

        priceText.Text = FormatPrice(price);
        string change = FormatSignedRatio(quote.ChangePercent);
        changeText.Text = "  " + change;
        Color color = QuoteColor(quote.ChangePercent);
        priceText.Foreground = BrushFrom(Text);
        changeText.Foreground = BrushFrom(color);
    }

    private void UpdateAccountCards()
    {
        if (_tradeLogs.Count > 0)
        {
            UpdateReplayAccountCards();
            return;
        }

        double positionCost = _positions.Sum(p => p.CostAmount);
        if (_accountState is null)
        {
            TotalAssetsText.Text = "0.00";
            TodayPnlText.Text = "--";
            SetFinancialColor(TodayPnlText, null);
            PositionPnlText.Text = "--";
            PositionPnlPercentText.Text = "--";
            PositionPnlRateText.Text = "--";
            SetFinancialColor(PositionPnlText, null);
            SetFinancialColor(PositionPnlPercentText, null);
            SetFinancialColor(PositionPnlRateText, null);
            BaseRatioText.Text = "--";
            BaseRatioTargetText.Text = "目标 --";
            BaseRatioReachedText.Text = "已录入 --";
            SniperPoolText.Text = "0.00";
            LastRefreshText.Text = "待策略模块";
            SniperPoolAvailableText.Text = "--";
            AccountUpdatedAtText.Text = "--";
            PrincipalSummaryText.Text = "0.00";
            CashBalanceSummaryText.Text = "0.00";
            PositionCostSummaryText.Text = FormatMoney(positionCost);
            KnownMarketValueSummaryText.Text = "--";
            AccountTotalAssetsSummaryText.Text = "0.00";
            AccountReplayStatusText.Text = "未回放";
            AccountReplayRealizedPnlText.Text = "--";
            AccountReplayFloatingPnlText.Text = "--";
            AccountReplayLastTradeText.Text = "--";
            AccountReplayCalculatedAtText.Text = "--";
            AccountReplayCalculatedAtText.ToolTip = null;
            TradeReplayStatusText.Text = "回放状态：未回放";
            ApplyStrategyPoolCard();
            return;
        }

        TotalAssetsText.Text = FormatMoney(_accountState.TotalAssets);
        TodayPnlText.Text = "--";
        SetFinancialColor(TodayPnlText, null);
        PositionPnlText.Text = "--";
        PositionPnlPercentText.Text = "--";
        PositionPnlRateText.Text = "--";
        SetFinancialColor(PositionPnlText, null);
        SetFinancialColor(PositionPnlPercentText, null);
        SetFinancialColor(PositionPnlRateText, null);
        BaseRatioText.Text = FormatPercentValue(_accountState.BasePositionRatio);
        BaseRatioTargetText.Text = "目标 --";
        BaseRatioReachedText.Text = $"已录入   {FormatPercentValue(_accountState.BasePositionRatio)}";
        SniperPoolText.Text = FormatMoney(_accountState.SniperPoolAmount);
        LastRefreshText.Text = "待策略模块";
        SniperPoolAvailableText.Text = FormatMoney(_accountState.SniperPoolAmount);
        AccountUpdatedAtText.Text = _accountState.UpdatedAt;
        PrincipalSummaryText.Text = FormatMoney(_accountState.Principal);
        CashBalanceSummaryText.Text = FormatMoney(_accountState.CashBalance);
        PositionCostSummaryText.Text = FormatMoney(positionCost);
        KnownMarketValueSummaryText.Text = "--";
        AccountTotalAssetsSummaryText.Text = FormatMoney(_accountState.TotalAssets);
        AccountReplayStatusText.Text = "未回放";
        AccountReplayRealizedPnlText.Text = "--";
        AccountReplayFloatingPnlText.Text = "--";
        AccountReplayLastTradeText.Text = "--";
        AccountReplayCalculatedAtText.Text = "--";
        AccountReplayCalculatedAtText.ToolTip = null;
        TradeReplayStatusText.Text = "回放状态：未回放";
        ApplyStrategyPoolCard();
    }

    private void UpdateReplayAccountCards()
    {
        AccountReplayStateRecord? replay = _accountReplayState;
        if (replay is null)
        {
            TotalAssetsText.Text = "--";
            TodayPnlText.Text = "回放中";
            TodayPnlText.Foreground = BrushFrom(Muted);
            PositionPnlText.Text = "--";
            PositionPnlPercentText.Text = "--";
            PositionPnlRateText.Text = "--";
            SetFinancialColor(PositionPnlText, null);
            SetFinancialColor(PositionPnlPercentText, null);
            SetFinancialColor(PositionPnlRateText, null);
            BaseRatioText.Text = "--";
            BaseRatioTargetText.Text = "待策略模块";
            BaseRatioReachedText.Text = "回放中";
            SniperPoolText.Text = "待策略模块";
            LastRefreshText.Text = "待策略模块";
            SniperPoolAvailableText.Text = "--";
            AccountUpdatedAtText.Text = "--";
            PrincipalSummaryText.Text = "--";
            CashBalanceSummaryText.Text = "--";
            PositionCostSummaryText.Text = "--";
            KnownMarketValueSummaryText.Text = "--";
            AccountTotalAssetsSummaryText.Text = "--";
            AccountReplayStatusText.Text = "回放中";
            AccountReplayRealizedPnlText.Text = "--";
            AccountReplayFloatingPnlText.Text = "--";
            AccountReplayLastTradeText.Text = "--";
            AccountReplayCalculatedAtText.Text = "--";
            AccountReplayCalculatedAtText.ToolTip = null;
            TradeReplayStatusText.Text = "回放状态：回放中";
            ApplyStrategyPoolCard();
            return;
        }

        bool incomplete = replay.ReplayStatus == "估值不完整" || !replay.MarketValueComplete;
        TotalAssetsText.Text = FormatNullableMoney(replay.TotalAssets);
        double? realDailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            _replayPositions,
            _replayOtcPositions,
            _marketQuotes,
            DateTime.Now);
        DailyPnlMetric dailyPnl = !incomplete && realDailyPnl.HasValue
            ? BuildValuationDailyPnlMetric(realDailyPnl.Value, replay.TotalAssets)
            : DailyPnlMetric.Empty;

        if (incomplete && !dailyPnl.Amount.HasValue)
        {
            TodayPnlText.Text = "估值不完整";
            TodayPnlText.Foreground = BrushFrom(Orange);
        }
        else
        {
            TodayPnlText.Text = FormatDailyPnl(dailyPnl);
            SetFinancialColor(TodayPnlText, dailyPnl.Amount);
        }

        PositionPnlText.Text = incomplete ? "--" : FormatSignedMoney(replay.TotalPnl);
        PositionPnlPercentText.Text = incomplete ? "估值不完整" : FormatSignedRatioFromRatio(replay.TotalReturnRate);
        PositionPnlRateText.Text = incomplete ? "估值不完整" : FormatSignedRatioFromRatio(replay.TotalReturnRate);
        SetFinancialColor(PositionPnlText, incomplete ? null : replay.TotalPnl);
        SetFinancialColor(PositionPnlPercentText, incomplete ? null : replay.TotalReturnRate);
        SetFinancialColor(PositionPnlRateText, incomplete ? null : replay.TotalReturnRate);

        if (replay.BasePositionRatio.HasValue)
        {
            BaseRatioText.Text = FormatPercent(replay.BasePositionRatio.Value);
            BaseRatioTargetText.Text = "目标 待策略模块";
            BaseRatioReachedText.Text = $"回放   {FormatPercent(replay.BasePositionRatio.Value)}";
        }
        else
        {
            BaseRatioText.Text = "--";
            BaseRatioTargetText.Text = "待策略模块";
            BaseRatioReachedText.Text = "本金未就绪";
        }

        SniperPoolText.Text = replay.CashBalance.HasValue ? FormatMoney(replay.CashBalance.Value) : "--";
        LastRefreshText.Text = "待策略模块";
        SniperPoolAvailableText.Text = "待策略模块";
        AccountUpdatedAtText.Text = replay.CalculatedAt;
        PrincipalSummaryText.Text = FormatNullableMoney(replay.Principal);
        CashBalanceSummaryText.Text = FormatNullableMoney(replay.CashBalance);
        PositionCostSummaryText.Text = FormatNullableMoney(replay.TotalPositionCost);
        KnownMarketValueSummaryText.Text = incomplete ? "估值不完整" : FormatNullableMoney(replay.KnownMarketValue);
        AccountTotalAssetsSummaryText.Text = FormatNullableMoney(replay.TotalAssets);
        AccountReplayStatusText.Text = replay.ReplayStatus;
        AccountReplayRealizedPnlText.Text = FormatSignedMoney(replay.TotalRealizedPnl);
        AccountReplayFloatingPnlText.Text = incomplete ? "--" : FormatSignedMoney(replay.TotalUnrealizedPnl);
        AccountReplayLastTradeText.Text = replay.LastTradeLogId?.ToString(CultureInfo.InvariantCulture) ?? "--";
        AccountReplayCalculatedAtText.Text = FormatReplayTime(replay.CalculatedAt);
        AccountReplayCalculatedAtText.ToolTip = replay.CalculatedAt;
        TradeReplayStatusText.Text = "回放状态：" + replay.ReplayStatus
                                     + (string.IsNullOrWhiteSpace(replay.ReplayError) ? string.Empty : "，" + ShortText(replay.ReplayError, 72));
        ApplyStrategyPoolCard();
    }

    private void ApplyStrategyPoolCard()
    {
        StrategyDecisionStateRecord? decision = _strategyDecisions
            .Where(decision => decision.RealSniperPool.HasValue)
            .OrderByDescending(decision => decision.CalculatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (decision is null)
        {
            ApplyBasePositionCardFallback();
            return;
        }

        SniperPoolText.Text = FormatMoney(decision.RealSniperPool!.Value);
        SniperPoolAvailableText.Text = FormatMoney(decision.RealSniperPool.Value);
        ApplyBasePositionCardFallback();
        if (decision.TierTotalParts is double parts && parts > 0)
        {
            LastRefreshText.Text = $"{FormatQuantity(parts)}份";
        }
        else if (_strategyDecisions.Any(item => item.PrerequisiteStatus == "未就绪"))
        {
            LastRefreshText.Text = "前置未就绪";
        }
    }

    private void ApplyBasePositionCardFallback()
    {
        double principal = Math.Max(0, _accountReplayState?.Principal ?? _accountState?.Principal ?? 0);
        double currentCost = Math.Max(0, _accountReplayState?.TotalPositionCost
                                          ?? SumNullable(_replayPositions.Select(position => (double?)position.CostAmount)
                                              .Concat(_replayOtcPositions.Select(position => (double?)position.CostAmount)))
                                          ?? _positions.Sum(position => position.CostAmount));
        BasePositionTargetResult target = BasePositionSettingsService.ResolveBaseTarget(principal, _basePositionSettings);
        double completion = BasePositionSettingsService.CalculateCompletionRate(currentCost, target.TargetAmount);
        BaseRatioText.Text = target.TargetAmount > 0 ? FormatPercent(completion) : "--";
        BaseRatioTargetText.Text = "基准 " + BasePositionSettingsService.FormatDisplay(_basePositionSettings);
        BaseRatioReachedText.Text = target.TargetAmount > 0
            ? $"目标 {FormatMoney(target.TargetAmount)} 当前 {FormatMoney(currentCost)}" + (target.IsCappedToPrincipal ? " 封顶" : string.Empty)
            : "目标 -- 当前 " + FormatMoney(currentCost);
    }

    private double GetBaseCompletionRatio()
    {
        double principal = Math.Max(0, _accountReplayState?.Principal ?? _accountState?.Principal ?? 0);
        double currentCost = Math.Max(0, _accountReplayState?.TotalPositionCost
                                          ?? SumNullable(_replayPositions.Select(position => (double?)position.CostAmount)
                                              .Concat(_replayOtcPositions.Select(position => (double?)position.CostAmount)))
                                          ?? _positions.Sum(position => position.CostAmount));
        BasePositionTargetResult target = BasePositionSettingsService.ResolveBaseTarget(principal, _basePositionSettings);
        return Math.Clamp(BasePositionSettingsService.CalculateCompletionRate(currentCost, target.TargetAmount), 0, 1);
    }

    private void UpdateOtcPanel()
    {
        int enabledCount = _otcChannels.Count(channel => channel.Enabled);
        int aCount = _otcChannels.Count(channel => channel.Enabled && channel.ClassType == "A类");
        int cCount = _otcChannels.Count(channel => channel.Enabled && channel.ClassType == "C类");
        double dailyLimit = _otcChannels.Where(channel => channel.Enabled).Sum(channel => channel.DailyLimit);
        MarketQuoteRecord? firstOtcQuote = _otcChannels
            .Where(channel => channel.Enabled)
            .Select(channel => FindMarketQuote(MarketSymbolNormalizer.DigitsOnly(channel.OtcCode), "OTC"))
            .FirstOrDefault(quote => quote?.Price is not null);

        string otcRecommendation = BuildOtcRecommendation();
        OtcSummaryText.Text = enabledCount == 0
            ? $"当前替代建议：{otcRecommendation}"
            : $"已启用通道：{enabledCount}  当前替代建议：{otcRecommendation}";
        OtcAClassText.Text = $"A类通道：{aCount}";
        OtcCClassText.Text = $"C类通道：{cCount}";
        OtcLimitText.Text = enabledCount == 0 ? "本地限额：--" :
            firstOtcQuote?.Price is double nav ? $"本地限额：{FormatMoney(dailyLimit)}  净值：{FormatPrice(nav)}" : $"本地限额：{FormatMoney(dailyLimit)}  净值：未连接";
    }

    private string BuildOtcRecommendation()
    {
        if (_strategyDecisions.Count == 0)
        {
            return "前置未就绪";
        }

        if (_strategyDecisions.Any(decision => string.Equals(decision.PreferredSource, "场外替代", StringComparison.Ordinal)
                                               && decision.ActionInstruction is not "--"
                                               && decision.StrategyStatus is "溢价替代" or "现金上限" or "资金配置" or "逢低吸筹"))
        {
            return "场外替代";
        }

        if (_strategyDecisions.All(decision => decision.PrerequisiteStatus == "未就绪"
                                               && string.Equals(decision.ActionInstruction, "--", StringComparison.Ordinal)))
        {
            return "前置未就绪";
        }

        return "场内优先";
    }

    private void TryWriteRuntimeLog(string level, string module, string message, string detail)
    {
        try
        {
            _repository.WriteRuntimeLog(level, module, message, detail);
        }
        catch
        {
            // The UI status already reports the database failure; avoid crashing while logging that failure.
        }
    }

    private void TitleDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse capture was lost during a fast double-click.
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OpenManualEntry_Click(object sender, MouseButtonEventArgs e)
        => OpenManualEntry(ManualEntryScope.SystemSettings);

    private void OpenManualEntry(ManualEntryScope scope)
    {
        var window = new ManualDataEntryWindow(_repository, _database.DatabasePath, scope, _runtimeHealthMonitor)
        {
            Owner = this,
            SaveHotkeySettingsRequested = SaveHotkeySettingsFromSettingsWindow
        };
        window.RefreshHotkeySettingsUi();
        window.DataSaved += (_, _) => RefreshLocalDataAndUi();
        window.ShowDialog();
        RefreshLocalDataAndUi();
    }

    private HotkeySettingsSaveResult SaveHotkeySettingsFromSettingsWindow(HotkeySettings settings)
    {
        if (!settings.TryValidate(out string? error))
        {
            return HotkeySettingsSaveResult.Failed("未保存", error ?? "快捷键无效");
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        HotkeySettings previousSettings = _hotkeySettings;
        GlobalHotkeyRegistrationResult registration = _globalHotkeyService.Apply(hwnd, settings);
        _hotkeyRegistrationStatus = registration.StatusText;
        if (!registration.Success)
        {
            return HotkeySettingsSaveResult.Failed(
                registration.StatusText,
                registration.Message ?? "快捷键冲突，未保存");
        }

        try
        {
            _repository.SaveHotkeySettings(settings);
            _hotkeySettings = settings;
            return HotkeySettingsSaveResult.Saved(
                registration.StatusText,
                settings.Enabled
                    ? $"快捷键已保存并生效：{settings.DisplayText}"
                    : "快捷键已清除。");
        }
        catch (Exception ex)
        {
            GlobalHotkeyRegistrationResult rollback = _globalHotkeyService.Apply(hwnd, previousSettings);
            _hotkeyRegistrationStatus = rollback.StatusText;
            TryWriteRuntimeLog("ERROR", "MainWindow", "界面快捷键设置保存失败", ex.ToString());
            return HotkeySettingsSaveResult.Failed("未保存", "快捷键设置保存失败，已保留旧配置。");
        }
    }

    public static ManualEntryScope? ResolveManualEntryScopeForNavigation(string navigationName)
        => navigationName switch
        {
            "溢价决策" => ManualEntryScope.PremiumDecision,
            "交易日志" => ManualEntryScope.TradeLog,
            "系统设置" => ManualEntryScope.SystemSettings,
            _ => null
        };

    public static bool IsRiskCenterNavigation(string navigationName)
        => string.Equals(navigationName, "风险中心", StringComparison.Ordinal);

    public static bool IsMarketMonitorNavigation(string navigationName)
        => string.Equals(navigationName, "行情监控", StringComparison.Ordinal);

    public static bool IsCapitalPositionNavigation(string navigationName)
        => string.Equals(navigationName, "资金仓位", StringComparison.Ordinal);

    public static bool IsIndicatorDrawdownNavigation(string navigationName)
        => string.Equals(navigationName, "指标回撤", StringComparison.Ordinal);

    public static bool IsT1T6ChartCenterNavigation(string navigationName)
        => string.Equals(navigationName, "T1-T6看图", StringComparison.Ordinal);

    public static bool IsActionableNavigation(string navigationName)
        => ResolveManualEntryScopeForNavigation(navigationName) is not null
           || IsMarketMonitorNavigation(navigationName)
           || IsIndicatorDrawdownNavigation(navigationName)
           || IsCapitalPositionNavigation(navigationName)
           || IsT1T6ChartCenterNavigation(navigationName)
           || IsRiskCenterNavigation(navigationName);

    public static string BuildVersionDisplayText()
        => $"版本： {ResolveDisplayVersion()}";

    public static string ResolveDisplayVersion()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Version? assemblyVersion = assembly.GetName().Version;
        string version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assemblyVersion?.ToString(3) ?? "0.0.0"
            : informationalVersion;
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        int prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            version = version[..prereleaseIndex];
        }

        return "V" + version.TrimStart('v', 'V');
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string navigationName })
        {
            return;
        }

        e.Handled = true;
        SelectNavigation(navigationName);

        if (IsMarketMonitorNavigation(navigationName))
        {
            OpenMarketMonitor();
            return;
        }

        if (IsRiskCenterNavigation(navigationName))
        {
            OpenRiskCenter();
            return;
        }

        if (IsIndicatorDrawdownNavigation(navigationName))
        {
            OpenIndicatorDrawdown();
            return;
        }

        if (IsCapitalPositionNavigation(navigationName))
        {
            OpenCapitalPosition();
            return;
        }

        if (IsT1T6ChartCenterNavigation(navigationName))
        {
            OpenT1T6ChartCenter();
            return;
        }

        if (ResolveManualEntryScopeForNavigation(navigationName) is ManualEntryScope scope)
        {
            OpenManualEntry(scope);
        }
    }

    private void SelectNavigation(string navigationName)
    {
        _selectedNavigationName = navigationName;

        foreach ((string name, Border background) in _navigationBackgrounds)
        {
            bool isSelected = string.Equals(name, navigationName, StringComparison.Ordinal);
            background.Background = BrushFrom(isSelected ? "#8A1D2A" : "#06101B");
            background.CornerRadius = new CornerRadius(isSelected ? 7 : 0);
        }

        foreach ((string name, TextBlock label) in _navigationLabels)
        {
            bool isSelected = string.Equals(name, navigationName, StringComparison.Ordinal);
            label.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void OpenMarketMonitor()
    {
        if (_marketMonitorWindow is { IsVisible: true })
        {
            _marketMonitorWindow.Activate();
            return;
        }

        _marketMonitorWindow = new MarketMonitorWindow(_repository)
        {
            Owner = this
        };
        _marketMonitorWindow.Closed += (_, _) => _marketMonitorWindow = null;
        _marketMonitorWindow.Show();
    }

    private void OpenRiskCenter()
    {
        if (_riskCenterWindow is { IsVisible: true })
        {
            _riskCenterWindow.Activate();
            return;
        }

        _riskCenterWindow = new RiskCenterWindow(_repository)
        {
            Owner = this
        };
        _riskCenterWindow.Closed += (_, _) => _riskCenterWindow = null;
        _riskCenterWindow.Show();
    }

    private void OpenT1T6ChartCenter()
    {
        if (_t1T6ChartCenterWindow is { IsVisible: true })
        {
            if (_t1T6ChartCenterWindow.WindowState == WindowState.Minimized)
            {
                _t1T6ChartCenterWindow.WindowState = WindowState.Normal;
            }

            _t1T6ChartCenterWindow.Activate();
            _t1T6ChartCenterWindow.Focus();
            return;
        }

        var snapshotBuilder = new T1T6ChartCenterSnapshotBuilder();
        T1T6ChartCenterReadModel readModel = _repository.ReadT1T6ChartCenterReadModel();
        T1T6ChartCenterSnapshot initialSnapshot = snapshotBuilder.Build(readModel, DateTimeOffset.Now);
        _t1T6ChartCenterWindow = new T1T6ChartCenterWindow(
            _repository,
            OpenT1T6SecurityChart,
            readModel,
            initialSnapshot)
        {
            Owner = this
        };
        _t1T6ChartCenterWindow.Closed += (_, _) => _t1T6ChartCenterWindow = null;
        _t1T6ChartCenterWindow.Show();
    }

    private void OpenT1T6SecurityChart(T1T6ChartOpenRequest request)
    {
        string normalizedSymbol = MarketSymbolNormalizer.DigitsOnly(request.Symbol);
        if (normalizedSymbol.Length != 6 || !normalizedSymbol.All(char.IsDigit))
        {
            throw new InvalidOperationException("ETF代码无效，无法打开图表。");
        }

        string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? normalizedSymbol
            : request.DisplayName.Trim();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo(
            normalizedSymbol,
            displayName,
            normalizedSymbol);
        _chartWindowManager.OpenOrActivate(security);
        QueueChartRefresh();
    }

    private void BuildNavigation()
    {
        NavStack.Children.Clear();
        _navigationBackgrounds.Clear();
        _navigationLabels.Clear();
        string[] icons = { "⌂", "▥", "◔", "⌁", "▣", "◱", "▤", "♢", "⚙" };
        string[] names =
        {
            "作战总览", "行情监控", "溢价决策", "指标回撤", "资金仓位",
            "T1-T6看图", "交易日志", "风险中心", "系统设置"
        };

        for (int i = 0; i < names.Length; i++)
        {
            var row = new Grid { Height = 83, Tag = names[i], Background = BrushFrom("#06101B") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var bg = new Border
            {
                Background = i == 0 ? BrushFrom("#8A1D2A") : BrushFrom("#06101B"),
                CornerRadius = new CornerRadius(i == 0 ? 7 : 0)
            };
            Grid.SetColumnSpan(bg, 2);
            row.Children.Add(bg);
            _navigationBackgrounds[names[i]] = bg;

            row.Children.Add(new TextBlock
            {
                Text = icons[i],
                FontSize = 28,
                Foreground = BrushFrom(Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            var label = new TextBlock
            {
                Text = names[i],
                FontSize = 22,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = BrushFrom(Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            _navigationLabels[names[i]] = label;
            if (IsActionableNavigation(names[i]))
            {
                var navButton = new Button
                {
                    Content = row,
                    Tag = names[i],
                    Height = 83,
                    Background = BrushFrom("#06101B"),
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    ToolTip = names[i]
                };
                System.Windows.Automation.AutomationProperties.SetName(navButton, names[i]);
                navButton.Template = CreateTransparentNavigationButtonTemplate();
                navButton.Click += NavigationButton_Click;
                NavStack.Children.Add(navButton);
                continue;
            }
            NavStack.Children.Add(row);
        }

        if (names.Length > 0)
        {
            SelectNavigation(names[0]);
        }
    }

    private static ControlTemplate CreateTransparentNavigationButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        border.SetValue(System.Windows.Controls.Border.BackgroundProperty, BrushFrom("#06101B"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private void OpenIndicatorDrawdown()
    {
        if (_indicatorDrawdownWindow is { IsVisible: true })
        {
            if (_indicatorDrawdownWindow.WindowState == WindowState.Minimized)
            {
                _indicatorDrawdownWindow.WindowState = WindowState.Normal;
            }

            _indicatorDrawdownWindow.Activate();
            return;
        }

        _indicatorDrawdownWindow = new IndicatorDrawdownWindow(_repository)
        {
            Owner = this
        };
        _indicatorDrawdownWindow.Closed += (_, _) => _indicatorDrawdownWindow = null;
        _indicatorDrawdownWindow.Show();
    }

    private void OpenCapitalPosition()
    {
        if (_capitalPositionWindow is { IsVisible: true })
        {
            if (_capitalPositionWindow.WindowState == WindowState.Minimized)
            {
                _capitalPositionWindow.WindowState = WindowState.Normal;
            }

            _capitalPositionWindow.Activate();
            return;
        }

        _capitalPositionWindow = new CapitalPositionWindow(_repository)
        {
            Owner = this
        };
        _capitalPositionWindow.Closed += (_, _) => _capitalPositionWindow = null;
        _capitalPositionWindow.Show();
    }

    private void DrawSparklines()
    {
        DrawSparkline(
            Spark1,
            SparklineSeriesBuilder.Build(_accountReplaySnapshots, record => record.TotalAssets),
            "暂无数据");
        DrawSparkline(
            Spark2,
            SparklineSeriesBuilder.Build(_accountReplaySnapshots, record => record.TotalPnl ?? record.TotalUnrealizedPnl),
            "暂无数据");
    }

    private void DrawSparkline(Canvas canvas, IReadOnlyList<SparklinePoint> points, string emptyText)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        canvas.Children.Add(new Line
        {
            X1 = 0,
            Y1 = height - 8,
            X2 = width,
            Y2 = height - 8,
            Stroke = BrushFrom("#13263A"),
            StrokeThickness = 1
        });
        if (points.Count == 0)
        {
            AddText(canvas, emptyText, 42, 20, 14, Muted, FontWeights.Normal);
            return;
        }

        double left = 2;
        double top = 7;
        double plotWidth = Math.Max(1, width - 4);
        double plotHeight = Math.Max(1, height - 18);
        SparklineTrend trend = SparklineSeriesBuilder.GetTrend(points);
        Color lineColor = trend == SparklineTrend.Down ? Green : Red;
        if (points.Count == 1)
        {
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = BrushFrom(lineColor)
            };
            Canvas.SetLeft(dot, left + plotWidth * points[0].X - 3.5);
            Canvas.SetTop(dot, top + plotHeight * points[0].Y - 3.5);
            canvas.Children.Add(dot);
            AddText(canvas, "数据不足", 76, 22, 13, Muted, FontWeights.Normal);
            return;
        }

        var glow = new Polyline
        {
            Stroke = BrushFrom(Color.FromArgb(70, lineColor.R, lineColor.G, lineColor.B)),
            StrokeThickness = 5,
            StrokeLineJoin = PenLineJoin.Round
        };
        var polyline = new Polyline
        {
            Stroke = BrushFrom(lineColor),
            StrokeThickness = 2.4,
            StrokeLineJoin = PenLineJoin.Round
        };
        foreach (SparklinePoint point in points)
        {
            var plotPoint = new Point(left + plotWidth * point.X, top + plotHeight * point.Y);
            glow.Points.Add(plotPoint);
            polyline.Points.Add(plotPoint);
        }

        canvas.Children.Add(glow);
        canvas.Children.Add(polyline);
        if (points.All(point => Math.Abs(point.Value - points[0].Value) <= 0.000001))
        {
            AddText(canvas, "真实持平", Math.Max(8, width - 74), 9, 12, Muted, FontWeights.Normal);
        }
    }

    private void DrawRing()
    {
        RingCanvas.Children.Clear();
        const double centerX = 52;
        const double centerY = 52;
        const double radius = 46;
        const double strokeThickness = 13;

        RingCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = new EllipseGeometry(new Point(centerX, centerY), radius, radius),
            Stroke = BrushFrom("#183249"),
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        double ratio = GetBaseCompletionRatio();
        if (ratio <= 0)
        {
            return;
        }

        Geometry progressGeometry = ratio >= 0.999
            ? new EllipseGeometry(new Point(centerX, centerY), radius, radius)
            : Arc(centerX, centerY, radius, -90, -90 + 360 * ratio);

        RingCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = progressGeometry,
            Stroke = new LinearGradientBrush(Blue, ColorFrom("#7B4CFF"), 0),
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private void DrawPool()
    {
        PoolCanvas.Children.Clear();
        double x = 11;
        double top = 12;
        double width = 98;
        double ellipseHeight = 28;
        double bottom = 94;
        double centerOffset = ellipseHeight / 2;

        var bodyFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(ColorFrom("#1600D084"), 0),
                new GradientStop(ColorFrom("#4A00D084"), 0.48),
                new GradientStop(ColorFrom("#2200B978"), 1)
            }
        };
        var body = new Rectangle
        {
            Width = width,
            Height = bottom - top,
            Fill = bodyFill,
            StrokeThickness = 0
        };
        Canvas.SetLeft(body, x);
        Canvas.SetTop(body, top + centerOffset);
        PoolCanvas.Children.Add(body);

        var sideBrush = BrushFrom("#9900D084");
        PoolCanvas.Children.Add(new Line
        {
            X1 = x + 1,
            Y1 = top + centerOffset,
            X2 = x + 1,
            Y2 = bottom + centerOffset,
            Stroke = sideBrush,
            StrokeThickness = 1.4
        });
        PoolCanvas.Children.Add(new Line
        {
            X1 = x + width - 1,
            Y1 = top + centerOffset,
            X2 = x + width - 1,
            Y2 = bottom + centerOffset,
            Stroke = sideBrush,
            StrokeThickness = 1.4
        });

        AddPoolEllipse(x, top, width, ellipseHeight, "#2C00D084", "#00D084", 2.1);
        AddPoolEllipse(x, top + 22, width, ellipseHeight, "#00000000", "#8A00D084", 1.5);
        AddPoolEllipse(x, top + 44, width, ellipseHeight, "#00000000", "#7600D084", 1.4);
        AddPoolEllipse(x, top + 66, width, ellipseHeight, "#00000000", "#7600D084", 1.4);
        AddPoolEllipse(x, bottom, width, ellipseHeight, "#3900D084", "#00D084", 2.1);

        var highlight = new Border
        {
            Width = 16,
            Height = 86,
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(
                ColorFrom("#4C7CFFCE"),
                ColorFrom("#0000D084"),
                new Point(0, 0),
                new Point(0, 1)),
            Opacity = 0.36
        };
        Canvas.SetLeft(highlight, x + 17);
        Canvas.SetTop(highlight, top + 18);
        PoolCanvas.Children.Add(highlight);
    }

    private void AddPoolEllipse(double x, double y, double width, double height, string fill, string stroke, double thickness)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Fill = BrushFrom(fill),
            Stroke = BrushFrom(stroke),
            StrokeThickness = thickness
        };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        PoolCanvas.Children.Add(ellipse);
    }

    private void DrawDrawdownCharts()
    {
        DrawChart(LeftChartCanvas, LeftChartStatusText, true, IndexDrawdownChartSeriesBuilder.LeftChartSymbol, "纳指科技增强");
        DrawChart(RightChartCanvas, RightChartStatusText, false, IndexDrawdownChartSeriesBuilder.RightChartSymbol, "纳斯达克100");
    }

    private void DrawChart(Canvas canvas, TextBlock statusText, bool redChart, string historySymbol, string chartTitle)
    {
        canvas.Children.Clear();
        double availableWidth = canvas.Parent is FrameworkElement parent && parent.ActualWidth > 80
            ? parent.ActualWidth - 50
            : redChart ? 1000 : 1110;
        canvas.Width = Math.Max(redChart ? 1000 : 1110, availableWidth);
        canvas.Height = 286;

        double gx = 74;
        double gy = 5;
        double gw = canvas.Width - 330;
        double gh = 214;
        Color accent = redChart ? Red : Blue;

        IReadOnlyList<MarketHistoryPoint> historyPoints = GetChartHistoryPoints(historySymbol);
        MarketHistoryPoint? latestPoint = GetLatestIndexChartPoint(historySymbol);
        IndexDrawdownChartSeries series = IndexDrawdownChartSeriesBuilder.Build(historyPoints, latestPoint);
        IReadOnlyList<IndexDrawdownAxisTick> axisTicks = series.IsReady
            ? series.AxisTicks
            : IndexDrawdownChartSeriesBuilder.BuildAxisTicks(IndexDrawdownChartSeriesBuilder.DefaultAxisMinDrawdown);
        double axisMinDrawdown = series.IsReady
            ? series.AxisMinDrawdown
            : IndexDrawdownChartSeriesBuilder.DefaultAxisMinDrawdown;

        foreach (IndexDrawdownAxisTick tick in axisTicks)
        {
            double y = gy + tick.YRatio * gh;
            AddText(canvas, tick.Text, 7, y - 11, 17, Text, FontWeights.Normal);
            var line = new Line
            {
                X1 = gx,
                Y1 = y,
                X2 = gx + gw,
                Y2 = y,
                Stroke = BrushFrom(accent),
                StrokeThickness = 1,
                Opacity = Math.Abs(tick.Drawdown) < 0.0000001 ? 0.58 : 0.34,
                StrokeDashArray = new DoubleCollection { Math.Abs(tick.Drawdown) < 0.0000001 ? 2.5 : 2, Math.Abs(tick.Drawdown) < 0.0000001 ? 5 : 6 }
            };
            canvas.Children.Add(line);
        }
        AddDrawdownWarningLabels(canvas, gx, gy, gw, gh, axisMinDrawdown);

        if (!series.IsReady)
        {
            statusText.Text = $"跟踪指数：{historySymbol}（{chartTitle}）";
            statusText.Foreground = BrushFrom(Muted);
            for (int i = 0; i < 7; i++)
            {
                double x = gx + i * gw / 6;
                AddText(canvas, "--", x - 8, gy + gh + 14, 16, Muted, FontWeights.Normal);
            }

            AddText(canvas, "历史 K 线暂不可用，T1-T6 前置未就绪", gx + gw * 0.31, gy + 84, 22, Text, FontWeights.SemiBold);
            AddText(canvas, historySymbol + " 未成功返回真实 klines，未绘制假曲线", gx + gw * 0.32, gy + 122, 17, Muted, FontWeights.Normal);
            return;
        }

        statusText.Text = $"跟踪指数：{historySymbol}（{chartTitle}）";
        statusText.Foreground = BrushFrom(Muted);
        foreach (IndexDrawdownAxisLabel label in series.AxisLabels)
        {
            double x = gx + label.XRatio * gw;
            AddText(canvas, label.Text, x - 20, gy + gh + 14, 14, Muted, FontWeights.Normal);
        }

        var area = new Polygon
        {
            Fill = CreateDrawdownAreaBrush(accent),
            Opacity = 1,
            Clip = new RectangleGeometry(new Rect(gx, gy, gw, gh))
        };
        foreach (IndexDrawdownAreaPoint areaPoint in IndexDrawdownChartSeriesBuilder.BuildAreaPointRatios(series.Points))
        {
            area.Points.Add(new Point(gx + areaPoint.XRatio * gw, gy + areaPoint.YRatio * gh));
        }
        canvas.Children.Add(area);

        var polyline = new Polyline
        {
            Stroke = BrushFrom(accent),
            StrokeThickness = 2.2,
            Opacity = 0.95,
            Clip = new RectangleGeometry(new Rect(gx, gy, gw, gh))
        };
        double latestX = gx;
        double latestY = gy;
        foreach (IndexDrawdownChartPoint point in series.Points)
        {
            latestX = gx + point.XRatio * gw;
            latestY = gy + point.YRatio * gh;
            polyline.Points.Add(new Point(latestX, latestY));
        }
        canvas.Children.Add(polyline);
        if (series.LatestDrawdown.HasValue)
        {
            const double tagWidth = 96;
            const double tagHeight = 30;
            var forbiddenRects = new[]
            {
                new IndexDrawdownRect(gw + 20, 0, 190, gh),
                new IndexDrawdownRect(gw + 8, Math.Max(0, gh - 58), 210, 62)
            };
            IndexDrawdownLabelPlacement labelPlacement = IndexDrawdownChartSeriesBuilder.PlaceLatestLabel(
                series.Points,
                gw,
                gh,
                tagWidth,
                tagHeight,
                forbiddenRects);
            AddDrawdownLabelLeader(canvas, gx, gy, labelPlacement, accent);
            AddValueTag(canvas, gx + labelPlacement.X, gy + labelPlacement.Y, FormatSignedRatioFromRatio(series.LatestDrawdown.Value), accent);
            AddDrawdownSummary(canvas, gx, gy, gw, gh, series.LatestDrawdown.Value);
        }
    }

    private void AddDrawdownWarningLabels(Canvas canvas, double gx, double gy, double gw, double gh, double axisMinDrawdown)
    {
        (double Threshold, string Text)[] labels =
        {
            (5, "轻度预警"),
            (10, "中度预警"),
            (15, "高度预警"),
            (20, "极度预警")
        };

        foreach ((double threshold, string text) in labels)
        {
            double y = gy + IndexDrawdownChartSeriesBuilder.CalculateYRatio(-threshold / 100.0, axisMinDrawdown) * gh;
            AddText(canvas, $"-{threshold:0}%", gx + gw + 28, y - 10, 15, Muted, FontWeights.Normal);
            AddText(canvas, text, gx + gw + 78, y - 10, 15, Orange, FontWeights.SemiBold);
        }
    }

    private static void AddDrawdownLabelLeader(Canvas canvas, double gx, double gy, IndexDrawdownLabelPlacement placement, Color accent)
    {
        var line = new Line
        {
            X1 = gx + placement.LeaderStartX,
            Y1 = gy + placement.LeaderStartY,
            X2 = gx + placement.LeaderEndX,
            Y2 = gy + placement.LeaderEndY,
            Stroke = BrushFrom(accent),
            StrokeThickness = 1,
            Opacity = 0.62,
            StrokeDashArray = new DoubleCollection { 3, 3 }
        };
        canvas.Children.Add(line);
    }

    private static Brush CreateDrawdownAreaBrush(Color accent)
    {
        var start = Color.FromArgb(0x28, accent.R, accent.G, accent.B);
        var middle = Color.FromArgb(0x16, accent.R, accent.G, accent.B);
        var end = Color.FromArgb(0x00, accent.R, accent.G, accent.B);
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(start, 0.0),
                new(middle, 0.45),
                new(end, 1.0)
            },
            new Point(0, 0),
            new Point(0, 1));
    }

    private void AddDrawdownSummary(Canvas canvas, double gx, double gy, double gw, double gh, double latestDrawdown)
    {
        double x = gx + gw + 42;
        double y = gy + gh - 34;
        double distanceToExtreme = Math.Max(0, 0.20 + latestDrawdown);

        AddText(canvas, "当前回撤：", x, y, 15, Text, FontWeights.Normal);
        AddText(canvas, FormatSignedRatioFromRatio(latestDrawdown), x + 128, y, 15, Green, FontWeights.SemiBold);
        AddText(canvas, "距离极限预警：", x - 26, y + 28, 15, Text, FontWeights.Normal);
        AddText(canvas, FormatPercent(distanceToExtreme), x + 128, y + 28, 15, Green, FontWeights.SemiBold);
    }

    private void BuildEtfTable()
    {
        EtfTableGrid.Children.Clear();
        EtfTableGrid.RowDefinitions.Clear();
        EtfTableGrid.ColumnDefinitions.Clear();
        EtfTableGrid.ClipToBounds = true;
        EtfTableGrid.MouseMove -= EtfTableGrid_MouseMove;
        EtfTableGrid.MouseLeftButtonUp -= EtfTableGrid_MouseLeftButtonUp;
        EtfTableGrid.LostMouseCapture -= EtfTableGrid_LostMouseCapture;
        EtfTableGrid.MouseMove += EtfTableGrid_MouseMove;
        EtfTableGrid.MouseLeftButtonUp += EtfTableGrid_MouseLeftButtonUp;
        EtfTableGrid.LostMouseCapture += EtfTableGrid_LostMouseCapture;
        IReadOnlyList<EtfDecisionColumnDefinition> allColumns = EtfDecisionColumnSettings.AllColumns;
        IReadOnlyList<EtfDecisionColumnDefinition> visibleColumns = EtfDecisionColumnSettings.ResolveVisibleColumns(_visibleEtfColumnKeys);
        HashSet<int> visibleSourceColumns = visibleColumns.Select(column => column.SourceIndex).ToHashSet();
        if (_etfSortSourceColumn >= 0 && !visibleSourceColumns.Contains(_etfSortSourceColumn))
        {
            _etfSortSourceColumn = -1;
            _etfSortDescending = false;
        }

        string[][] displayRows = BuildEtfRows(allColumns.Count);
        displayRows = ApplyEtfSort(displayRows);
        displayRows = ApplyEtfPinnedSort(displayRows);
        _selectedEtfStrategyCode = EtfDecisionDisplayAnimationHelper.RetainSelectedCode(
            displayRows.Select(row => row.Length > 0 ? row[0] : string.Empty),
            _selectedEtfStrategyCode);

        EnsureEtfColumnOrder(visibleColumns.Select(column => column.SourceIndex).ToArray());
        EtfDecisionColumnDefinition[] orderedColumns = _etfColumnOrder
            .Select(sourceColumn => allColumns.First(column => column.SourceIndex == sourceColumn))
            .ToArray();
        double[] orderedWidths = orderedColumns.Select(column => column.PreferredWidth).ToArray();

        double etfScale = GetColumnScale(EtfTableGrid, orderedWidths);
        foreach (double width in orderedWidths)
        {
            EtfTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * etfScale) });
        }
        EtfTableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(31) });
        for (int i = 0; i < displayRows.Length; i++)
        {
            EtfTableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        }

        for (int c = 0; c < orderedColumns.Length; c++)
        {
            EtfDecisionColumnDefinition column = orderedColumns[c];
            AddEtfCell(GetEtfHeaderText(column.HeaderText, column.SourceIndex), 0, c, Muted, FontWeights.SemiBold, "#081A28", 15, true, column.SourceIndex);
        }
        for (int r = 0; r < displayRows.Length; r++)
        {
            string rowToolTip = BuildEtfRowToolTip(displayRows[r], allColumns);
            string strategyCode = displayRows[r][0];
            bool isPinned = IsEtfPinned(strategyCode);
            bool isSelected = string.Equals(
                NormalizePinnedEtfSymbol(strategyCode),
                NormalizePinnedEtfSymbol(_selectedEtfStrategyCode),
                StringComparison.OrdinalIgnoreCase);
            bool hasPinnedFeedback = IsRecentPinnedFeedback(strategyCode);
            string rowFill = isSelected
                ? "#123A55"
                : isPinned ? "#102D3A" : r % 2 == 0 ? "#061622" : "#071927";
            var rowCells = new List<Border>();
            for (int c = 0; c < orderedColumns.Length; c++)
            {
                int sourceColumn = orderedColumns[c].SourceIndex;
                string value = displayRows[r][sourceColumn];
                if (sourceColumn == 0 && isPinned)
                {
                    value = "★ " + value;
                }

                string? toolTip = null;
                if (sourceColumn == 4 && _etfOrderDraftTooltips.TryGetValue(displayRows[r][0], out string? draftToolTip))
                {
                    toolTip = draftToolTip;
                }
                else if (sourceColumn == 0)
                {
                    toolTip = isPinned
                        ? "已置顶" + Environment.NewLine + rowToolTip
                        : rowToolTip;
                }

                bool isWarningInstructionCell = sourceColumn == 2 && EtfDecisionDisplayAnimationHelper.IsWarningInstruction(value);
                string cellFill = GetEtfCellFill(sourceColumn, value, rowFill);
                Border cell = AddEtfCell(
                    value,
                    r + 1,
                    c,
                    CellColor(sourceColumn, value, strategyCode),
                    sourceColumn is 2 or 3 ? FontWeights.SemiBold : FontWeights.Normal,
                    cellFill,
                    15,
                    false,
                    sourceColumn,
                    toolTip,
                    strategyCode,
                    hasPinnedFeedback,
                    isWarningInstructionCell);
                rowCells.Add(cell);
            }
            WireEtfRowInteraction(rowCells, strategyCode, isSelected);
        }
        AddEtfDropIndicator();
    }

    private string[][] BuildEtfRows(int columnCount)
    {
        _etfOrderDraftTooltips.Clear();
        if (_strategies.Count == 0)
        {
            var empty = CreateEmptyRow(columnCount);
            empty[0] = "暂无已配置 ETF";
            return new[] { empty };
        }

        var rows = new List<string[]>();
        _etfRowHasPosition.Clear();
        foreach (StrategyConfigRecord strategy in _strategies)
        {
            string etfCode = MarketSymbolNormalizer.DigitsOnly(strategy.Code);
            MarketQuoteRecord? etfQuote = FindMarketQuote(etfCode, "ETF");
            string? indexSymbol = string.IsNullOrWhiteSpace(strategy.IndexSecId)
                ? null
                : MarketSymbolNormalizer.NormalizeEastMoneySecId(strategy.IndexSecId, true);
            MarketQuoteRecord? indexQuote = string.IsNullOrWhiteSpace(strategy.IndexSecId)
                ? null
                : FindMarketQuote(indexSymbol, "INDEX");
            MarketQuoteRecord? etfHistory = FindHistoryQuote(etfCode, "ETF");
            MarketQuoteRecord? indexHistory = string.IsNullOrWhiteSpace(strategy.IndexSecId)
                ? null
                : FindHistoryQuote(indexSymbol, "INDEX");
            StrategyDecisionStateRecord? decision = FindStrategyDecision(strategy.Code);
            double? etfHigh = MaxValue(etfHistory?.HighValue, strategy.EtfHigh, etfQuote?.Price);
            double? indexHigh = MaxValue(indexHistory?.HighValue, strategy.IndexHigh, indexQuote?.Price);
            double? etfDrawdown = CalculateDrawdown(etfQuote?.Price, etfHigh);
            double? indexDrawdown = decision?.IndexDrawdown ?? CalculateDrawdown(indexQuote?.Price, indexHigh);
            PositionReplayStateRecord[] replayPositions = _tradeLogs.Count > 0
                ? _replayPositions
                    .Where(position => string.Equals(position.StrategyCode, strategy.Code, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<PositionReplayStateRecord>();
            OtcPositionReplayStateRecord[] replayOtcPositions = _tradeLogs.Count > 0
                ? _replayOtcPositions
                    .Where(position => string.Equals(position.StrategyCode, strategy.Code, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<OtcPositionReplayStateRecord>();
            PositionStateRecord[] manualPositions = _tradeLogs.Count == 0
                ? _positions
                    .Where(position => string.Equals(position.StrategyCode, strategy.Code, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<PositionStateRecord>();

            bool hasReplayPositions = replayPositions.Length > 0 || replayOtcPositions.Length > 0;
            EtfPositionCostMetrics replayCostMetrics = EtfDecisionTableMetrics.CalculatePositionCostMetrics(replayPositions, replayOtcPositions);
            double manualQuantity = manualPositions.Sum(position => position.Quantity);
            double manualCostAmount = manualPositions.Sum(position => position.CostAmount);
            double quantity = hasReplayPositions ? replayCostMetrics.TotalQuantity : manualQuantity;
            double costAmount = hasReplayPositions ? replayCostMetrics.TotalCostAmount : manualCostAmount;
            double averageCost = hasReplayPositions
                ? replayCostMetrics.AverageCost
                : manualQuantity > 0 ? manualCostAmount / manualQuantity : 0;
            double? premiumRate = EtfDecisionTableMetrics.CalculatePremiumRate(etfQuote);
            double principal = _tradeLogs.Count > 0 ? _accountReplayState?.Principal ?? 0 : _accountState?.Principal ?? 0;
            bool replayQuoteComplete = replayPositions.Length > 0 && replayPositions.All(position => position.MarketValue.HasValue);
            double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
                replayPositions,
                replayOtcPositions,
                _marketQuotes,
                DateTime.Now);
            double? knownMarketValue = replayQuoteComplete
                ? replayPositions.Sum(position => position.MarketValue!.Value)
                : null;
            double? holdingPnl = EtfDecisionTableMetrics.CalculateHoldingPnl(knownMarketValue, costAmount);
            double? holdingRate = EtfDecisionTableMetrics.CalculateHoldingReturnRate(holdingPnl, costAmount);
            double? principalRatio = EtfDecisionTableMetrics.CalculatePrincipalRatio(costAmount, principal);
            double adjFactor = hasReplayPositions
                ? replayPositions
                    .Where(position => position.Quantity > 0)
                    .Select(position => position.AdjFactor)
                    .DefaultIfEmpty(1)
                    .Average()
                : manualPositions
                    .Where(position => position.Quantity > 0)
                    .Select(position => position.AdjFactor)
                    .DefaultIfEmpty(1)
                    .Average();
            double equivalentQuantity = quantity * adjFactor;
            _etfRowHasPosition[strategy.Code] = EtfDecisionDisplayAnimationHelper.HasEtfPosition(quantity, costAmount);
            // VBA qText 口径：场内买卖显示股数，场外买入显示金额，场外卖出显示份额，多通道用 Tooltip 展开。
            EtfOrderDraftDisplay orderDraftDisplay = BuildEtfOrderDraftDisplay(strategy.Code, _orderDrafts, _orderDraftLegs);
            if (!string.IsNullOrWhiteSpace(orderDraftDisplay.ToolTip))
            {
                _etfOrderDraftTooltips[strategy.Code] = orderDraftDisplay.ToolTip;
            }

            rows.Add(new[]
            {
                strategy.Code,
                string.IsNullOrWhiteSpace(strategy.Name) ? etfQuote?.DisplayName ?? strategy.Code : strategy.Name,
                string.IsNullOrWhiteSpace(decision?.ActionInstruction) ? "--" : decision!.ActionInstruction!,
                string.IsNullOrWhiteSpace(decision?.StrategyStatus) ? strategy.Enabled ? "待策略模块" : "本地停用" : decision!.StrategyStatus!,
                orderDraftDisplay.Text,
                FormatSignedRatioFromRatio(premiumRate),
                FormatNullableNumber(etfQuote?.Price),
                FormatSignedRatio(etfQuote?.ChangePercent),
                FormatSignedMoney(dailyPnl),
                FormatSignedMoney(holdingPnl),
                FormatSignedRatioFromRatio(holdingRate),
                FormatNullableNumber(etfHigh),
                FormatSignedRatio(etfDrawdown),
                FormatSignedRatio(indexDrawdown),
                FormatNullableNumber(indexQuote?.Price),
                FormatNullableNumber(indexHigh),
                FormatSignedRatio(indexQuote?.ChangePercent),
                FormatStrategyPercent(strategy.SellRatio),
                FormatStrategyPercent(strategy.TakeProfitPrice),
                FormatNullableNumber(etfQuote?.Iopv),
                quantity == 0 ? "--" : FormatQuantity(quantity),
                averageCost == 0 ? "--" : FormatNullableNumber(averageCost),
                principalRatio.HasValue ? FormatPercent(principalRatio.Value) : "--",
                quantity == 0 ? "--" : FormatNullableNumber(adjFactor),
                equivalentQuantity == 0 ? "--" : FormatQuantity(equivalentQuantity)
            });
        }

        return rows.ToArray();
    }

    public sealed record EtfOrderDraftDisplay(string Text, string? ToolTip);

    public static EtfOrderDraftDisplay BuildEtfOrderDraftDisplay(
        string strategyCode,
        IEnumerable<OrderDraftStateRecord> drafts,
        IEnumerable<OrderDraftLegStateRecord> legs)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(legs);

        OrderDraftStateRecord? draft = drafts
            .Where(item => SameStrategyCode(item.StrategyCode, strategyCode))
            .OrderByDescending(IsDisplayableOrderDraft)
            .ThenByDescending(item => item.Amount)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        if (draft is null)
        {
            return new EtfOrderDraftDisplay("--", null);
        }

        string status = NormalizeOrderDraftStatusLabel(draft.DraftStatus);
        string side = NormalizeOrderDraftSide(draft.Side);
        string source = string.IsNullOrWhiteSpace(draft.Source) ? "--" : draft.Source.Trim();
        OrderDraftLegStateRecord[] draftLegs = legs
            .Where(item => string.Equals(item.DraftKey, draft.DraftKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => IsSellSide(side) ? OrderDraftLegSourceRank(item) : 0)
            .ThenBy(item => IsOtcOrderDraft(draft) && IsSellSide(side) ? OtcSellClassRank(item.ChannelClass) : 0)
            .ThenBy(item => item.Priority ?? int.MaxValue)
            .ThenBy(item => item.Id)
            .ToArray();

        if (!IsDisplayableOrderDraft(draft))
        {
            string reason = ResolveOrderDraftReason(draft, draftLegs);
            return new EtfOrderDraftDisplay("--", $"状态：{status}{Environment.NewLine}原因：{reason}");
        }

        if (IsExchangeOrderDraft(draft) && IsBuyOrSellSide(side))
        {
            double quantity = draft.Quantity > 0 ? draft.Quantity : draftLegs.Sum(item => item.Quantity);
            if (quantity < 100 || !IsBoardLot(quantity))
            {
                string reason = ResolveOrderDraftReason(draft, draftLegs, "数量不足 100 股整手");
                return new EtfOrderDraftDisplay("--", $"状态：{status}{Environment.NewLine}原因：{reason}");
            }

            string quantityText = FormatWholeShares(quantity);
            string tooltip = BuildExchangeOrderDraftTooltip(draft, draftLegs, status, side, source, quantityText);
            return new EtfOrderDraftDisplay(quantityText, tooltip);
        }

        if (IsOtcOrderDraft(draft) && IsBuySide(side))
        {
            double amount = draftLegs.Length > 0 ? draftLegs.Sum(item => item.Amount) : draft.Amount;
            string displayText = amount > 0 ? FormatOrderMoney(amount) + "元" : "--";
            string tooltip = BuildOtcBuyOrderDraftTooltip(draft, draftLegs, status, side, source, displayText);
            return amount > 0
                ? new EtfOrderDraftDisplay(displayText, tooltip)
                : new EtfOrderDraftDisplay("--", tooltip);
        }

        if (IsOtcOrderDraft(draft) && IsSellSide(side))
        {
            double quantity = draftLegs.Length > 0 ? draftLegs.Sum(item => item.Quantity) : draft.Quantity;
            double amount = draftLegs.Length > 0 ? draftLegs.Sum(item => item.Amount) : draft.Amount;
            string displayText = quantity > 0 ? FormatFundShares(quantity) + "份" : "--";
            string tooltip = BuildOtcSellOrderDraftTooltip(draft, draftLegs, status, side, source, displayText, amount);
            return quantity > 0
                ? new EtfOrderDraftDisplay(displayText, tooltip)
                : new EtfOrderDraftDisplay("--", tooltip);
        }

        string fallbackReason = ResolveOrderDraftReason(draft, draftLegs, "暂无可显示的委托摘要");
        return new EtfOrderDraftDisplay("--", $"状态：{status}{Environment.NewLine}原因：{fallbackReason}");
    }

    private static string BuildExchangeOrderDraftTooltip(
        OrderDraftStateRecord draft,
        IReadOnlyList<OrderDraftLegStateRecord> legs,
        string status,
        string side,
        string source,
        string quantityText)
    {
        double? price = FirstPositive(draft.Price, legs.Select(item => item.Price).FirstOrDefault(value => value.HasValue));
        double amount = draft.Amount > 0 ? draft.Amount : legs.Sum(item => item.Amount);
        var builder = new StringBuilder()
            .Append("状态：").Append(status).AppendLine()
            .Append("方向：").Append(side).AppendLine()
            .Append("来源：").Append(source).AppendLine()
            .Append("价格：").Append(price.HasValue ? FormatPrice(price.Value) : "--").AppendLine()
            .Append("数量：").Append(quantityText).AppendLine()
            .Append("金额：").Append(amount > 0 ? FormatOrderMoney(amount) + "元" : "--");

        AppendOrderDraftLegDetails(builder, legs);
        AppendOrderDraftReason(builder, draft, legs);
        return builder.ToString();
    }

    private static string BuildOtcBuyOrderDraftTooltip(
        OrderDraftStateRecord draft,
        IReadOnlyList<OrderDraftLegStateRecord> legs,
        string status,
        string side,
        string source,
        string displayText)
    {
        var builder = new StringBuilder()
            .Append("状态：").Append(status).AppendLine()
            .Append("方向：").Append(side).AppendLine()
            .Append("来源：").Append(source).AppendLine()
            .Append("主表显示金额：").Append(displayText).AppendLine()
            .Append("通道：");

        if (legs.Count == 0)
        {
            builder.AppendLine().Append(FormatOrderMoney(draft.Amount)).Append("元");
        }
        else
        {
            if (legs.Count > 1)
            {
                builder.AppendLine().Append("拆单：多通道 ").Append(legs.Count).Append("笔");
            }

            foreach (OrderDraftLegStateRecord leg in legs)
            {
                builder.AppendLine()
                    .Append(FormatOrderLegName(leg))
                    .Append(' ')
                    .Append(FormatOrderMoney(leg.Amount))
                    .Append("元");
            }
        }

        AppendOrderDraftReason(builder, draft, legs);
        return builder.ToString();
    }

    private static string BuildOtcSellOrderDraftTooltip(
        OrderDraftStateRecord draft,
        IReadOnlyList<OrderDraftLegStateRecord> legs,
        string status,
        string side,
        string source,
        string displayText,
        double totalAmount)
    {
        var builder = new StringBuilder()
            .Append("状态：").Append(status).AppendLine()
            .Append("方向：").Append(side).AppendLine()
            .Append("来源：").Append(source).AppendLine()
            .Append("主表显示份额：").Append(displayText).AppendLine()
            .Append("估算赎回金额：").Append(totalAmount > 0 ? FormatOrderMoney(totalAmount) + "元" : "--");

        if (legs.Count <= 1)
        {
            OrderDraftLegStateRecord? leg = legs.FirstOrDefault();
            double quantity = leg?.Quantity > 0 ? leg.Quantity : draft.Quantity;
            double amount = leg?.Amount > 0 ? leg.Amount : draft.Amount;
            builder.AppendLine()
                .Append("赎回份额：").Append(quantity > 0 ? FormatFundShares(quantity) + "份" : "--").AppendLine()
                .Append("估算赎回金额：").Append(amount > 0 ? FormatOrderMoney(amount) + "元" : "--");
        }
        else
        {
            builder.AppendLine().Append("拆单：多通道 ").Append(legs.Count).Append("笔");
            foreach (OrderDraftLegStateRecord leg in legs)
            {
                builder.AppendLine()
                    .Append(FormatOrderLegName(leg))
                    .Append(' ')
                    .Append(FormatFundShares(leg.Quantity))
                    .Append("份 估算金额 ")
                    .Append(FormatOrderMoney(leg.Amount))
                    .Append("元");
            }
        }

        AppendOrderDraftReason(builder, draft, legs);
        return builder.ToString();
    }

    private static void AppendOrderDraftLegDetails(StringBuilder builder, IReadOnlyList<OrderDraftLegStateRecord> legs)
    {
        if (legs.Count <= 1)
        {
            return;
        }

        builder.AppendLine().Append("拆单：").Append(legs.Count).Append("笔");
        foreach (OrderDraftLegStateRecord leg in legs)
        {
            builder.AppendLine().Append(FormatOrderLegName(leg)).Append(' ');
            if (leg.Source.Contains("场外", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatFundShares(leg.Quantity))
                    .Append("份 估算金额 ")
                    .Append(FormatOrderMoney(leg.Amount))
                    .Append("元");
            }
            else
            {
                builder.Append(FormatWholeShares(leg.Quantity))
                    .Append(" 金额 ")
                    .Append(FormatOrderMoney(leg.Amount))
                    .Append("元");
            }
        }
    }

    private static void AppendOrderDraftReason(StringBuilder builder, OrderDraftStateRecord draft, IReadOnlyList<OrderDraftLegStateRecord> legs)
    {
        string reason = ResolveOrderDraftReason(draft, legs, string.Empty);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine().Append("原因：").Append(reason);
        }
    }

    private static string ResolveOrderDraftReason(
        OrderDraftStateRecord draft,
        IReadOnlyList<OrderDraftLegStateRecord> legs,
        string fallback = "不可执行")
    {
        if (!string.IsNullOrWhiteSpace(draft.Reason))
        {
            return draft.Reason.Trim();
        }

        string? legReason = legs.Select(item => item.Reason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        return string.IsNullOrWhiteSpace(legReason) ? fallback : legReason.Trim();
    }

    private static bool IsDisplayableOrderDraft(OrderDraftStateRecord draft)
        => draft.IsExecutable
           && (string.Equals(draft.DraftStatus, "草案", StringComparison.Ordinal)
               || string.Equals(draft.DraftStatus, "部分可委托", StringComparison.Ordinal));

    private static bool IsExchangeOrderDraft(OrderDraftStateRecord draft)
        => draft.Source.Contains("场内", StringComparison.OrdinalIgnoreCase);

    private static bool IsOtcOrderDraft(OrderDraftStateRecord draft)
        => draft.Source.Contains("场外", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuyOrSellSide(string side)
        => IsBuySide(side) || IsSellSide(side);

    private static bool IsBuySide(string side)
        => string.Equals(side, "买入", StringComparison.OrdinalIgnoreCase)
           || string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase);

    private static bool IsSellSide(string side)
        => string.Equals(side, "卖出", StringComparison.OrdinalIgnoreCase)
           || string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOrderDraftSide(string side)
    {
        if (IsBuySide(side))
        {
            return "买入";
        }

        if (IsSellSide(side))
        {
            return "卖出";
        }

        return string.IsNullOrWhiteSpace(side) ? "--" : side.Trim();
    }

    private static bool IsBoardLot(double quantity)
    {
        double roundedLots = Math.Round(quantity / 100.0);
        return Math.Abs(quantity - roundedLots * 100.0) < 0.0001;
    }

    private static string FormatWholeShares(double quantity)
        => Math.Round(quantity, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "股";

    private static string FormatFundShares(double quantity)
        => quantity.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string FormatOrderMoney(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatOrderLegName(OrderDraftLegStateRecord leg)
    {
        string code = string.IsNullOrWhiteSpace(leg.ActualCode) ? leg.StrategyCode : leg.ActualCode!;
        return string.IsNullOrWhiteSpace(leg.ChannelClass)
            ? code
            : code + " " + leg.ChannelClass!.Trim();
    }

    private static int OtcSellClassRank(string? classType)
        => classType?.IndexOf("C类", StringComparison.OrdinalIgnoreCase) >= 0 ? 0
            : classType?.IndexOf("A类", StringComparison.OrdinalIgnoreCase) >= 0 ? 1
            : 2;

    private static int OrderDraftLegSourceRank(OrderDraftLegStateRecord leg)
        => leg.Source.Contains("场内", StringComparison.OrdinalIgnoreCase) ? 0
            : leg.Source.Contains("场外", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

    private static bool SameStrategyCode(string? left, string? right)
        => string.Equals(MarketSymbolNormalizer.DigitsOnly(left ?? string.Empty), MarketSymbolNormalizer.DigitsOnly(right ?? string.Empty), StringComparison.OrdinalIgnoreCase)
           || string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static double? FirstPositive(params double?[] values)
        => values.FirstOrDefault(value => value.HasValue && value.Value > 0);

    private static string BuildEtfRowToolTip(IReadOnlyList<string> row, IReadOnlyList<EtfDecisionColumnDefinition> columns)
    {
        var builder = new StringBuilder();
        builder.Append("完整行信息");
        foreach (EtfDecisionColumnDefinition column in columns)
        {
            string value = column.SourceIndex < row.Count ? row[column.SourceIndex] : "--";
            builder.AppendLine()
                .Append(column.HeaderText)
                .Append("：")
                .Append(string.IsNullOrWhiteSpace(value) ? "--" : value);
        }

        return builder.ToString();
    }

    private Border AddEtfCell(
        string value,
        int row,
        int col,
        Color color,
        FontWeight weight,
        string fill,
        double size,
        bool isHeader,
        int sourceColumn,
        string? toolTip = null,
        string? rowStrategyCode = null,
        bool rowFeedback = false,
        bool preserveFillOnRowHover = false)
    {
        string effectiveFill = fill;
        if (isHeader && _etfIsDragging && col == _etfDragSourceColumn)
        {
            effectiveFill = "#0D3453";
        }
        else if (isHeader && _etfIsDragging && col == _etfDragTargetColumn)
        {
            effectiveFill = "#0B2C46";
        }
        else if (isHeader && sourceColumn == _etfSortSourceColumn)
        {
            effectiveFill = "#092033";
        }

        var border = new Border
        {
            Background = BrushFrom(effectiveFill),
            BorderBrush = BrushFrom(isHeader && (_etfIsDragging || sourceColumn == _etfSortSourceColumn) ? "#1B4A6C" : "#0A2234"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Tag = new EtfCellVisualState(effectiveFill, preserveFillOnRowHover)
        };
        EtfDecisionTextAlignment alignment = isHeader
            ? EtfDecisionDisplayAnimationHelper.GetEtfHeaderTextAlignment(sourceColumn)
            : EtfDecisionDisplayAnimationHelper.GetEtfDataTextAlignment(sourceColumn);
        var textBlock = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = ToWpfTextAlignment(alignment),
            Margin = isHeader ? new Thickness(5, 0, 3, 0) : new Thickness(5, 0, 3, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);
        if (isHeader)
        {
            border.Child = textBlock;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            EtfTableGrid.Children.Add(border);
            WireEtfHeaderDrag(border, col, effectiveFill);
            return border;
        }

        string? resolvedToolTip = ResolveEtfCellToolTip(toolTip, value, sourceColumn);
        if (!string.IsNullOrWhiteSpace(resolvedToolTip))
        {
            textBlock.ToolTip = new ToolTip
            {
                Background = BrushFrom("#071B2A"),
                BorderBrush = BrushFrom("#1F4E68"),
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = resolvedToolTip,
                    Foreground = BrushFrom(Text),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                }
            };
            border.ToolTip = textBlock.ToolTip;
        }

        if (!isHeader && CanPinEtfSymbol(rowStrategyCode))
        {
            border.ContextMenu = CreateEtfPinnedContextMenu(rowStrategyCode!);
            textBlock.ContextMenu = border.ContextMenu;
        }

        border.Child = textBlock;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        EtfTableGrid.Children.Add(border);
        if (rowFeedback)
        {
            AnimateTemporaryBackground(border, "#18445C", effectiveFill, 650);
        }
        else
        {
            ApplyEtfValueChangeHighlight(border, rowStrategyCode, sourceColumn, value, effectiveFill);
        }

        return border;
    }

    private static string? ResolveEtfCellToolTip(string? explicitToolTip, string value, int sourceColumn)
    {
        if (!string.IsNullOrWhiteSpace(explicitToolTip))
        {
            return explicitToolTip;
        }

        int minLength = sourceColumn is 2 or 3 or 4 ? 4 : 10;
        return EtfDecisionDisplayAnimationHelper.BuildLongTextToolTip(value, minLength);
    }

    private void WireEtfRowInteraction(IReadOnlyList<Border> rowCells, string strategyCode, bool isSelected)
    {
        if (rowCells.Count == 0 || !CanPinEtfSymbol(strategyCode))
        {
            return;
        }

        string normalizedCode = NormalizePinnedEtfSymbol(strategyCode);
        string hoverFill = isSelected ? "#123A55" : "#0E2A3D";
        foreach (Border cell in rowCells)
        {
            cell.Cursor = Cursors.Hand;
            cell.MouseEnter += (_, _) => AnimateEtfRow(rowCells, hoverFill, 120);
            cell.MouseLeave += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!rowCells.Any(item => item.IsMouseOver))
                    {
                        RestoreEtfRow(rowCells, 120);
                    }
                }, DispatcherPriority.Background);
            };
            cell.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount >= 2)
                {
                    OpenSecurityChart(strategyCode);
                    args.Handled = true;
                    return;
                }

                if (!string.Equals(_selectedEtfStrategyCode, normalizedCode, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedEtfStrategyCode = normalizedCode;
                    BuildEtfTable();
                }
            };
        }
    }

    private void OpenSecurityChart(string strategyCode)
    {
        if (!CanPinEtfSymbol(strategyCode))
        {
            return;
        }

        StrategyConfigRecord? strategy = _strategies.FirstOrDefault(item =>
            string.Equals(item.Code, strategyCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(MarketSymbolNormalizer.DigitsOnly(item.Code), MarketSymbolNormalizer.DigitsOnly(strategyCode), StringComparison.OrdinalIgnoreCase));
        string code = strategy?.Code ?? strategyCode;
        string name = string.IsNullOrWhiteSpace(strategy?.Name) ? code : strategy!.Name;
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo(code, name, code);
        _chartWindowManager.OpenOrActivate(security);
        QueueChartRefresh();
    }

    private void LeftChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        OpenIndexChart(IndexDrawdownChartSeriesBuilder.LeftChartSymbol, "纳指科技指数");
        e.Handled = true;
    }

    private void RightChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        OpenIndexChart(IndexDrawdownChartSeriesBuilder.RightChartSymbol, "纳斯达克100");
        e.Handled = true;
    }

    private void OpenIndexChart(string indexSymbol, string indexName)
    {
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo(indexSymbol, indexName);
        _chartWindowManager.OpenOrActivate(security);
        QueueChartRefresh();
    }

    private static void AnimateEtfRow(IEnumerable<Border> rowCells, string targetFill, int durationMs)
    {
        foreach (Border cell in rowCells)
        {
            string effectiveFill = cell.Tag is EtfCellVisualState { PreserveFillOnRowHover: true } state
                ? state.NormalFill
                : targetFill;
            AnimateBorderBackground(cell, effectiveFill, durationMs);
        }
    }

    private static void RestoreEtfRow(IEnumerable<Border> rowCells, int durationMs)
    {
        foreach (Border cell in rowCells)
        {
            string normalFill = cell.Tag is EtfCellVisualState state ? state.NormalFill : "#071927";
            AnimateBorderBackground(cell, normalFill, durationMs);
        }
    }

    private void ApplyEtfValueChangeHighlight(Border border, string? strategyCode, int sourceColumn, string value, string normalFill)
    {
        if (!IsEtfValueHighlightColumn(sourceColumn))
        {
            return;
        }

        string normalizedCode = NormalizePinnedEtfSymbol(strategyCode);
        if (normalizedCode.Length == 0)
        {
            return;
        }

        string snapshotKey = normalizedCode + "|" + sourceColumn.ToString(CultureInfo.InvariantCulture);
        if (!_etfCellValueSnapshot.TryGetValue(snapshotKey, out string? previousValue))
        {
            _etfCellValueSnapshot[snapshotKey] = value;
            return;
        }

        _etfCellValueSnapshot[snapshotKey] = value;
        EtfValueChangeDirection direction = EtfDecisionDisplayAnimationHelper.DetectValueChange(previousValue, value);
        if (direction == EtfValueChangeDirection.None)
        {
            return;
        }

        string highlightFill = direction == EtfValueChangeDirection.Up ? "#33FF2F3A" : "#3327D17F";
        AnimateTemporaryBackground(border, highlightFill, normalFill, 520);
    }

    private static bool IsEtfValueHighlightColumn(int sourceColumn)
        => sourceColumn is 6 or 7 or 8 or 9 or 10;

    private bool IsRecentPinnedFeedback(string strategyCode)
    {
        string normalizedCode = NormalizePinnedEtfSymbol(strategyCode);
        return normalizedCode.Length > 0
               && string.Equals(_lastPinnedFeedbackSymbol, normalizedCode, StringComparison.OrdinalIgnoreCase)
               && DateTimeOffset.UtcNow - _lastPinnedFeedbackAt <= TimeSpan.FromMilliseconds(900);
    }

    private static void AnimateTemporaryBackground(Border border, string startFill, string endFill, int durationMs)
    {
        var brush = BrushFrom(startFill);
        border.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, CreateColorAnimation(endFill, durationMs));
    }

    private static void AnimateBorderBackground(Border border, string targetFill, int durationMs)
    {
        if (border.Background is not SolidColorBrush brush)
        {
            brush = BrushFrom(targetFill);
            border.Background = brush;
            return;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, CreateColorAnimation(targetFill, durationMs));
    }

    private ContextMenu CreateEtfPinnedContextMenu(string strategyCode)
    {
        bool isPinned = IsEtfPinned(strategyCode);
        var menu = new ContextMenu
        {
            Opacity = 0
        };
        if (TryFindResource("EtfPinnedContextMenuStyle") is Style menuStyle)
        {
            menu.Style = menuStyle;
        }

        menu.Opened += (_, _) => menu.BeginAnimation(UIElement.OpacityProperty, CreateDoubleAnimation(1, 130));

        menu.Items.Add(CreateEtfPinnedMenuItem(isPinned ? "取消置顶" : "置顶该标的", () => ToggleEtfPinnedSymbol(strategyCode)));
        var separator = new Separator();
        if (TryFindResource("EtfPinnedSeparatorStyle") is Style separatorStyle)
        {
            separator.Style = separatorStyle;
        }
        menu.Items.Add(separator);
        MenuItem clearItem = CreateEtfPinnedMenuItem("清空全部置顶", ClearEtfPinnedSymbols);
        clearItem.IsEnabled = _pinnedEtfSymbols.Count > 0;
        menu.Items.Add(clearItem);
        return menu;
    }

    private MenuItem CreateEtfPinnedMenuItem(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header
        };
        if (TryFindResource("EtfPinnedMenuItemStyle") is Style itemStyle)
        {
            item.Style = itemStyle;
        }

        item.Click += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        return item;
    }

    private void WireEtfHeaderDrag(Border element, int displayColumn, string normalFill)
    {
        element.Tag = displayColumn;
        element.Cursor = Cursors.SizeWE;
        element.ToolTip = "拖拽列；单击排序";
        element.MouseEnter += (_, _) =>
        {
            if (!_etfIsDragging)
            {
                element.Background = BrushFrom("#092238");
            }
        };
        element.MouseLeave += (_, _) =>
        {
            if (!_etfIsDragging)
            {
                element.Background = BrushFrom(normalFill);
            }
        };
        element.MouseLeftButtonDown += EtfHeader_MouseLeftButtonDown;
    }

    private void EnsureEtfColumnOrder(IReadOnlyList<int> visibleSourceColumns)
    {
        HashSet<int> visibleSet = visibleSourceColumns.ToHashSet();
        if (_etfColumnOrder.Length == visibleSourceColumns.Count
            && _etfColumnOrder.Distinct().Count() == visibleSourceColumns.Count
            && _etfColumnOrder.All(visibleSet.Contains))
        {
            return;
        }

        var orderedColumns = new List<int>();
        foreach (int existingColumn in _etfColumnOrder)
        {
            if (visibleSet.Contains(existingColumn) && !orderedColumns.Contains(existingColumn))
            {
                orderedColumns.Add(existingColumn);
            }
        }

        foreach (int sourceColumn in visibleSourceColumns)
        {
            if (!orderedColumns.Contains(sourceColumn))
            {
                orderedColumns.Add(sourceColumn);
            }
        }

        _etfColumnOrder = orderedColumns.ToArray();
    }

    private string GetEtfHeaderText(string header, int sourceColumn)
    {
        if (sourceColumn != _etfSortSourceColumn)
        {
            return header;
        }

        return header + (_etfSortDescending ? " ↓" : " ↑");
    }

    private string[][] ApplyEtfSort(string[][] rows)
    {
        if (_etfSortSourceColumn < 0)
        {
            return rows;
        }

        var sorted = (string[][])rows.Clone();
        Array.Sort(sorted, CompareEtfRows);
        return sorted;
    }

    private string[][] ApplyEtfPinnedSort(string[][] rows)
        => EtfDecisionColumnSettings
            .ApplyPinnedSort(rows, _pinnedEtfSymbols, row => row.Length > 0 ? row[0] : string.Empty)
            .ToArray();

    private bool IsEtfPinned(string strategyCode)
    {
        string normalized = NormalizePinnedEtfSymbol(strategyCode);
        return normalized.Length > 0
               && _pinnedEtfSymbols.Any(symbol => string.Equals(symbol, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanPinEtfSymbol(string? strategyCode)
    {
        string normalized = NormalizePinnedEtfSymbol(strategyCode);
        return normalized.Length > 0
               && _strategies.Any(strategy => string.Equals(NormalizePinnedEtfSymbol(strategy.Code), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void ToggleEtfPinnedSymbol(string strategyCode)
    {
        string normalized = NormalizePinnedEtfSymbol(strategyCode);
        if (normalized.Length == 0)
        {
            return;
        }

        List<string> pinned = _pinnedEtfSymbols.ToList();
        int existingIndex = pinned.FindIndex(symbol => string.Equals(symbol, normalized, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            pinned.RemoveAt(existingIndex);
        }
        else
        {
            pinned.Add(normalized);
        }

        _lastPinnedFeedbackSymbol = normalized;
        _lastPinnedFeedbackAt = DateTimeOffset.UtcNow;
        SaveEtfPinnedSymbols(pinned);
    }

    private void ClearEtfPinnedSymbols()
    {
        if (_pinnedEtfSymbols.Count == 0)
        {
            return;
        }

        _lastPinnedFeedbackSymbol = null;
        SaveEtfPinnedSymbols(Array.Empty<string>());
    }

    private void SaveEtfPinnedSymbols(IEnumerable<string> symbols)
    {
        _pinnedEtfSymbols = EtfDecisionColumnSettings.NormalizePinnedSymbols(symbols);
        _repository.SaveAppSetting(
            EtfDecisionColumnSettings.PinnedSymbolsSettingKey,
            EtfDecisionColumnSettings.SerializePinnedSymbols(_pinnedEtfSymbols));
        BuildEtfTable();
    }

    private static string NormalizePinnedEtfSymbol(string? strategyCode)
        => EtfDecisionColumnSettings.NormalizePinnedSymbols(new[] { strategyCode ?? string.Empty }).FirstOrDefault() ?? string.Empty;

    private int CompareEtfRows(string[] left, string[] right)
    {
        string leftValue = left[_etfSortSourceColumn];
        string rightValue = right[_etfSortSourceColumn];
        bool leftBlank = IsBlankEtfValue(leftValue);
        bool rightBlank = IsBlankEtfValue(rightValue);

        if (leftBlank && rightBlank)
        {
            return 0;
        }
        if (leftBlank)
        {
            return 1;
        }
        if (rightBlank)
        {
            return -1;
        }

        int comparison;
        if (TryParseEtfNumber(leftValue, out double leftNumber) && TryParseEtfNumber(rightValue, out double rightNumber))
        {
            comparison = leftNumber.CompareTo(rightNumber);
        }
        else
        {
            comparison = string.Compare(leftValue, rightValue, StringComparison.CurrentCultureIgnoreCase);
        }

        return _etfSortDescending ? -comparison : comparison;
    }

    private static bool IsBlankEtfValue(string value)
    {
        string trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed == "—" || trimmed == "-" || trimmed == "--" || trimmed == "未连接";
    }

    private static bool TryParseEtfNumber(string value, out double number)
    {
        number = 0;
        if (IsBlankEtfValue(value))
        {
            return false;
        }

        var builder = new StringBuilder();
        bool hasDigit = false;
        foreach (char ch in value)
        {
            if (char.IsDigit(ch))
            {
                hasDigit = true;
                builder.Append(ch);
            }
            else if (ch is '-' or '+' or '.')
            {
                builder.Append(ch);
            }
        }

        return hasDigit && double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private void ToggleEtfSort(int sourceColumn)
    {
        if (_etfSortSourceColumn == sourceColumn)
        {
            _etfSortDescending = !_etfSortDescending;
        }
        else
        {
            _etfSortSourceColumn = sourceColumn;
            _etfSortDescending = false;
        }
    }

    private void EtfHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int displayColumn })
        {
            return;
        }

        _etfDragSourceColumn = displayColumn;
        _etfDragTargetColumn = displayColumn;
        _etfDragStart = e.GetPosition(EtfTableGrid);
        _etfIsDragging = false;
        EtfTableGrid.CaptureMouse();
        e.Handled = true;
    }

    private void EtfTableGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_etfDragSourceColumn < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(EtfTableGrid);
        if (!_etfIsDragging)
        {
            double dx = Math.Abs(current.X - _etfDragStart.X);
            double dy = Math.Abs(current.Y - _etfDragStart.Y);
            _etfIsDragging = dx >= EtfHeaderDragThreshold || dy >= EtfHeaderDragThreshold;
            if (_etfIsDragging)
            {
                _etfDragTargetColumn = GetEtfDisplayColumnAt(current.X);
                BuildEtfTable();
                EtfTableGrid.CaptureMouse();
            }
        }

        if (!_etfIsDragging)
        {
            return;
        }

        int target = GetEtfDisplayColumnAt(current.X);
        if (target >= 0 && target != _etfDragTargetColumn)
        {
            _etfDragTargetColumn = target;
            BuildEtfTable();
            EtfTableGrid.CaptureMouse();
        }
        e.Handled = true;
    }

    private void EtfTableGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_etfDragSourceColumn < 0)
        {
            return;
        }

        int target = GetEtfDisplayColumnAt(e.GetPosition(EtfTableGrid).X);
        if (_etfIsDragging && target >= 0 && target != _etfDragSourceColumn)
        {
            MoveEtfColumn(_etfDragSourceColumn, target);
        }
        else if (!_etfIsDragging && _etfDragSourceColumn >= 0 && _etfDragSourceColumn < _etfColumnOrder.Length)
        {
            ToggleEtfSort(_etfColumnOrder[_etfDragSourceColumn]);
        }

        ResetEtfDragState();
        EtfTableGrid.ReleaseMouseCapture();
        BuildEtfTable();
        e.Handled = true;
    }

    private void EtfTableGrid_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_etfDragSourceColumn >= 0 && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            ResetEtfDragState();
            BuildEtfTable();
        }
    }

    private int GetEtfDisplayColumnAt(double x)
    {
        if (EtfTableGrid.ColumnDefinitions.Count == 0)
        {
            return -1;
        }

        double current = 0;
        for (int i = 0; i < EtfTableGrid.ColumnDefinitions.Count; i++)
        {
            double width = EtfTableGrid.ColumnDefinitions[i].ActualWidth;
            if (width <= 0)
            {
                width = EtfTableGrid.ColumnDefinitions[i].Width.Value;
            }

            if (x <= current + width)
            {
                return i;
            }
            current += width;
        }
        return EtfTableGrid.ColumnDefinitions.Count - 1;
    }

    private void AddEtfDropIndicator()
    {
        if (!_etfIsDragging || _etfDragTargetColumn < 0 || EtfTableGrid.ColumnDefinitions.Count == 0)
        {
            return;
        }

        double x = 0;
        for (int i = 0; i < _etfDragTargetColumn; i++)
        {
            x += GetEtfColumnWidth(i);
        }

        if (_etfDragTargetColumn > _etfDragSourceColumn)
        {
            x += GetEtfColumnWidth(_etfDragTargetColumn);
        }

        var indicator = new Border
        {
            Width = 3,
            Height = 29,
            Background = BrushFrom("#3B82F6"),
            Opacity = 0.78,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Max(0, x - 1.5), 0, 0, 0)
        };
        Panel.SetZIndex(indicator, 40);
        Grid.SetColumn(indicator, 0);
        Grid.SetColumnSpan(indicator, Math.Max(1, EtfTableGrid.ColumnDefinitions.Count));
        Grid.SetRow(indicator, 0);
        Grid.SetRowSpan(indicator, Math.Max(1, EtfTableGrid.RowDefinitions.Count));
        EtfTableGrid.Children.Add(indicator);
    }

    private double GetEtfColumnWidth(int displayColumn)
    {
        if (displayColumn < 0 || displayColumn >= EtfTableGrid.ColumnDefinitions.Count)
        {
            return 0;
        }

        double width = EtfTableGrid.ColumnDefinitions[displayColumn].ActualWidth;
        return width > 0 ? width : EtfTableGrid.ColumnDefinitions[displayColumn].Width.Value;
    }

    private void MoveEtfColumn(int fromDisplayIndex, int toDisplayIndex)
    {
        if (fromDisplayIndex < 0 || toDisplayIndex < 0 ||
            fromDisplayIndex >= _etfColumnOrder.Length || toDisplayIndex >= _etfColumnOrder.Length ||
            fromDisplayIndex == toDisplayIndex)
        {
            return;
        }

        int moving = _etfColumnOrder[fromDisplayIndex];
        if (fromDisplayIndex < toDisplayIndex)
        {
            for (int i = fromDisplayIndex; i < toDisplayIndex; i++)
            {
                _etfColumnOrder[i] = _etfColumnOrder[i + 1];
            }
        }
        else
        {
            for (int i = fromDisplayIndex; i > toDisplayIndex; i--)
            {
                _etfColumnOrder[i] = _etfColumnOrder[i - 1];
            }
        }
        _etfColumnOrder[toDisplayIndex] = moving;
    }

    private void ResetEtfDragState()
    {
        _etfDragSourceColumn = -1;
        _etfDragTargetColumn = -1;
        _etfIsDragging = false;
    }

    private void BuildOrderDraftPanel()
    {
        OrderDraftGrid.Children.Clear();
        OrderDraftGrid.RowDefinitions.Clear();
        OrderDraftGrid.ColumnDefinitions.Clear();
        OrderDraftGrid.ClipToBounds = true;

        string[] headers = { "代码", "方向", "来源", "金额", "状态/原因" };
        double[] widths = { 62, 48, 66, 82, 166 };
        string[][] rows = BuildOrderDraftRows(headers.Length);
        double scale = GetColumnScale(OrderDraftGrid, widths);
        foreach (double width in widths)
        {
            OrderDraftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * scale) });
        }

        OrderDraftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (int i = 0; i < rows.Length; i++)
        {
            OrderDraftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        }

        for (int c = 0; c < headers.Length; c++)
        {
            AddOrderDraftCell(headers[c], 0, c, Muted, FontWeights.SemiBold, "#071A29", 13);
        }

        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                AddOrderDraftCell(
                    rows[r][c],
                    r + 1,
                    c,
                    OrderDraftCellColor(c, rows[r][c]),
                    c == 1 || c == 4 ? FontWeights.SemiBold : FontWeights.Normal,
                    r % 2 == 0 ? "#061622" : "#071827",
                    13,
                    c == 4 && rows[r].Length > headers.Length ? rows[r][headers.Length] : null);
            }
        }

        int executableCount = _orderDrafts.Count(draft => draft.IsExecutable);
        double executableAmount = _orderDrafts.Where(draft => draft.IsExecutable).Sum(draft => draft.Amount);
        OrderDraftStatusText.Text = executableCount == 0
            ? "委托草案：当前无可定稿草案"
            : $"委托草案：{executableCount} 条可定稿，金额 {FormatMoney(executableAmount)}";
        FinalizeOrderDraftButton.IsEnabled = executableCount > 0;
        FinalizeOrderDraftButton.ToolTip = executableCount > 0
            ? "冻结当前委托草案，不代表成交，不写入 TradeLog"
            : "当前没有可定稿草案";

        OrderFinalizationStateRecord? latest = _orderFinalizations.FirstOrDefault();
        if (latest is null)
        {
            OrderFinalizationStatusText.Text = "定稿：未定稿；定稿不代表成交，不写入 TradeLog";
            OrderFinalizationStatusText.Foreground = BrushFrom(Muted);
        }
        else
        {
            bool current = _orderDrafts.Any(draft => string.Equals(draft.SnapshotKey, latest.SnapshotKey, StringComparison.Ordinal));
            OrderFinalizationStatusText.Text = current
                ? $"定稿：{latest.FinalizedAt} 已定稿；定稿不代表成交，不写入 TradeLog"
                : $"定稿：{latest.FinalizedAt} 可能失效；定稿不代表成交，不写入 TradeLog";
            OrderFinalizationStatusText.Foreground = BrushFrom(current ? Green : Orange);
        }
    }

    private string[][] BuildOrderDraftRows(int columnCount)
    {
        if (_orderDrafts.Count == 0)
        {
            var empty = CreateEmptyRow(columnCount + 1);
            empty[0] = "暂无委托草案";
            empty[columnCount] = "暂无委托草案";
            return new[] { empty };
        }

        return _orderDrafts
            .OrderByDescending(draft => draft.IsExecutable)
            .ThenByDescending(draft => draft.Amount)
            .Take(2)
            .Select(draft =>
            {
                OrderDraftLegStateRecord? leg = _orderDraftLegs.FirstOrDefault(item => string.Equals(item.DraftKey, draft.DraftKey, StringComparison.OrdinalIgnoreCase));
                string source = draft.Source;
                if (!string.IsNullOrWhiteSpace(leg?.ChannelClass))
                {
                    source += "/" + leg.ChannelClass;
                }

                string reason = !string.IsNullOrWhiteSpace(draft.Reason)
                    ? draft.Reason
                    : !string.IsNullOrWhiteSpace(leg?.Reason)
                        ? leg.Reason
                        : "--";
                string target = leg?.ActualCode ?? draft.StrategyCode;
                string statusReason = BuildOrderDraftStatusReason(draft.DraftStatus, reason);
                string tooltip = BuildOrderDraftStatusTooltip(draft.DraftStatus, reason, draft.StrategyCode, source, target);

                return new[]
                {
                    draft.StrategyCode,
                    draft.Side,
                    source,
                    draft.Amount > 0 ? FormatMoney(draft.Amount) : "--",
                    statusReason,
                    tooltip
                };
            })
            .ToArray();
    }

    private static string BuildOrderDraftStatusReason(string status, string reason)
    {
        string displayStatus = NormalizeOrderDraftStatusLabel(status);
        string displayReason = string.IsNullOrWhiteSpace(reason) ? "--" : reason.Trim();

        return displayStatus switch
        {
            "可委托" => "可委托",
            "已定稿" => "已定稿",
            "部分可委托" => "部分可委托：" + ShortText(displayReason, 14),
            "不可执行" => "不可执行：" + ShortText(displayReason, 14),
            _ => displayReason == "--" ? displayStatus : displayStatus + "：" + ShortText(displayReason, 14)
        };
    }

    private static string BuildOrderDraftStatusTooltip(string status, string reason, string strategyCode, string source, string target)
    {
        string displayStatus = NormalizeOrderDraftStatusLabel(status);
        string displayReason = string.IsNullOrWhiteSpace(reason) ? "--" : reason.Trim();

        var builder = new StringBuilder()
            .Append("状态：").Append(displayStatus).AppendLine()
            .Append("代码：").Append(strategyCode).AppendLine()
            .Append("来源：").Append(string.IsNullOrWhiteSpace(source) ? "--" : source).AppendLine()
            .Append("标的：").Append(string.IsNullOrWhiteSpace(target) ? "--" : target);

        if (displayStatus == "可委托")
        {
            builder.AppendLine()
                .Append("定稿说明：定稿不代表成交，不写入 TradeLog");
        }
        else
        {
            builder.AppendLine()
                .Append("原因：").Append(displayReason);
        }

        return builder.ToString();
    }

    private static string NormalizeOrderDraftStatusLabel(string status)
    {
        return status == "草案" ? "可委托" : string.IsNullOrWhiteSpace(status) ? "--" : status;
    }

    private void AddOrderDraftCell(string value, int row, int col, Color color, FontWeight weight, string fill, double size, string? toolTip = null)
    {
        var border = new Border
        {
            Background = BrushFrom(fill),
            BorderBrush = BrushFrom("#0A2234"),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        OrderDraftGrid.Children.Add(border);

        var textBlock = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 3, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (!string.IsNullOrWhiteSpace(toolTip) && toolTip != "--")
        {
            textBlock.ToolTip = new TextBlock
            {
                Text = toolTip,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320
            };
        }

        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, col);
        OrderDraftGrid.Children.Add(textBlock);
    }

    private static Color OrderDraftCellColor(int column, string value)
    {
        if (column == 1)
        {
            return value == "卖出" ? Green : value == "买入" ? Red : Muted;
        }

        if (column == 4)
        {
            if (value.StartsWith("可委托", StringComparison.Ordinal) || value == "草案")
            {
                return Green;
            }

            if (value.StartsWith("部分可委托", StringComparison.Ordinal))
            {
                return Orange;
            }

            if (value.StartsWith("不可执行", StringComparison.Ordinal))
            {
                return Red;
            }

            if (value.StartsWith("已定稿", StringComparison.Ordinal))
            {
                return Blue;
            }

            return Muted;
        }

        return column == 3 ? Text : Muted;
    }

    private void FinalizeOrderDraftButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int count = _repository.FinalizeOrderDrafts(_orderDrafts, _orderDraftLegs, "主界面定稿；未成交，需按实际成交手动录入 TradeLog");
            if (count == 0)
            {
                OrderFinalizationStatusText.Text = "定稿：没有可定稿草案";
                OrderFinalizationStatusText.Foreground = BrushFrom(Orange);
                return;
            }

            TryWriteRuntimeLog("INFO", "OrderFinalization", "委托草案已定稿", $"定稿 {count} 条；未写入 TradeLog。");
            RefreshLocalDataAndUi();
        }
        catch (Exception ex)
        {
            TryWriteRuntimeLog("ERROR", "OrderFinalization", "委托草案定稿失败", ex.ToString());
            OrderFinalizationStatusText.Text = "定稿失败：" + ShortText(ex.Message, 36);
            OrderFinalizationStatusText.Foreground = BrushFrom(Red);
        }
    }

    private void BuildTradeLog()
    {
        TradeGrid.Children.Clear();
        TradeGrid.RowDefinitions.Clear();
        TradeGrid.ColumnDefinitions.Clear();
        TradeGrid.ClipToBounds = true;
        string[] headers = { "时间", "策略代码", "实际代码", "动作", "数量", "金额", "价格", "状态" };
        double[] widths = { 136, 100, 100, 76, 92, 116, 84, 96 };
        string[][] rows = BuildTradeRows(headers.Length);
        double tradeScale = GetColumnScale(TradeGrid, widths);
        foreach (double width in widths)
        {
            TradeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * tradeScale) });
        }
        TradeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (int i = 0; i < rows.Length; i++)
        {
            TradeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        }
        for (int c = 0; c < headers.Length; c++)
        {
            AddTradeCell(headers[c], 0, c, Muted, FontWeights.SemiBold, "#071A29", 13.5, true);
        }
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                Color color = TradeCellColor(c, rows[r][c]);
                AddTradeCell(rows[r][c], r + 1, c, color, c is 3 or 7 ? FontWeights.SemiBold : FontWeights.Normal, r % 2 == 0 ? "#061622" : "#071827", 13.5, false);
            }
        }
    }

    private string[][] BuildTradeRows(int columnCount)
    {
        if (_tradeLogs.Count == 0)
        {
            var empty = CreateEmptyRow(columnCount);
            empty[0] = "暂无交易日志";
            return new[] { empty };
        }

        return _tradeLogs
            .Take(7)
            .Select(log => new[]
            {
                log.Time,
                log.StrategyCode,
                log.ActualCode ?? "--",
                log.Action,
                FormatQuantity(log.Quantity),
                FormatMoney(log.Amount),
                FormatPrice(log.Price),
                "本地录入"
            })
            .ToArray();
    }

    private void AddTradeCell(string value, int row, int col, Color color, FontWeight weight, string fill, double size, bool isHeader)
    {
        var border = new Border
        {
            Background = BrushFrom(fill),
            BorderBrush = BrushFrom("#0A2234"),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        TradeGrid.Children.Add(border);

        var textBlock = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = isHeader ? new Thickness(5, 0, 4, 0) : new Thickness(5, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, col);
        TradeGrid.Children.Add(textBlock);
    }

    private void AddCell(Grid grid, string value, int row, int col, Color color, FontWeight weight, string fill, double size)
    {
        var border = new Border
        {
            Background = BrushFrom(fill),
            BorderBrush = BrushFrom("#0D2538"),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        grid.Children.Add(border);

        var textBlock = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, col);
        grid.Children.Add(textBlock);
    }

    private static double GetColumnScale(FrameworkElement grid, double[] widths)
    {
        double total = 0;
        foreach (double width in widths)
        {
            total += width;
        }

        if (grid.ActualWidth <= total || total <= 0)
        {
            return 1;
        }

        return grid.ActualWidth / total;
    }

    private string GetEtfCellFill(int column, string value, string rowFill)
    {
        string operationBackground = column == 2
            ? EtfDecisionDisplayAnimationHelper.GetOperationInstructionBackground(value)
            : EtfDecisionDisplayAnimationHelper.OperationNormalBackground;
        return string.Equals(operationBackground, EtfDecisionDisplayAnimationHelper.OperationNormalBackground, StringComparison.OrdinalIgnoreCase)
            ? rowFill
            : operationBackground;
    }

    private Color CellColor(int column, string value, string? strategyCode = null)
    {
        if (column == 1)
        {
            bool hasPosition = !string.IsNullOrWhiteSpace(strategyCode)
                               && _etfRowHasPosition.TryGetValue(strategyCode, out bool rowHasPosition)
                               && rowHasPosition;
            return ColorFrom(EtfDecisionDisplayAnimationHelper.GetEtfNameForeground(hasPosition));
        }

        if (column == 2)
        {
            return ColorFrom(EtfDecisionDisplayAnimationHelper.GetOperationInstructionForeground(value));
        }

        if (EtfDecisionDisplayAnimationHelper.UsesDefaultWhiteForeground(column))
        {
            return ColorFrom(EtfDecisionDisplayAnimationHelper.EtfDefaultDataForeground);
        }

        if (IsBlankEtfValue(value)) return Muted;
        if (column == 3) return value.Contains("启用", StringComparison.Ordinal) ? Green : Muted;
        if (EtfDecisionColumnSettings.UsesSignedNumberColorRule(column))
        {
            return EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value) switch
            {
                FinancialValueTone.Positive => Red,
                FinancialValueTone.Negative => Green,
                _ => Muted
            };
        }

        return Text;
    }

    private sealed record EtfCellVisualState(string NormalFill, bool PreserveFillOnRowHover);

    private static TextAlignment ToWpfTextAlignment(EtfDecisionTextAlignment alignment)
        => alignment == EtfDecisionTextAlignment.Left ? TextAlignment.Left : TextAlignment.Center;

    private static Color TradeCellColor(int column, string value)
    {
        if (IsBlankEtfValue(value)) return Muted;
        if (column == 3)
        {
            return value is "买入" or "入金" ? Red :
                value is "卖出" or "出金" ? Green : Blue;
        }
        if (column == 7) return Green;
        return Text;
    }

    private static string[] CreateEmptyRow(int columnCount)
    {
        string[] row = new string[columnCount];
        Array.Fill(row, "--");
        return row;
    }

    private static double NormalizePercent(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return Math.Clamp(value > 1 ? value / 100.0 : value, 0, 1);
    }

    private MarketQuoteRecord? FindMarketQuote(string? symbol, string? marketType)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        return _marketQuotes
            .Where(quote => string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                            && (marketType is null || string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private StrategyDecisionStateRecord? FindStrategyDecision(string? strategyCode)
    {
        if (string.IsNullOrWhiteSpace(strategyCode))
        {
            return null;
        }

        string digits = MarketSymbolNormalizer.DigitsOnly(strategyCode);
        return _strategyDecisions
            .Where(decision => string.Equals(decision.StrategyCode, strategyCode, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(MarketSymbolNormalizer.DigitsOnly(decision.StrategyCode), digits, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(decision => decision.CalculatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool HasValidCoreIndexHistoryCache()
        => HasDailyHistoryCache(IndexDrawdownChartSeriesBuilder.LeftChartSymbol)
           && HasDailyHistoryCache(IndexDrawdownChartSeriesBuilder.RightChartSymbol);

    private bool HasDailyHistoryCache(string symbol)
    {
        MarketQuoteRecord? history = FindHistoryQuote(symbol, "INDEX");
        return MarketHistoryQuality.IsDailyLike(history?.RawPayload);
    }

    private MarketQuoteRecord? FindHistoryQuote(string? symbol, string marketType)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        return _marketHistory
            .Where(quote => string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private IReadOnlyList<MarketHistoryPoint> GetChartHistoryPoints(string historySymbol)
    {
        MarketQuoteRecord? history = FindHistoryQuote(historySymbol, "INDEX");
        if (string.IsNullOrWhiteSpace(history?.RawPayload))
        {
            return Array.Empty<MarketHistoryPoint>();
        }

        try
        {
            return EastMoneyHistoryParser.ParsePoints(history.RawPayload);
        }
        catch
        {
            return Array.Empty<MarketHistoryPoint>();
        }
    }

    private MarketHistoryPoint? GetLatestIndexChartPoint(string historySymbol)
    {
        MarketQuoteRecord? quote = FindMarketQuote(historySymbol, "INDEX");
        if (quote?.Price is not double price || price <= 0)
        {
            return null;
        }

        DateTime? quoteDate = ParseMarketDate(quote.QuoteTime) ?? ParseMarketDate(quote.ReceivedAt);
        if (!quoteDate.HasValue)
        {
            return null;
        }

        return new MarketHistoryPoint
        {
            Date = quoteDate.Value.Date,
            Open = price,
            Close = price,
            High = price,
            Low = price
        };
    }

    private static DateTime? ParseMarketDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
               || DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed
            : null;
    }

    private static double? MaxValue(params double?[] values)
    {
        double? result = null;
        foreach (double? value in values)
        {
            if (value.HasValue)
            {
                result = result.HasValue ? Math.Max(result.Value, value.Value) : value.Value;
            }
        }

        return result;
    }

    private static double? CalculateDrawdown(double? currentValue, double? highValue)
    {
        if (!currentValue.HasValue || !highValue.HasValue || highValue.Value <= 0)
        {
            return null;
        }

        return currentValue.Value / highValue.Value - 1.0;
    }

    private static bool IsQuoteStale(MarketQuoteRecord quote, TimeSpan maxAge)
    {
        return !DateTime.TryParse(quote.ReceivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime receivedAt)
               || DateTime.Now - receivedAt > maxAge;
    }

    private static Color QuoteColor(double? changePercent)
    {
        if (!changePercent.HasValue)
        {
            return Muted;
        }

        return changePercent.Value > 0 ? Red : changePercent.Value < 0 ? Green : Muted;
    }

    private void SetFinancialColor(TextBlock textBlock, double? value)
    {
        textBlock.Foreground = BrushFrom(AccountTrendMetrics.GetTone(value) switch
        {
            FinancialValueTone.Positive => Red,
            FinancialValueTone.Negative => Green,
            _ => Muted
        });
    }

    private static double? SumNullable(IEnumerable<double?> values)
    {
        double total = 0;
        bool hasValue = false;
        foreach (double? value in values)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                continue;
            }

            hasValue = true;
            total += value.Value;
        }

        return hasValue ? total : null;
    }

    private static string FormatMoney(double value)
        => value.ToString("#,0.00", CultureInfo.InvariantCulture);

    private static string FormatNullableMoney(double? value)
        => value.HasValue ? FormatMoney(value.Value) : "--";

    private static string FormatSignedMoney(double? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        return value.Value > 0
            ? "+" + FormatMoney(value.Value)
            : FormatMoney(value.Value);
    }

    private static string FormatDailyPnl(DailyPnlMetric metric)
    {
        if (!metric.Amount.HasValue)
        {
            return "--";
        }

        return FormatSignedMoney(metric.Amount)
               + "  "
               + (metric.Ratio.HasValue ? FormatSignedRatioFromRatio(metric.Ratio.Value) : "--");
    }

    private static DailyPnlMetric BuildValuationDailyPnlMetric(double amount, double? latestTotalAssets)
    {
        double? denominator = latestTotalAssets.HasValue && latestTotalAssets.Value - amount > 0
            ? latestTotalAssets.Value - amount
            : null;
        return new DailyPnlMetric(amount, denominator.HasValue ? amount / denominator.Value : null);
    }

    private static string FormatQuantity(double value)
        => value.ToString("#,0.####", CultureInfo.InvariantCulture);

    private static string FormatPrice(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatNullableNumber(double? value)
        => value.HasValue ? value.Value.ToString("#,0.####", CultureInfo.InvariantCulture) : "--";

    private static string FormatSignedPercent(double? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        double percent = value.Value;
        return percent > 0
            ? "+" + percent.ToString("0.##", CultureInfo.InvariantCulture) + "%"
            : percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatSignedRatio(double? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        return FormatSignedPercent(value.Value * 100.0);
    }

    private static string FormatSignedRatioFromRatio(double? value)
        => value.HasValue ? FormatSignedPercent(value.Value * 100.0) : "--";

    private static string FormatStrategyPercent(double? value)
        => value.HasValue ? PercentValueParser.FormatPercent(value.Value) : "--";

    private static string FormatPercent(double ratio)
        => $"{ratio * 100:0.##}%";

    private static string FormatPercentValue(double value)
        => FormatPercent(NormalizePercent(value));

    private static string FormatReplayTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : value;
    }

    private static string ShortText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string singleLine = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..Math.Max(0, maxLength - 1)] + "…";
    }

    private void AddText(Canvas canvas, string text, double x, double y, double size, Color color, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color)
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private void AddValueTag(Canvas canvas, double x, double y, string text, Color color)
    {
        AddRectangle(canvas, x, y, 96, 30, ColorToHex(Color.Multiply(color, 0.42f)), ColorToHex(color), 1, 5);
        AddText(canvas, text, x + 13, y + 3, 18, Text, FontWeights.SemiBold);
    }

    private void AddSummaryRow(Canvas canvas, string label, string value, double x, double y)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Width = 136,
            TextAlignment = TextAlignment.Right,
            FontSize = 16,
            Foreground = BrushFrom(Text)
        };
        Canvas.SetLeft(labelBlock, x);
        Canvas.SetTop(labelBlock, y);
        canvas.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom(Green)
        };
        Canvas.SetLeft(valueBlock, x + 148);
        Canvas.SetTop(valueBlock, y);
        canvas.Children.Add(valueBlock);
    }

    private static void AddEllipse(Canvas canvas, double x, double y, double w, double h, string fill, string stroke, double thickness)
    {
        var ellipse = new Ellipse { Width = w, Height = h, Fill = BrushFrom(fill), Stroke = BrushFrom(stroke), StrokeThickness = thickness };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        canvas.Children.Add(ellipse);
    }

    private static void AddRectangle(Canvas canvas, double x, double y, double w, double h, string fill, string stroke, double thickness, double radius = 0)
    {
        var border = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(radius),
            Background = BrushFrom(fill),
            BorderBrush = BrushFrom(stroke),
            BorderThickness = new Thickness(thickness)
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        canvas.Children.Add(border);
    }

    private static Geometry Arc(double cx, double cy, double r, double startAngle, double endAngle)
    {
        Point start = PointOnCircle(cx, cy, r, startAngle);
        Point end = PointOnCircle(cx, cy, r, endAngle);
        bool isLarge = Math.Abs(endAngle - startAngle) > 180;
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private static Point PointOnCircle(double cx, double cy, double r, double angle)
    {
        double rad = Math.PI * angle / 180.0;
        return new Point(cx + Math.Cos(rad) * r, cy + Math.Sin(rad) * r);
    }

    private void WriteDiagnostics()
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrossETF.Terminal.UiShell.Reference");
        Directory.CreateDirectory(dir);
        var diagnostics = new
        {
            ProjectName = "CrossETF.Terminal.UiShell.Reference",
            IsStaticUiShell = false,
            DesignSurfaceWidth = 2560,
            DesignSurfaceHeight = 1440,
            EffectiveDesignSurfaceWidth = Math.Round(DesignSurface.Width, 2),
            UsesViewboxUniform = true,
            TopBarCreated = true,
            SidebarCreated = true,
            CoreCardsCreated = true,
            DrawdownChartsCreated = true,
            EtfTableCreated = true,
            BottomPanelsCreated = true,
            StaticDataOnly = false,
            LocalDataPersistence = true,
            DatabasePath = _database.DatabasePath,
            NoExternalMarketData = false,
            ManualRefreshButton = false,
            CustomBorderlessWindow = true,
            WindowResizeEnabled = ResizeMode == ResizeMode.CanResize,
            StartsMaximized = WindowState == WindowState.Maximized,
            RespectsTaskbarWorkArea = true,
            OverallSuccess = true,
            Errors = Array.Empty<string>()
        };
        File.WriteAllText(System.IO.Path.Combine(dir, "ui-replica-diagnostics.json"), JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkeyService.WmHotkey && wParam.ToInt32() == GlobalHotkeyService.HotkeyId)
        {
            ToggleMainWindowVisibilityByHotkey();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmGetMinMaxInfo)
        {
            AdjustMaximizedWindowBounds(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private void RegisterConfiguredHotkey()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkeyRegistrationResult result = _globalHotkeyService.Apply(hwnd, _hotkeySettings);
        _hotkeyRegistrationStatus = result.StatusText;
        if (!result.Success)
        {
            TryWriteRuntimeLog("WARN", "MainWindow", "界面快捷键注册失败", result.Message ?? result.StatusText);
        }
    }

    private void ToggleMainWindowVisibilityByHotkey()
    {
        if (IsVisible)
        {
            _windowStateBeforeHotkeyHide = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            Hide();
            return;
        }

        Show();
        WindowState = _windowStateBeforeHotkeyHide == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private static void AdjustMaximizedWindowBounds(IntPtr hwnd, IntPtr lParam)
    {
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo
            {
                cbSize = Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                RectInt workArea = monitorInfo.rcWork;
                RectInt monitorArea = monitorInfo.rcMonitor;

                minMaxInfo.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
                minMaxInfo.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
                minMaxInfo.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
                minMaxInfo.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);
                minMaxInfo.ptMaxTrackSize.x = minMaxInfo.ptMaxSize.x;
                minMaxInfo.ptMaxTrackSize.y = minMaxInfo.ptMaxSize.y;
            }
        }

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private static DoubleAnimation CreateDoubleAnimation(double to, int durationMs)
        => new(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

    private static ColorAnimation CreateColorAnimation(string toHex, int durationMs)
        => new(ColorFrom(toHex), TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

    private static SolidColorBrush BrushFrom(string hex) => new(ColorFrom(hex));
    private static SolidColorBrush BrushFrom(Color color) => new(color);
    private static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static string ColorToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInt
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointInt ptReserved;
        public PointInt ptMaxSize;
        public PointInt ptMaxPosition;
        public PointInt ptMinTrackSize;
        public PointInt ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInt
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectInt rcMonitor;
        public RectInt rcWork;
        public int dwFlags;
    }
}
