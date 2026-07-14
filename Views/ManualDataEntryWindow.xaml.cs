using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public enum ManualEntryScope
{
    All,
    PremiumDecision,
    TradeLog,
    SystemSettings
}

public partial class ManualDataEntryWindow : Window
{
    private enum SystemSettingsPage
    {
        General,
        Alerts,
        DataSecurity,
        RuntimeDiagnostics
    }

    private enum SettingsButtonKind
    {
        Secondary,
        Primary,
        Danger
    }

    private sealed record SystemSettingsPageDefinition(
        SystemSettingsPage Page,
        string Icon,
        string Title,
        string Description);

    private sealed record SystemSettingsMenuVisual(
        Border Container,
        Border Indicator,
        TextBlock Icon,
        TextBlock Title,
        TextBlock Description);

    private static readonly Color ManualWindowBackgroundColor = Color.FromRgb(0x05, 0x0B, 0x14);

    private static readonly SystemSettingsPageDefinition[] SystemSettingsPageDefinitions =
    {
        new(SystemSettingsPage.General, "\uE713", "通用设置", "快捷键、版本与程序目录"),
        new(SystemSettingsPage.Alerts, "\uEA8F", "预警与通知", "微信通知、语音与提醒频率"),
        new(SystemSettingsPage.DataSecurity, "\uE896", "数据安全", "数据维护、备份与恢复"),
        new(SystemSettingsPage.RuntimeDiagnostics, "\uE9D9", "运行与诊断", "行情诊断与运行健康")
    };

    private const string StrategyTabHeader = "策略配置";
    private const string AccountTabHeader = "账户状态";
    private const string PositionTabHeader = "持仓";
    private const string OtcTabHeader = "OTCMap";
    private const string BasePositionSettingsTabHeader = "底仓基准设置";
    private const string TradeLogTabHeader = "TradeLog";
    private const string SystemMaintenanceTabHeader = "系统维护";

    private static readonly PercentInputConverter PercentConverter = new();
    private static readonly string[] TradeLogActions =
    {
        "买入", "卖出", "分红", "送股", "拆分", "合并", "除权校准", "CASH", "入金", "出金"
    };

    private static readonly string[] TradeLogSources =
    {
        string.Empty, "场内ETF", "场外替代", "CASH", "手动录入"
    };

    private static readonly string[] TradeLogTiers =
    {
        string.Empty,
        "战略底仓",
        "止盈减仓(留底)",
        "狙击一档",
        "狙击二档",
        "狙击三档",
        "狙击四档",
        "狙击五档",
        "狙击六档",
        "周期结束"
    };

    private const string StrategyColumnLayoutKey = "strategy_config";
    private const string PositionColumnLayoutKey = "position_state";
    private const string OtcColumnLayoutKey = "otc_map";
    private const string TradeLogColumnLayoutKey = "trade_log";

    private readonly LocalDataRepository _repository;
    private readonly string _databasePath;
    private readonly DatabaseBackupService _databaseBackupService;
    private readonly RuntimeHealthMonitor? _runtimeHealthMonitor;
    private readonly ObservableCollection<StrategyConfigRecord> _strategies = new();
    private readonly ObservableCollection<PositionStateRecord> _positions = new();
    private readonly ObservableCollection<OtcChannelRecord> _otcChannels = new();
    private readonly ObservableCollection<TradeLogRecord> _tradeLogs = new();
    private readonly ObservableCollection<DatabaseBackupValidationResult> _databaseBackups = new();
    private readonly HashSet<long> _deletedStrategyIds = new();
    private readonly HashSet<long> _deletedPositionIds = new();
    private readonly HashSet<long> _deletedOtcIds = new();
    private readonly HashSet<long> _deletedTradeLogIds = new();
    private readonly HashSet<long> _loadedTradeLogIds = new();
    private readonly Dictionary<string, TextBox> _accountFields = new();

    private AccountStateRecord _accountState = new();
    private BasePositionSettings _basePositionSettings = BasePositionSettings.Default();
    private ComboBox? _basePositionModeCombo;
    private TextBox? _basePositionRatioBox;
    private TextBox? _basePositionAmountBox;
    private Button? _hotkeyCaptureButton;
    private Button? _hotkeyClearButton;
    private TextBlock? _hotkeyStatusText;
    private CheckBox? _alertPushPlusEnabledBox;
    private PasswordBox? _alertPushPlusTokenBox;
    private CheckBox? _alertVoiceEnabledBox;
    private TextBox? _alertRepeatIntervalBox;
    private TextBox? _alertSevereIntervalBox;
    private TextBox? _alertMarketIntervalBox;
    private TextBlock? _alertStatusText;
    private HotkeySettings _hotkeyUiSettings = HotkeySettings.Default;
    private AlertSettings _alertUiSettings = AlertSettings.Default;
    private bool _isCapturingHotkey;
    private bool _isApplyingTradeLogAutoCalc;
    private bool _isLoadingTradeLogs;
    private bool _isSavingTradeLogs;
    private Button? _tradeLogSaveButton;
    private DataGrid _strategyGrid = null!;
    private DataGrid _positionGrid = null!;
    private DataGrid _otcGrid = null!;
    private DataGrid _tradeLogGrid = null!;
    private bool _isApplyingColumnLayout;
    private DataGrid? _databaseBackupGrid;
    private Button? _createDatabaseBackupButton;
    private Button? _refreshDatabaseBackupListButton;
    private Button? _openDatabaseBackupDirectoryButton;
    private Button? _restoreDatabaseBackupButton;
    private TextBlock? _databaseBackupSummaryText;
    private TextBlock? _databaseBackupOperationText;
    private bool _databaseBackupOperationInProgress;
    private readonly Dictionary<string, TextBlock> _runtimeHealthValueTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<SystemSettingsPage, UIElement> _systemSettingsPages = new();
    private readonly Dictionary<SystemSettingsPage, Button> _systemSettingsMenuButtons = new();
    private readonly Dictionary<SystemSettingsPage, SystemSettingsMenuVisual> _systemSettingsMenuVisuals = new();
    private ContentControl? _systemSettingsPageHost;
    private SystemSettingsPage _currentSystemSettingsPage = SystemSettingsPage.General;
    private TextBlock? _databaseSummaryStatusText;
    private TextBlock? _databaseSummaryLatestBackupText;
    private TextBlock? _databaseSummaryValidCountText;
    private TextBlock? _databaseSummaryAutomaticStatusText;
    private TextBlock? _runtimeSummaryPrivateMemoryText;
    private TextBlock? _runtimeSummaryDispatcherText;
    private TextBlock? _runtimeActionSampleText;
    private TextBlock? _runtimeActionDirectoryText;
    private Button? _exportRuntimeHealthReportButton;

    public ManualDataEntryWindow(LocalDataRepository repository, string databasePath)
        : this(repository, databasePath, ManualEntryScope.All)
    {
    }

    public ManualDataEntryWindow(LocalDataRepository repository, string databasePath, ManualEntryScope scope)
        : this(repository, databasePath, scope, runtimeHealthMonitor: null)
    {
    }

    public ManualDataEntryWindow(
        LocalDataRepository repository,
        string databasePath,
        ManualEntryScope scope,
        RuntimeHealthMonitor? runtimeHealthMonitor)
    {
        _repository = repository;
        _databasePath = databasePath;
        _runtimeHealthMonitor = runtimeHealthMonitor;
        string applicationDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("无法解析数据库目录。", nameof(databasePath));
        _databaseBackupService = new DatabaseBackupService(
            databasePath,
            Path.Combine(applicationDirectory, DatabaseBackupService.BackupDirectoryName),
            Path.Combine(applicationDirectory, DatabaseBackupService.RestoreDirectoryName),
            MainWindow.ResolveDisplayVersion());
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            TryApplyDarkTitleBar();
            ApplyDarkHwndBackground();
        };
        DatabasePathText.Text = _databasePath;
        BuildTabs();
        LoadData();
        ApplyScope(scope);
        if (_runtimeHealthMonitor is not null)
        {
            _runtimeHealthMonitor.SnapshotAvailable += RuntimeHealthMonitor_SnapshotAvailable;
            Closed += ManualDataEntryWindow_RuntimeHealthClosed;
        }
    }

    public ManualEntryScope Scope { get; private set; } = ManualEntryScope.All;

    public event EventHandler? DataSaved;

    public Func<HotkeySettings, HotkeySettingsSaveResult>? SaveHotkeySettingsRequested { get; set; }

    public void ApplyScope(ManualEntryScope scope)
    {
        Scope = scope;
        string title = GetWindowTitle(scope);
        Title = title;
        WindowTitleText.Text = title;
        DatabasePathText.Text = scope == ManualEntryScope.SystemSettings
            ? "管理数据、备份、通知与系统运行状态"
            : _databasePath;
        EntryTabs.Style = (Style)FindResource(
            scope == ManualEntryScope.SystemSettings
                ? "EntryHeaderlessTabControlStyle"
                : "EntryNormalTabControlStyle");

        HashSet<string> visibleHeaders = GetVisibleTabHeaders(scope).ToHashSet(StringComparer.Ordinal);
        foreach (TabItem tab in EntryTabs.Items.OfType<TabItem>())
        {
            string header = Convert.ToString(tab.Header, CultureInfo.InvariantCulture) ?? string.Empty;
            bool visible = visibleHeaders.Contains(header);
            tab.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            tab.IsEnabled = visible;
        }

        SelectTab(GetDefaultTabHeader(scope));
    }

    public static IReadOnlyList<string> GetVisibleTabHeaders(ManualEntryScope scope)
        => scope switch
        {
            ManualEntryScope.PremiumDecision => new[] { StrategyTabHeader, OtcTabHeader, BasePositionSettingsTabHeader },
            ManualEntryScope.TradeLog => new[] { TradeLogTabHeader },
            ManualEntryScope.SystemSettings => new[] { SystemMaintenanceTabHeader },
            _ => new[] { StrategyTabHeader, AccountTabHeader, PositionTabHeader, OtcTabHeader, BasePositionSettingsTabHeader, TradeLogTabHeader, SystemMaintenanceTabHeader }
        };

    public static string GetDefaultTabHeader(ManualEntryScope scope)
        => scope switch
        {
            ManualEntryScope.PremiumDecision => StrategyTabHeader,
            ManualEntryScope.TradeLog => TradeLogTabHeader,
            ManualEntryScope.SystemSettings => SystemMaintenanceTabHeader,
            _ => StrategyTabHeader
        };

    public static string GetWindowTitle(ManualEntryScope scope)
        => scope switch
        {
            ManualEntryScope.PremiumDecision => "溢价决策配置",
            ManualEntryScope.TradeLog => "交易日志录入",
            ManualEntryScope.SystemSettings => "系统设置",
            _ => "本地数据手动录入"
        };

    public static IReadOnlyList<string> GetSystemSettingsPageTitles()
        => SystemSettingsPageDefinitions.Select(definition => definition.Title).ToArray();

    public static IReadOnlyList<string> GetSystemSettingsPageDescriptions()
        => SystemSettingsPageDefinitions.Select(definition => definition.Description).ToArray();

    public static string GetDefaultSystemSettingsPageTitle()
        => SystemSettingsPageDefinitions[0].Title;

    private void SelectTab(string header)
    {
        foreach (TabItem tab in EntryTabs.Items.OfType<TabItem>())
        {
            if (tab.Visibility == Visibility.Visible
                && string.Equals(Convert.ToString(tab.Header, CultureInfo.InvariantCulture), header, StringComparison.Ordinal))
            {
                EntryTabs.SelectedItem = tab;
                return;
            }
        }

        EntryTabs.SelectedItem = EntryTabs.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
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
            // Keep the native title bar unchanged on Windows builds without this DWM attribute.
        }
    }

    private void ApplyDarkHwndBackground()
    {
        try
        {
            if (PresentationSource.FromVisual(this) is HwndSource source
                && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = ManualWindowBackgroundColor;
            }
        }
        catch
        {
            // DWM dark mode still keeps the visible frame dark if HwndSource is unavailable.
        }
    }

    private static int ToColorRef(Color color)
        => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void BuildTabs()
    {
        BuildStrategyTab();
        BuildAccountTab();
        BuildPositionTab();
        BuildOtcTab();
        BuildBasePositionSettingsTab();
        BuildTradeLogTab();
        BuildSystemMaintenanceTab();
    }

    private void BuildStrategyTab()
    {
        _strategyGrid = CreateDataGrid(_strategies);
        AddTextColumn(_strategyGrid, "ID", nameof(StrategyConfigRecord.Id), 64, true);
        AddTextColumn(_strategyGrid, "ETF 代码", nameof(StrategyConfigRecord.Code), 110);
        AddTextColumn(_strategyGrid, "ETF 名称", nameof(StrategyConfigRecord.Name), 160);
        AddTextColumn(_strategyGrid, "指数 secid", nameof(StrategyConfigRecord.IndexSecId), 130);
        AddTextColumn(_strategyGrid, "ETF 高点", nameof(StrategyConfigRecord.EtfHigh), 96);
        AddTextColumn(_strategyGrid, "指数高点", nameof(StrategyConfigRecord.IndexHigh), 96);
        AddPercentColumn(_strategyGrid, "极端溢价", nameof(StrategyConfigRecord.ExtraPrice), 96);
        AddPercentColumn(_strategyGrid, "收益止盈", nameof(StrategyConfigRecord.SellRatio), 96);
        AddPercentColumn(_strategyGrid, "溢价止盈", nameof(StrategyConfigRecord.TakeProfitPrice), 96);
        AddPercentColumn(_strategyGrid, "补仓溢价限制", nameof(StrategyConfigRecord.AddPremiumLimit), 126);
        AddTextColumn(_strategyGrid, "T1 权重", nameof(StrategyConfigRecord.T1Weight), 88);
        AddTextColumn(_strategyGrid, "T2 权重", nameof(StrategyConfigRecord.T2Weight), 88);
        AddTextColumn(_strategyGrid, "T3 权重", nameof(StrategyConfigRecord.T3Weight), 88);
        AddTextColumn(_strategyGrid, "T4 权重", nameof(StrategyConfigRecord.T4Weight), 88);
        AddTextColumn(_strategyGrid, "T5 权重", nameof(StrategyConfigRecord.T5Weight), 88);
        AddTextColumn(_strategyGrid, "T6 权重", nameof(StrategyConfigRecord.T6Weight), 88);
        AddTextColumn(_strategyGrid, "折算系数", nameof(StrategyConfigRecord.AdjFactor), 96);
        AddCheckColumn(_strategyGrid, "启用", nameof(StrategyConfigRecord.Enabled), 70);
        AddTextColumn(_strategyGrid, "创建时间", nameof(StrategyConfigRecord.CreatedAt), 150, true);
        AddTextColumn(_strategyGrid, "更新时间", nameof(StrategyConfigRecord.UpdatedAt), 150, true);

        FinalizeManualEntryGrid(_strategyGrid, StrategyColumnLayoutKey);
        StrategyTabRoot.Children.Add(CreateEditableTab(
            _strategyGrid,
            () => _strategies.Add(new StrategyConfigRecord { Enabled = true }),
            () => BeginEdit(_strategyGrid),
            DeleteSelectedStrategy,
            SaveStrategies));
    }

    private void BuildAccountTab()
    {
        var border = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(22),
            Background = BrushFrom("#071827"),
            BorderBrush = BrushFrom("#24415B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new Grid { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddAccountField(form, "Principal", "本金", 0);
        AddAccountField(form, "CashBalance", "当前现金", 1);
        AddAccountField(form, "TotalAssets", "当前总资产", 2);
        AddAccountField(form, "BasePositionRatio", "底仓完成度", 3);
        AddAccountField(form, "SniperPoolAmount", "狙击资金池", 4);
        AddAccountField(form, "Memo", "备注", 5);

        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        var saveButton = CreateButton("保存当前账户状态");
        saveButton.Margin = new Thickness(150, 14, 0, 0);
        saveButton.HorizontalAlignment = HorizontalAlignment.Left;
        saveButton.Click += (_, _) => SaveAccount();
        Grid.SetRow(saveButton, 6);
        Grid.SetColumn(saveButton, 1);
        form.Children.Add(saveButton);

        scroll.Content = form;
        border.Child = scroll;
        AccountTabRoot.Children.Add(border);
    }

    private void BuildPositionTab()
    {
        _positionGrid = CreateDataGrid(_positions);
        AddTextColumn(_positionGrid, "ID", nameof(PositionStateRecord.Id), 64, true);
        AddTextColumn(_positionGrid, "策略代码", nameof(PositionStateRecord.StrategyCode), 120);
        AddTextColumn(_positionGrid, "实际代码", nameof(PositionStateRecord.ActualCode), 120);
        AddComboColumn(_positionGrid, "来源", nameof(PositionStateRecord.Source), new[] { "场内ETF", "场外替代" }, 110);
        AddTextColumn(_positionGrid, "数量", nameof(PositionStateRecord.Quantity), 110);
        AddTextColumn(_positionGrid, "成本金额", nameof(PositionStateRecord.CostAmount), 120);
        AddTextColumn(_positionGrid, "折算系数", nameof(PositionStateRecord.AdjFactor), 100);
        AddTextColumn(_positionGrid, "创建时间", nameof(PositionStateRecord.CreatedAt), 150, true);
        AddTextColumn(_positionGrid, "更新时间", nameof(PositionStateRecord.UpdatedAt), 150, true);

        FinalizeManualEntryGrid(_positionGrid, PositionColumnLayoutKey);
        PositionTabRoot.Children.Add(CreateEditableTab(
            _positionGrid,
            () => _positions.Add(new PositionStateRecord { Source = "场内ETF", AdjFactor = 1 }),
            () => BeginEdit(_positionGrid),
            DeleteSelectedPosition,
            SavePositions));
    }

    private void BuildOtcTab()
    {
        _otcGrid = CreateDataGrid(_otcChannels);
        AddTextColumn(_otcGrid, "ID", nameof(OtcChannelRecord.Id), 64, true);
        AddTextColumn(_otcGrid, "策略代码", nameof(OtcChannelRecord.StrategyCode), 120);
        AddTextColumn(_otcGrid, "场外基金代码", nameof(OtcChannelRecord.OtcCode), 130);
        AddComboColumn(_otcGrid, "类别", nameof(OtcChannelRecord.ClassType), new[] { "A类", "C类" }, 90);
        AddCheckColumn(_otcGrid, "启用", nameof(OtcChannelRecord.Enabled), 70);
        AddTextColumn(_otcGrid, "单日限额", nameof(OtcChannelRecord.DailyLimit), 110);
        AddTextColumn(_otcGrid, "优先级", nameof(OtcChannelRecord.Priority), 90);
        AddTextColumn(_otcGrid, "最小申购", nameof(OtcChannelRecord.MinBuy), 110);
        AddTextColumn(_otcGrid, "备注", nameof(OtcChannelRecord.Memo), 180);
        AddTextColumn(_otcGrid, "创建时间", nameof(OtcChannelRecord.CreatedAt), 150, true);
        AddTextColumn(_otcGrid, "更新时间", nameof(OtcChannelRecord.UpdatedAt), 150, true);

        FinalizeManualEntryGrid(_otcGrid, OtcColumnLayoutKey);
        OtcTabRoot.Children.Add(CreateEditableTab(
            _otcGrid,
            () => _otcChannels.Add(new OtcChannelRecord { ClassType = "A类", Enabled = true, Priority = 999 }),
            () => BeginEdit(_otcGrid),
            DeleteSelectedOtc,
            SaveOtcChannels));
    }

    private void BuildBasePositionSettingsTab()
    {
        var border = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(22),
            Background = BrushFrom("#071827"),
            BorderBrush = BrushFrom("#24415B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new Grid { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddBasePositionSettingsFields(form, 0);

        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        var saveButton = CreateButton("保存底仓基准设置");
        saveButton.Margin = new Thickness(150, 14, 0, 0);
        saveButton.HorizontalAlignment = HorizontalAlignment.Left;
        saveButton.Click += (_, _) => SaveBasePositionSettings();
        Grid.SetRow(saveButton, 4);
        Grid.SetColumn(saveButton, 1);
        form.Children.Add(saveButton);

        scroll.Content = form;
        border.Child = scroll;
        BasePositionSettingsTabRoot.Children.Add(border);
    }

    private void BuildTradeLogTab()
    {
        _tradeLogGrid = CreateDataGrid(_tradeLogs);
        AddTextColumn(_tradeLogGrid, "ID", nameof(TradeLogRecord.Id), 64, true);
        AddTextColumn(_tradeLogGrid, "时间", nameof(TradeLogRecord.Time), 150);
        AddTextColumn(_tradeLogGrid, "策略代码", nameof(TradeLogRecord.StrategyCode), 120);
        AddTextColumn(_tradeLogGrid, "实际代码", nameof(TradeLogRecord.ActualCode), 120);
        AddTradeLogComboColumn(_tradeLogGrid, "动作", nameof(TradeLogRecord.Action), TradeLogActions, 110);
        AddTradeLogNumberColumn(_tradeLogGrid, "价格", nameof(TradeLogRecord.Price), 96);
        AddTradeLogNumberColumn(_tradeLogGrid, "数量", nameof(TradeLogRecord.Quantity), 96);
        AddTradeLogNumberColumn(_tradeLogGrid, "金额", nameof(TradeLogRecord.Amount), 110);
        AddTradeLogComboColumn(_tradeLogGrid, "档位", nameof(TradeLogRecord.Tier), TradeLogTiers, 140);
        AddTradeLogComboColumn(_tradeLogGrid, "来源", nameof(TradeLogRecord.Source), TradeLogSources, 110);
        AddTradeLogNumberColumn(_tradeLogGrid, "手续费", nameof(TradeLogRecord.Fee), 96);
        AddTextColumn(_tradeLogGrid, "备注", nameof(TradeLogRecord.Memo), 180);
        AddTradeLogNumberColumn(_tradeLogGrid, "净现金流", nameof(TradeLogRecord.NetCashImpact), 110, true);
        AddTradeLogNumberColumn(_tradeLogGrid, "本金", nameof(TradeLogRecord.Principal), 110, true);
        AddTradeLogNumberColumn(_tradeLogGrid, "现金余额", nameof(TradeLogRecord.CashBalance), 110, true);
        AddTradeLogNumberColumn(_tradeLogGrid, "总资产", nameof(TradeLogRecord.TotalAssets), 110, true);
        FinalizeManualEntryGrid(_tradeLogGrid, TradeLogColumnLayoutKey);
        _tradeLogGrid.CellEditEnding += TradeLogGrid_CellEditEnding;
        _tradeLogGrid.CurrentCellChanged += TradeLogGrid_CurrentCellChanged;

        TradeLogTabRoot.Children.Add(CreateEditableTab(
            _tradeLogGrid,
            () => _tradeLogs.Add(new TradeLogRecord { Time = LocalDatabase.NowText(), Action = "买入" }),
            () => BeginEdit(_tradeLogGrid),
            DeleteSelectedTradeLog,
            SaveTradeLogs));
    }

    private void BuildSystemMaintenanceTab()
    {
        if (SystemMaintenanceTabRoot.Children.Count > 0)
        {
            return;
        }

        var root = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0),
            Background = BrushFrom("#050B14")
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(226) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navigationBorder = new Border
        {
            Background = BrushFrom("#061927"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10)
        };
        var navigationPanel = new StackPanel();
        foreach (SystemSettingsPageDefinition definition in SystemSettingsPageDefinitions)
        {
            navigationPanel.Children.Add(CreateSystemSettingsMenuButton(definition));
        }

        navigationBorder.Child = new ScrollViewer
        {
            Background = BrushFrom("#061927"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = navigationPanel
        };
        Grid.SetColumn(navigationBorder, 0);
        root.Children.Add(navigationBorder);

        _systemSettingsPageHost = new ContentControl
        {
            Background = BrushFrom("#050B14"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(_systemSettingsPageHost, 2);
        root.Children.Add(_systemSettingsPageHost);

        _systemSettingsPages[SystemSettingsPage.General] = CreateSystemSettingsPage(
            "通用设置",
            "管理界面快捷键、软件版本和本地数据目录。",
            CreateGeneralSettingsPanel());
        _systemSettingsPages[SystemSettingsPage.Alerts] = CreateSystemSettingsPage(
            "预警与通知",
            "管理微信通知、系统语音和重复提醒频率。",
            CreateAlertSettingsPanel());
        _systemSettingsPages[SystemSettingsPage.DataSecurity] = CreateSystemSettingsPage(
            "数据安全",
            "管理数据库位置、本地备份与安全恢复。",
            CreateDataSecurityPanel());
        _systemSettingsPages[SystemSettingsPage.RuntimeDiagnostics] = CreateSystemSettingsPage(
            "运行与诊断",
            "查看行情数据源、数据库状态和程序运行健康。",
            CreateRuntimeDiagnosticsPanel());

        SystemMaintenanceTabRoot.Children.Add(root);
        SwitchSystemSettingsPage(SystemSettingsPage.General);
        RefreshAlertSettingsUi();
        RefreshHotkeySettingsUi();
        _ = RefreshDatabaseBackupListAsync();
    }

    private Button CreateSystemSettingsMenuButton(SystemSettingsPageDefinition definition)
    {
        var indicator = new Border
        {
            Width = 3,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var icon = CreateMaintenanceText(definition.Icon, 19, "#7FA8C0", FontWeights.Normal);
        icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var title = CreateMaintenanceText(definition.Title, 15, "#D8E8F3", FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        var description = CreateMaintenanceText(definition.Description, 12, "#7E9CAF", FontWeights.Normal, new Thickness(0, 3, 0, 0));
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.TextWrapping = TextWrapping.NoWrap;

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(title);
        textPanel.Children.Add(description);

        var content = new Grid { Background = Brushes.Transparent };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(indicator, 0);
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(textPanel, 2);
        content.Children.Add(indicator);
        content.Children.Add(icon);
        content.Children.Add(textPanel);

        var container = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(0, 8, 7, 8),
            Child = content
        };
        var button = new Button
        {
            Height = 66,
            MinWidth = 0,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = container,
            Tag = definition.Page
        };
        button.Click += (_, _) => SwitchSystemSettingsPage(definition.Page);
        button.MouseEnter += (_, _) =>
        {
            if (_currentSystemSettingsPage != definition.Page)
            {
                container.Background = BrushFrom("#0A2133");
            }
        };
        button.MouseLeave += (_, _) => UpdateSystemSettingsMenuVisual(definition.Page);

        _systemSettingsMenuButtons[definition.Page] = button;
        _systemSettingsMenuVisuals[definition.Page] = new SystemSettingsMenuVisual(container, indicator, icon, title, description);
        return button;
    }

    private void SwitchSystemSettingsPage(SystemSettingsPage page)
    {
        if (_systemSettingsPageHost is null || !_systemSettingsPages.TryGetValue(page, out UIElement? content))
        {
            return;
        }

        _currentSystemSettingsPage = page;
        _systemSettingsPageHost.Content = content;
        if (content is ScrollViewer pageScrollViewer)
        {
            pageScrollViewer.ScrollToTop();
        }

        foreach (SystemSettingsPage menuPage in _systemSettingsMenuButtons.Keys)
        {
            UpdateSystemSettingsMenuVisual(menuPage);
        }
    }

    private void UpdateSystemSettingsMenuVisual(SystemSettingsPage page)
    {
        if (!_systemSettingsMenuVisuals.TryGetValue(page, out SystemSettingsMenuVisual? visual))
        {
            return;
        }

        bool selected = _currentSystemSettingsPage == page;
        visual.Container.Background = BrushFrom(selected ? "#0A2132" : "#061927");
        visual.Container.BorderBrush = BrushFrom(selected ? "#173E55" : "#061927");
        visual.Indicator.Background = selected ? FindResource("EntryAccentBrush") as Brush ?? BrushFrom("#3B82F6") : Brushes.Transparent;
        visual.Icon.Foreground = BrushFrom(selected ? "#3B82F6" : "#7FA8C0");
        visual.Title.Foreground = BrushFrom(selected ? "#FFFFFF" : "#D8E8F3");
        visual.Description.Foreground = BrushFrom("#7E9CAF");
    }

    private static ScrollViewer CreateSystemSettingsPage(string title, string description, UIElement content)
    {
        var page = new StackPanel
        {
            MinWidth = 850,
            MaxWidth = 1220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(36, 28, 36, 28),
            Background = BrushFrom("#050B14")
        };
        page.Children.Add(CreateMaintenanceText(title, 24, "#EAF6FF", FontWeights.SemiBold));
        page.Children.Add(CreateMaintenanceText(description, 14, "#8FAABD", FontWeights.Normal, new Thickness(0, 5, 0, 20)));
        page.Children.Add(content);
        return new ScrollViewer
        {
            Background = BrushFrom("#050B14"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = page
        };
    }

    private UIElement CreateDataMaintenancePanel()
    {
        var card = CreateSystemSettingsCard();
        var content = new StackPanel();
        content.Children.Add(CreateMaintenanceText("数据库位置与维护边界", 17, "#EAF6FF", FontWeights.SemiBold));
        var columns = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });

        Grid paths = CreatePathInformationGrid(
            ("当前数据库路径", _databasePath),
            ("数据目录", Path.GetDirectoryName(_databasePath) ?? "--"));
        Grid.SetColumn(paths, 0);
        columns.Children.Add(paths);

        var boundary = new StackPanel();
        string[] boundaryItems =
        {
            "账户状态和持仓由 TradeLog 自动回放生成。",
            "策略配置、OTCMap 和底仓基准在“溢价决策”维护。",
            "TradeLog 在“交易日志”维护。",
            "备份恢复不触发行情、策略或委托。",
            "TradeLog 是账户和持仓事实源。",
            "系统不自动写入交易记录。"
        };
        for (int index = 0; index < boundaryItems.Length; index++)
        {
            boundary.Children.Add(CreateMaintenanceText(
                "• " + boundaryItems[index],
                13,
                "#C8D8E8",
                FontWeights.Normal,
                new Thickness(0, index == 0 ? 0 : 6, 0, 0)));
        }

        Grid.SetColumn(boundary, 2);
        columns.Children.Add(boundary);
        content.Children.Add(columns);
        card.Child = content;
        return card;
    }

    private UIElement CreateSystemDiagnosticsPanel(MarketDiagnosticsSnapshot snapshot)
    {
        var card = CreateSystemSettingsCard();
        var content = new StackPanel();
        content.Children.Add(CreateMaintenanceText("系统诊断", 17, "#EAF6FF", FontWeights.SemiBold));
        content.Children.Add(CreateInformationGrid(
            ("总体诊断状态", snapshot.Overview.OverallStatus),
            ("行情源概要", $"正常 {snapshot.Overview.NormalSourceCount} / 异常 {snapshot.Overview.AbnormalSourceCount} / 过期 {snapshot.Overview.StaleQuoteCount}"),
            ("本地数据库状态", snapshot.Overview.DatabaseStatus),
            ("账户/盈亏口径概要", $"{snapshot.PnlSummary.ConsistencyStatus}，今日有效项 {snapshot.Overview.IncludedPnlItemCount}"),
            ("最近诊断时间", snapshot.Environment.CurrentTime),
            ("当前程序版本", snapshot.Environment.AppVersion)));
        content.Children.Add(CreateMaintenanceText(
            "更详细的行情、盈亏、配置和运行诊断请在风险中心查看。",
            12,
            "#8FAABD",
            FontWeights.Normal,
            new Thickness(0, 18, 0, 0)));
        card.Child = content;
        return card;
    }

    private UIElement CreateGeneralSettingsPanel()
    {
        var root = new StackPanel();
        root.Children.Add(CreateTwoColumnSettingsGrid(
            CreateHotkeySettingsPanel(),
            CreateSoftwareInformationPanel(),
            2,
            3));

        UIElement directories = CreateLocalDataDirectoryPanel();
        directories.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(directories);

        UIElement boundary = CreateSystemBoundaryPanel();
        boundary.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(boundary);
        return root;
    }

    private UIElement CreateSoftwareInformationPanel()
    {
        Assembly assembly = typeof(ManualDataEntryWindow).Assembly;
        string executablePath = Environment.ProcessPath ?? assembly.Location;
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
        string informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "--";
        var card = CreateSystemSettingsCard();
        var content = new StackPanel();
        string buildIdentifier = string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
            ? informationalVersion
            : versionInfo.ProductVersion;
        content.Children.Add(CreateMaintenanceText("软件信息", 17, "#EAF6FF", FontWeights.SemiBold));
        content.Children.Add(CreateTrimmableInformationGrid(
            ("产品名称", "跨境ETF智能投资决策系统"),
            ("当前版本", "V8.5.0"),
            ("FileVersion", versionInfo.FileVersion ?? "--"),
            ("构建标识", buildIdentifier)));
        card.Child = content;
        return card;
    }

    private UIElement CreateLocalDataDirectoryPanel()
    {
        Assembly assembly = typeof(ManualDataEntryWindow).Assembly;
        string executablePath = Environment.ProcessPath ?? assembly.Location;
        string dataDirectory = Path.GetDirectoryName(_databasePath) ?? "--";
        string healthDirectory = _runtimeHealthMonitor?.HealthDirectory ?? Path.Combine(dataDirectory, "health");
        var card = CreateSystemSettingsCard();
        var content = new StackPanel();
        content.Children.Add(CreateMaintenanceText("本地数据目录", 17, "#EAF6FF", FontWeights.SemiBold));
        content.Children.Add(CreatePathInformationGrid(
            ("程序目录", Path.GetDirectoryName(executablePath) ?? "--"),
            ("数据库路径", _databasePath),
            ("数据目录", dataDirectory),
            ("备份目录", _databaseBackupService.BackupDirectory),
            ("恢复目录", _databaseBackupService.RestoreDirectory),
            ("健康日志目录", healthDirectory)));
        card.Child = content;
        return card;
    }

    private static UIElement CreateSystemBoundaryPanel()
        => new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = CreateMaintenanceText(
                "本系统仅提供投资分析与委托建议，不连接券商，不自动执行交易。",
                13,
                "#C8D8E8",
                FontWeights.SemiBold)
        };

    private UIElement CreateDataSecurityPanel()
    {
        var root = new StackPanel();
        Grid summaries = CreateSettingsSummaryGrid(
            CreateSettingsSummaryCard("数据库状态", "本地数据库", out _databaseSummaryStatusText),
            CreateSettingsSummaryCard("最近有效备份", "--", out _databaseSummaryLatestBackupText),
            CreateSettingsSummaryCard("有效备份数量", "--", out _databaseSummaryValidCountText),
            CreateSettingsSummaryCard("自动备份状态", "等待读取", out _databaseSummaryAutomaticStatusText));
        _databaseSummaryStatusText.Foreground = BrushFrom("#84CC16");
        root.Children.Add(summaries);

        UIElement backup = CreateDatabaseBackupPanel();
        backup.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(backup);

        UIElement maintenance = CreateDataMaintenancePanel();
        maintenance.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(maintenance);

        UIElement restore = CreateDatabaseRestorePanel();
        restore.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(restore);
        UpdateDatabaseBackupButtonState();
        return root;
    }

    private UIElement CreateRuntimeDiagnosticsPanel()
    {
        MarketDiagnosticsSnapshot snapshot = new MarketDiagnosticsSnapshotService(_repository).BuildSnapshot();
        RuntimeHealthSnapshot? runtimeSnapshot = _runtimeHealthMonitor?.CurrentSnapshot;
        var root = new StackPanel();
        Grid summaries = CreateSettingsSummaryGrid(
            CreateSettingsSummaryCard("综合状态", snapshot.Overview.OverallStatus, out TextBlock overallStatusText),
            CreateSettingsSummaryCard(
                "行情概要",
                $"正常 {snapshot.Overview.NormalSourceCount} / 异常 {snapshot.Overview.AbnormalSourceCount} / 过期 {snapshot.Overview.StaleQuoteCount}",
                out _),
            CreateSettingsSummaryCard(
                "私有内存",
                runtimeSnapshot is null ? (_runtimeHealthMonitor is null ? "--" : "等待采样") : FormatRuntimeBytes(runtimeSnapshot.PrivateMemoryBytes),
                out _runtimeSummaryPrivateMemoryText),
            CreateSettingsSummaryCard(
                "界面延迟",
                runtimeSnapshot is null ? (_runtimeHealthMonitor is null ? "--" : "等待采样") : $"{runtimeSnapshot.DispatcherLagMilliseconds:F0} ms",
                out _runtimeSummaryDispatcherText));
        overallStatusText.Foreground = BrushFrom(snapshot.Overview.OverallStatus switch
        {
            "正常" => "#84CC16",
            "警告" => "#F59E0B",
            "异常" => "#EF4444",
            _ => "#EAF6FF"
        });
        root.Children.Add(summaries);

        Grid details = CreateTwoColumnSettingsGrid(
            CreateSystemDiagnosticsPanel(snapshot),
            CreateRuntimeHealthPanel());
        details.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(details);

        UIElement actions = CreateRuntimeHealthActionsPanel();
        actions.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 16, 0, 0));
        root.Children.Add(actions);
        return root;
    }

    private static Border CreateSystemSettingsCard()
        => new()
        {
            Padding = new Thickness(20),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

    private static Grid CreateTwoColumnSettingsGrid(UIElement left, UIElement right, double leftWeight = 1, double rightWeight = 1)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWeight, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Grid CreateSettingsSummaryGrid(params UIElement[] cards)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int index = 0; index < cards.Length; index++)
        {
            int column = index * 2;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cards[index], column);
            grid.Children.Add(cards[index]);
            if (index < cards.Length - 1)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            }
        }

        return grid;
    }

    private static Border CreateSettingsSummaryCard(string title, string value, out TextBlock valueText)
    {
        var content = new StackPanel();
        content.Children.Add(CreateMaintenanceText(title, 13, "#8FAABD", FontWeights.SemiBold));
        valueText = CreateMaintenanceText(value, 19, "#EAF6FF", FontWeights.SemiBold, new Thickness(0, 9, 0, 0));
        valueText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(valueText);
        return new Border
        {
            MinHeight = 94,
            Padding = new Thickness(20),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
    }

    private static Grid CreateInformationGrid(params (string Label, string Value)[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            TextBlock label = CreateMaintenanceText(rows[row].Label, 13, "#8FAABD", FontWeights.SemiBold, new Thickness(0, 0, 16, 0));
            TextBlock value = CreateMaintenanceText(rows[row].Value, 13, "#EAF6FF", FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static Grid CreatePathInformationGrid(params (string Label, string Value)[] rows)
    {
        Grid grid = CreateInformationGrid(rows);
        foreach (TextBlock value in grid.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 1))
        {
            value.FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
            value.TextWrapping = TextWrapping.NoWrap;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.ToolTip = value.Text;
        }

        return grid;
    }

    private static Grid CreateTrimmableInformationGrid(params (string Label, string Value)[] rows)
    {
        Grid grid = CreateInformationGrid(rows);
        foreach (TextBlock value in grid.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 1))
        {
            value.TextWrapping = TextWrapping.NoWrap;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.ToolTip = value.Text;
        }

        return grid;
    }

    private UIElement CreateRuntimeHealthPanel()
    {
        Border border = CreateSystemSettingsCard();
        var root = new StackPanel();
        root.Children.Add(CreateMaintenanceText("运行健康", 17, "#E5EEF8", FontWeights.SemiBold));
        root.Children.Add(CreateMaintenanceText(
            "轻量只读监测：每 30 秒记录进程资源，每 5 秒探测一次 UI 响应；不触发行情、回放、策略或数据库写入。",
            12,
            "#9CAFC3",
            FontWeights.Normal,
            new Thickness(0, 7, 0, 0)));

        var metrics = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        string[] fields =
        {
            "当前状态",
            "已运行时间",
            "当前工作集",
            "当前私有内存",
            ".NET 托管堆",
            "30 分钟内存变化",
            "当前线程数",
            "当前句柄数",
            "最近 Dispatcher 延迟",
            "最大 Dispatcher 延迟",
            "最近主刷新耗时",
            "当前是否正在刷新",
            "当前走势图窗口数量",
            "最近采样时间",
            "健康日志目录",
            "最近状态原因"
        };
        _runtimeHealthValueTexts.Clear();
        for (int row = 0; row < 9; row++)
        {
            metrics.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        }

        for (int index = 0; index < fields.Length; index++)
        {
            bool fullWidth = index >= 14;
            int row = fullWidth ? 7 + (index - 14) : index / 2;
            int labelColumn = fullWidth ? 0 : (index % 2) * 2;
            int valueColumn = fullWidth ? 1 : labelColumn + 1;
            TextBlock label = CreateMaintenanceText(
                fields[index],
                12,
                "#9CAFC3",
                FontWeights.SemiBold,
                new Thickness(labelColumn == 0 ? 0 : 16, row == 0 ? 0 : 8, 12, 0));
            TextBlock value = CreateMaintenanceText(
                "--",
                12,
                "#E5EEF8",
                FontWeights.Normal,
                new Thickness(0, row == 0 ? 0 : 8, 0, 0));
            value.TextWrapping = fields[index] == "健康日志目录" ? TextWrapping.NoWrap : TextWrapping.Wrap;
            if (fields[index] == "健康日志目录")
            {
                value.TextTrimming = TextTrimming.CharacterEllipsis;
                value.ToolTip = value.Text;
            }
            Grid.SetRow(label, row);
            Grid.SetColumn(label, labelColumn);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, valueColumn);
            if (fullWidth)
            {
                Grid.SetColumnSpan(value, 3);
            }

            metrics.Children.Add(label);
            metrics.Children.Add(value);
            _runtimeHealthValueTexts[fields[index]] = value;
        }

        root.Children.Add(metrics);
        border.Child = root;
        return border;
    }

    private UIElement CreateRuntimeHealthActionsPanel()
    {
        Border border = CreateSystemSettingsCard();
        var root = new StackPanel();
        root.Children.Add(CreateMaintenanceText("日志与报告", 17, "#E5EEF8", FontWeights.SemiBold));
        var statusGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock sampleLabel = CreateMaintenanceText("最近采样时间", 12, "#9CAFC3", FontWeights.SemiBold);
        _runtimeActionSampleText = CreateMaintenanceText("--", 12, "#E5EEF8", FontWeights.Normal);
        TextBlock directoryLabel = CreateMaintenanceText("健康日志目录", 12, "#9CAFC3", FontWeights.SemiBold, new Thickness(0, 8, 0, 0));
        _runtimeActionDirectoryText = CreateMaintenanceText(_runtimeHealthMonitor?.HealthDirectory ?? "--", 12, "#E5EEF8", FontWeights.Normal, new Thickness(0, 8, 0, 0));
        _runtimeActionDirectoryText.TextWrapping = TextWrapping.NoWrap;
        _runtimeActionDirectoryText.TextTrimming = TextTrimming.CharacterEllipsis;
        _runtimeActionDirectoryText.ToolTip = _runtimeActionDirectoryText.Text;
        Grid.SetRow(sampleLabel, 0);
        Grid.SetColumn(sampleLabel, 0);
        Grid.SetRow(_runtimeActionSampleText, 0);
        Grid.SetColumn(_runtimeActionSampleText, 1);
        Grid.SetRow(directoryLabel, 1);
        Grid.SetColumn(directoryLabel, 0);
        Grid.SetRow(_runtimeActionDirectoryText, 1);
        Grid.SetColumn(_runtimeActionDirectoryText, 1);
        statusGrid.Children.Add(sampleLabel);
        statusGrid.Children.Add(_runtimeActionSampleText);
        statusGrid.Children.Add(directoryLabel);
        statusGrid.Children.Add(_runtimeActionDirectoryText);
        root.Children.Add(statusGrid);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        Button refreshButton = CreateSettingsButton("刷新状态");
        refreshButton.Click += (_, _) => RefreshRuntimeHealthPanel();
        Button openDirectoryButton = CreateSettingsButton("打开健康日志目录");
        openDirectoryButton.Click += (_, _) => OpenRuntimeHealthDirectory();
        _exportRuntimeHealthReportButton = CreateSettingsButton("导出最近24小时报告");
        _exportRuntimeHealthReportButton.Click += async (_, _) => await ExportRuntimeHealthReportAsync();
        toolbar.Children.Add(refreshButton);
        toolbar.Children.Add(openDirectoryButton);
        toolbar.Children.Add(_exportRuntimeHealthReportButton);
        root.Children.Add(toolbar);
        border.Child = root;
        RefreshRuntimeHealthPanel();
        return border;
    }

    private void RuntimeHealthMonitor_SnapshotAvailable(object? sender, RuntimeHealthSnapshotEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            new Action(() => UpdateRuntimeHealthPanel(e.Snapshot)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ManualDataEntryWindow_RuntimeHealthClosed(object? sender, EventArgs e)
    {
        if (_runtimeHealthMonitor is not null)
        {
            _runtimeHealthMonitor.SnapshotAvailable -= RuntimeHealthMonitor_SnapshotAvailable;
        }
    }

    private void RefreshRuntimeHealthPanel()
    {
        if (_runtimeHealthValueTexts.Count == 0)
        {
            return;
        }

        RuntimeHealthSnapshot? snapshot = _runtimeHealthMonitor?.CurrentSnapshot;
        if (snapshot is null)
        {
            string waitingText = _runtimeHealthMonitor is null ? "--" : "等待采样";
            SetRuntimeHealthValue("当前状态", _runtimeHealthMonitor is null ? "监测服务未连接" : "等待首次采样", "#F59E0B");
            SetRuntimeHealthValue("健康日志目录", _runtimeHealthMonitor?.HealthDirectory ?? "--");
            if (_runtimeSummaryPrivateMemoryText is not null)
            {
                _runtimeSummaryPrivateMemoryText.Text = waitingText;
            }

            if (_runtimeSummaryDispatcherText is not null)
            {
                _runtimeSummaryDispatcherText.Text = waitingText;
            }

            if (_runtimeActionSampleText is not null)
            {
                _runtimeActionSampleText.Text = waitingText;
            }

            if (_runtimeActionDirectoryText is not null)
            {
                _runtimeActionDirectoryText.Text = _runtimeHealthMonitor?.HealthDirectory ?? "--";
                _runtimeActionDirectoryText.ToolTip = _runtimeActionDirectoryText.Text;
            }

            return;
        }

        UpdateRuntimeHealthPanel(snapshot);
    }

    private void UpdateRuntimeHealthPanel(RuntimeHealthSnapshot snapshot)
    {
        string statusText = snapshot.HealthStatus switch
        {
            RuntimeHealthStatus.Normal => "正常",
            RuntimeHealthStatus.Warning => "警告",
            RuntimeHealthStatus.Critical => "严重",
            _ => snapshot.HealthStatus.ToString()
        };
        string statusColor = snapshot.HealthStatus switch
        {
            RuntimeHealthStatus.Normal => "#84CC16",
            RuntimeHealthStatus.Warning => "#F59E0B",
            RuntimeHealthStatus.Critical => "#EF4444",
            _ => "#E5EEF8"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.MonitoringError))
        {
            statusText = "监测异常（业务继续运行）";
            statusColor = "#F59E0B";
        }

        SetRuntimeHealthValue("当前状态", statusText, statusColor);
        SetRuntimeHealthValue("已运行时间", FormatRuntimeDuration(snapshot.UptimeSeconds));
        SetRuntimeHealthValue("当前工作集", FormatRuntimeBytes(snapshot.WorkingSetBytes));
        SetRuntimeHealthValue("当前私有内存", FormatRuntimeBytes(snapshot.PrivateMemoryBytes));
        SetRuntimeHealthValue(".NET 托管堆", FormatRuntimeBytes(snapshot.ManagedHeapBytes));
        SetRuntimeHealthValue(
            "30 分钟内存变化",
            snapshot.PrivateMemoryChange30MinutesBytes.HasValue
                ? FormatSignedRuntimeBytes(snapshot.PrivateMemoryChange30MinutesBytes.Value)
                : "样本不足");
        SetRuntimeHealthValue("当前线程数", snapshot.ThreadCount.ToString(CultureInfo.InvariantCulture));
        SetRuntimeHealthValue("当前句柄数", snapshot.HandleCount.ToString(CultureInfo.InvariantCulture));
        SetRuntimeHealthValue("最近 Dispatcher 延迟", $"{snapshot.DispatcherLagMilliseconds:F0} ms");
        SetRuntimeHealthValue("最大 Dispatcher 延迟", $"{snapshot.MaximumDispatcherLagSinceLastSample:F0} ms");
        SetRuntimeHealthValue(
            "最近主刷新耗时",
            snapshot.LastUiRefreshDurationMilliseconds.HasValue
                ? $"{snapshot.LastUiRefreshDurationMilliseconds.Value:F0} ms"
                : "--");
        SetRuntimeHealthValue("当前是否正在刷新", snapshot.UiRefreshCurrentlyRunning ? "是" : "否");
        SetRuntimeHealthValue("当前走势图窗口数量", snapshot.OpenChartWindowCount.ToString(CultureInfo.InvariantCulture));
        SetRuntimeHealthValue("最近采样时间", snapshot.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        SetRuntimeHealthValue("健康日志目录", _runtimeHealthMonitor?.HealthDirectory ?? "--");
        SetRuntimeHealthValue(
            "最近状态原因",
            snapshot.HealthReasons.Length == 0 ? "无" : string.Join("；", snapshot.HealthReasons));
        if (_runtimeSummaryPrivateMemoryText is not null)
        {
            _runtimeSummaryPrivateMemoryText.Text = FormatRuntimeBytes(snapshot.PrivateMemoryBytes);
        }

        if (_runtimeSummaryDispatcherText is not null)
        {
            _runtimeSummaryDispatcherText.Text = $"{snapshot.DispatcherLagMilliseconds:F0} ms";
        }

        if (_runtimeActionSampleText is not null)
        {
            _runtimeActionSampleText.Text = snapshot.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (_runtimeActionDirectoryText is not null)
        {
            _runtimeActionDirectoryText.Text = _runtimeHealthMonitor?.HealthDirectory ?? "--";
            _runtimeActionDirectoryText.ToolTip = _runtimeActionDirectoryText.Text;
        }
    }

    private void SetRuntimeHealthValue(string field, string value, string color = "#E5EEF8")
    {
        if (_runtimeHealthValueTexts.TryGetValue(field, out TextBlock? text))
        {
            text.Text = value;
            text.Foreground = BrushFrom(color);
            if (field == "健康日志目录")
            {
                text.ToolTip = value;
            }
        }
    }

    private void OpenRuntimeHealthDirectory()
    {
        if (_runtimeHealthMonitor is null)
        {
            MessageBox.Show(this, "运行健康监测服务未连接。", "运行稳定性", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(_runtimeHealthMonitor.HealthDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _runtimeHealthMonitor.HealthDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开健康日志目录：" + ex.Message, "运行稳定性", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportRuntimeHealthReportAsync()
    {
        if (_runtimeHealthMonitor is null || _exportRuntimeHealthReportButton is null)
        {
            MessageBox.Show(this, "运行健康监测服务未连接。", "运行稳定性", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _exportRuntimeHealthReportButton.IsEnabled = false;
        try
        {
            RuntimeHealthReportExportResult result = await Task.Run(
                () => _runtimeHealthMonitor.ExportLast24HoursAsync());
            string message = result.Success
                ? $"{result.Message}\nJSON：{result.JsonPath}\nTXT：{result.TextPath}"
                : result.Message;
            MessageBox.Show(
                this,
                message,
                "运行稳定性",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            _exportRuntimeHealthReportButton.IsEnabled = true;
        }
    }

    private static string FormatRuntimeDuration(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString("d'.'hh':'mm':'ss", CultureInfo.InvariantCulture);

    private static string FormatRuntimeBytes(long bytes)
        => $"{bytes / 1024d / 1024d:F2} MB";

    private static string FormatSignedRuntimeBytes(long bytes)
    {
        string sign = bytes > 0 ? "+" : string.Empty;
        return $"{sign}{bytes / 1024d / 1024d:F2} MB";
    }

    private UIElement CreateDatabaseBackupPanel()
    {
        var root = new StackPanel();

        Border recordsCard = CreateSystemSettingsCard();
        var records = new StackPanel();
        var recordsHeader = new Grid();
        recordsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        recordsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock recordsTitle = CreateMaintenanceText("备份记录", 17, "#E5EEF8", FontWeights.SemiBold);
        Grid.SetColumn(recordsTitle, 0);
        recordsHeader.Children.Add(recordsTitle);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _createDatabaseBackupButton = CreateSettingsButton("立即备份", SettingsButtonKind.Primary);
        _createDatabaseBackupButton.Click += async (_, _) => await CreateManualDatabaseBackupAsync();
        _refreshDatabaseBackupListButton = CreateSettingsButton("刷新列表");
        _refreshDatabaseBackupListButton.Click += async (_, _) => await RefreshDatabaseBackupListAsync();
        _openDatabaseBackupDirectoryButton = CreateSettingsButton("打开备份目录");
        _openDatabaseBackupDirectoryButton.Click += (_, _) => OpenDatabaseBackupDirectory();
        toolbar.Children.Add(_createDatabaseBackupButton);
        toolbar.Children.Add(_refreshDatabaseBackupListButton);
        toolbar.Children.Add(_openDatabaseBackupDirectoryButton);
        Grid.SetColumn(toolbar, 1);
        recordsHeader.Children.Add(toolbar);
        records.Children.Add(recordsHeader);

        records.Children.Add(CreateMaintenanceText(
            "备份仅包含已经保存到数据库的数据；界面中尚未保存的编辑内容不会进入备份。",
            13,
            "#F59E0B",
            FontWeights.Normal,
            new Thickness(0, 8, 0, 0)));
        records.Children.Add(CreateMaintenanceText(
            "当前数据库：" + _databaseBackupService.DatabasePath,
            12,
            "#9CAFC3",
            FontWeights.Normal,
            new Thickness(0, 12, 0, 0)));
        records.Children.Add(CreateMaintenanceText(
            "备份目录：" + _databaseBackupService.BackupDirectory,
            12,
            "#9CAFC3",
            FontWeights.Normal,
            new Thickness(0, 4, 0, 0)));

        _databaseBackupSummaryText = CreateMaintenanceText(
            "正在读取备份状态...",
            13,
            "#C8D8E8",
            FontWeights.Normal,
            new Thickness(0, 10, 0, 0));
        records.Children.Add(_databaseBackupSummaryText);

        _databaseBackupGrid = CreateDataGrid(_databaseBackups);
        _databaseBackupGrid.Height = 280;
        _databaseBackupGrid.Margin = new Thickness(0, 12, 0, 0);
        _databaseBackupGrid.IsReadOnly = true;
        _databaseBackupGrid.SelectionMode = DataGridSelectionMode.Single;
        _databaseBackupGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _databaseBackupGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _databaseBackupGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _databaseBackupGrid.SelectionChanged += (_, _) => UpdateDatabaseBackupButtonState();
        AddTextColumn(_databaseBackupGrid, "备份时间", nameof(DatabaseBackupValidationResult.CreatedAtText), 170, true);
        AddTextColumn(_databaseBackupGrid, "类型", nameof(DatabaseBackupValidationResult.BackupKindText), 100, true);
        AddTextColumn(_databaseBackupGrid, "版本", nameof(DatabaseBackupValidationResult.Version), 90, true);
        AddTextColumn(_databaseBackupGrid, "文件大小", nameof(DatabaseBackupValidationResult.FileSizeText), 100, true);
        AddTextColumn(_databaseBackupGrid, "状态", nameof(DatabaseBackupValidationResult.IntegrityText), 90, true);
        AddTextColumn(_databaseBackupGrid, "文件名", nameof(DatabaseBackupValidationResult.FileName), 220, true);
        var statusStyle = new Style(typeof(TextBlock), (Style)FindResource("EntryDataGridTextBlockStyle"));
        statusStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(DatabaseBackupValidationResult.IsValid)),
            Value = true,
            Setters = { new Setter(TextBlock.ForegroundProperty, BrushFrom("#84CC16")) }
        });
        ((DataGridTextColumn)_databaseBackupGrid.Columns[4]).ElementStyle = statusStyle;
        var fileNameStyle = new Style(typeof(TextBlock), (Style)FindResource("EntryDataGridTextBlockStyle"));
        fileNameStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        fileNameStyle.Setters.Add(new Setter(ToolTipService.ToolTipProperty, new Binding(nameof(DatabaseBackupValidationResult.FileName))));
        ((DataGridTextColumn)_databaseBackupGrid.Columns[5]).ElementStyle = fileNameStyle;
        _databaseBackupGrid.Columns[5].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        records.Children.Add(_databaseBackupGrid);

        _databaseBackupOperationText = CreateMaintenanceText(
            "操作状态：就绪",
            12,
            "#9CAFC3",
            FontWeights.Normal,
            new Thickness(0, 10, 0, 0));
        records.Children.Add(_databaseBackupOperationText);
        recordsCard.Child = records;
        root.Children.Add(recordsCard);
        return root;
    }

    private UIElement CreateDatabaseRestorePanel()
    {
        var restoreDangerArea = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = BrushFrom("#111820"),
            BorderBrush = BrushFrom("#7F1D1D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        var restoreContent = new StackPanel();
        restoreContent.Children.Add(CreateMaintenanceText("恢复数据库", 17, "#FCA5A5", FontWeights.SemiBold));
        restoreContent.Children.Add(CreateMaintenanceText(
            "恢复任务会在下次启动前替换当前数据库，并在替换前创建安全备份。此操作继续使用原有双重确认流程。",
            13,
            "#D6A4A4",
            FontWeights.Normal,
            new Thickness(0, 7, 0, 0)));
        _restoreDatabaseBackupButton = CreateSettingsButton("恢复选中备份", SettingsButtonKind.Danger);
        _restoreDatabaseBackupButton.Click += async (_, _) => await ConfirmAndStageDatabaseRestoreAsync();
        _restoreDatabaseBackupButton.Margin = new Thickness(0, 12, 0, 0);
        _restoreDatabaseBackupButton.HorizontalAlignment = HorizontalAlignment.Right;
        restoreContent.Children.Add(_restoreDatabaseBackupButton);
        restoreDangerArea.Child = restoreContent;
        return restoreDangerArea;
    }

    private async Task CreateManualDatabaseBackupAsync()
    {
        if (_databaseBackupOperationInProgress)
        {
            return;
        }

        SetDatabaseBackupBusy(true, "正在创建一致性备份...");
        try
        {
            DatabaseBackupOperationResult result = await _databaseBackupService.CreateBackupAsync(DatabaseBackupKind.Manual);
            if (!result.Success || result.Backup is null)
            {
                SetDatabaseBackupOperationStatus(result.Message, true);
                MessageBox.Show(this, result.Message, "数据库备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await RefreshDatabaseBackupListCoreAsync();
            string successMessage = string.Join(Environment.NewLine, new[]
            {
                "数据库备份成功。",
                "文件：" + result.Backup.FileName,
                "时间：" + result.Backup.CreatedAtText,
                "大小：" + result.Backup.FileSizeText,
                "当前有效备份：" + _databaseBackups.Count(item => item.IsValid).ToString(CultureInfo.InvariantCulture)
            });
            SetDatabaseBackupOperationStatus(successMessage.Replace(Environment.NewLine, "；", StringComparison.Ordinal), false);
            MessageBox.Show(this, successMessage, "数据库备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetDatabaseBackupOperationStatus("数据库备份失败：" + ex.Message, true);
            MessageBox.Show(this, "数据库备份失败：" + ex.Message, "数据库备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetDatabaseBackupBusy(false, null);
        }
    }

    private async Task RefreshDatabaseBackupListAsync()
    {
        if (_databaseBackupOperationInProgress)
        {
            return;
        }

        SetDatabaseBackupBusy(true, "正在读取本地备份列表...");
        try
        {
            await RefreshDatabaseBackupListCoreAsync();
            SetDatabaseBackupOperationStatus("备份列表已刷新。", false);
        }
        catch (Exception ex)
        {
            SetDatabaseBackupOperationStatus("备份列表读取失败：" + ex.Message, true);
        }
        finally
        {
            SetDatabaseBackupBusy(false, null);
        }
    }

    private async Task RefreshDatabaseBackupListCoreAsync()
    {
        (IReadOnlyList<DatabaseBackupValidationResult> Backups, DatabaseBackupSummary Summary) snapshot = await Task.Run(() =>
        {
            IReadOnlyList<DatabaseBackupValidationResult> backups = _databaseBackupService.ReadBackupList();
            return (backups, _databaseBackupService.BuildSummary(backups));
        });
        IReadOnlyList<DatabaseBackupValidationResult> backups = snapshot.Backups;
        DatabaseBackupSummary summary = snapshot.Summary;
        ReplaceCollection(_databaseBackups, backups);
        if (_databaseBackupSummaryText is not null)
        {
            string latest = summary.LatestValidBackupAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";
            _databaseBackupSummaryText.Text =
                $"最近有效备份：{latest}    有效备份数量：{summary.ValidBackupCount}    自动备份状态：{summary.AutomaticBackupStatus}";
            if (_databaseSummaryLatestBackupText is not null)
            {
                _databaseSummaryLatestBackupText.Text = latest;
            }

            if (_databaseSummaryValidCountText is not null)
            {
                _databaseSummaryValidCountText.Text = summary.ValidBackupCount.ToString(CultureInfo.InvariantCulture);
            }

            if (_databaseSummaryAutomaticStatusText is not null)
            {
                _databaseSummaryAutomaticStatusText.Text = summary.AutomaticBackupStatus;
            }

            if (_databaseSummaryStatusText is not null)
            {
                _databaseSummaryStatusText.Text = "本地数据库";
            }
        }

        UpdateDatabaseBackupButtonState();
    }

    private void OpenDatabaseBackupDirectory()
    {
        try
        {
            Directory.CreateDirectory(_databaseBackupService.BackupDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _databaseBackupService.BackupDirectory,
                UseShellExecute = true
            });
            SetDatabaseBackupOperationStatus("已打开备份目录。", false);
        }
        catch (Exception ex)
        {
            SetDatabaseBackupOperationStatus("打开备份目录失败：" + ex.Message, true);
        }
    }

    private async Task ConfirmAndStageDatabaseRestoreAsync()
    {
        if (_databaseBackupOperationInProgress
            || _databaseBackupGrid?.SelectedItem is not DatabaseBackupValidationResult selected
            || !selected.CanRestore)
        {
            return;
        }

        string firstConfirmation = string.Join(Environment.NewLine, new[]
        {
            "即将准备恢复以下数据库备份：",
            "文件：" + selected.FileName,
            "时间：" + selected.CreatedAtText,
            "版本：V" + selected.Version,
            string.Empty,
            "当前数据库将在下次启动前被替换。",
            "未保存的界面编辑不会保留。",
            "恢复后需要重新打开程序。"
        });
        if (MessageBox.Show(
                this,
                firstConfirmation,
                "确认数据库恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "确认恢复此备份并关闭程序",
                "再次确认数据库恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetDatabaseBackupBusy(true, "正在校验并暂存恢复请求...");
        try
        {
            DatabaseRestoreStageResult result = await _databaseBackupService.StageRestoreAsync(selected.FilePath);
            if (!result.Success)
            {
                SetDatabaseBackupOperationStatus(result.Message, true);
                MessageBox.Show(this, result.Message, "恢复请求准备失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                this,
                "恢复请求已准备，程序将关闭，请重新打开桌面跨境ETF。",
                "恢复请求已准备",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetDatabaseBackupOperationStatus("恢复请求准备失败：" + ex.Message, true);
            MessageBox.Show(this, "恢复请求准备失败：" + ex.Message, "恢复请求准备失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetDatabaseBackupBusy(false, null);
        }
    }

    private void SetDatabaseBackupBusy(bool isBusy, string? status)
    {
        _databaseBackupOperationInProgress = isBusy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetDatabaseBackupOperationStatus(status, false);
        }

        UpdateDatabaseBackupButtonState();
    }

    private void UpdateDatabaseBackupButtonState()
    {
        bool enabled = !_databaseBackupOperationInProgress;
        if (_createDatabaseBackupButton is not null)
        {
            _createDatabaseBackupButton.IsEnabled = enabled;
        }

        if (_refreshDatabaseBackupListButton is not null)
        {
            _refreshDatabaseBackupListButton.IsEnabled = enabled;
        }

        if (_openDatabaseBackupDirectoryButton is not null)
        {
            _openDatabaseBackupDirectoryButton.IsEnabled = enabled;
        }

        if (_restoreDatabaseBackupButton is not null)
        {
            _restoreDatabaseBackupButton.IsEnabled = enabled
                                                     && _databaseBackupGrid?.SelectedItem is DatabaseBackupValidationResult { CanRestore: true };
        }
    }

    private void SetDatabaseBackupOperationStatus(string message, bool isError)
    {
        if (_databaseBackupOperationText is null)
        {
            return;
        }

        _databaseBackupOperationText.Text = "操作状态：" + message;
        _databaseBackupOperationText.Foreground = BrushFrom(isError ? "#EF4444" : "#9CAFC3");
    }

    public void RefreshAlertSettingsUi()
    {
        AlertSettings settings;
        try
        {
            settings = _repository.ReadAlertSettings();
        }
        catch
        {
            settings = AlertSettings.Default;
        }

        ApplyAlertSettingsToUi(settings);
    }

    private UIElement CreateAlertSettingsPanel()
    {
        var root = new StackPanel();

        Border wechatCard = CreateSystemSettingsCard();
        wechatCard.MinHeight = 210;
        var wechatPanel = new StackPanel();
        wechatPanel.Children.Add(CreateMaintenanceText("微信通知", 17, "#E5EEF8", FontWeights.SemiBold));

        _alertPushPlusEnabledBox = CreateAlertCheckBox("启用微信预警");
        _alertPushPlusEnabledBox.Margin = new Thickness(0, 14, 0, 0);
        wechatPanel.Children.Add(_alertPushPlusEnabledBox);

        var tokenRow = CreateAlertSettingRow("PushPlus Token", out PasswordBox tokenBox);
        _alertPushPlusTokenBox = tokenBox;
        wechatPanel.Children.Add(tokenRow);

        Button testWechatButton = CreateSettingsButton("测试微信");
        testWechatButton.Width = 112;
        testWechatButton.Margin = new Thickness(0, 14, 0, 0);
        testWechatButton.HorizontalAlignment = HorizontalAlignment.Right;
        testWechatButton.Click += async (_, _) => await TestWechatAsync();
        wechatPanel.Children.Add(testWechatButton);
        wechatCard.Child = wechatPanel;

        Border voiceCard = CreateSystemSettingsCard();
        voiceCard.MinHeight = 210;
        var voicePanel = new StackPanel();
        voicePanel.Children.Add(CreateMaintenanceText("系统语音", 17, "#E5EEF8", FontWeights.SemiBold));
        _alertVoiceEnabledBox = CreateAlertCheckBox("启用系统语音");
        _alertVoiceEnabledBox.Margin = new Thickness(0, 14, 0, 0);
        voicePanel.Children.Add(_alertVoiceEnabledBox);

        Button testVoiceButton = CreateSettingsButton("测试语音");
        testVoiceButton.Width = 112;
        testVoiceButton.Margin = new Thickness(0, 14, 0, 0);
        testVoiceButton.HorizontalAlignment = HorizontalAlignment.Right;
        testVoiceButton.Click += async (_, _) => await TestVoiceAsync();
        voicePanel.Children.Add(testVoiceButton);
        voiceCard.Child = voicePanel;
        root.Children.Add(CreateTwoColumnSettingsGrid(wechatCard, voiceCard));

        Border intervalCard = CreateSystemSettingsCard();
        intervalCard.Margin = new Thickness(0, 16, 0, 0);
        var intervalPanel = new StackPanel();
        intervalPanel.Children.Add(CreateMaintenanceText("提醒频率", 17, "#E5EEF8", FontWeights.SemiBold));
        var intervalGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid repeatColumn = CreateAlertIntervalColumn("重复提醒间隔", out _alertRepeatIntervalBox);
        Grid severeColumn = CreateAlertIntervalColumn("严重风险间隔", out _alertSevereIntervalBox);
        Grid marketColumn = CreateAlertIntervalColumn("行情异常间隔", out _alertMarketIntervalBox);
        Grid.SetColumn(repeatColumn, 0);
        Grid.SetColumn(severeColumn, 2);
        Grid.SetColumn(marketColumn, 4);
        intervalGrid.Children.Add(repeatColumn);
        intervalGrid.Children.Add(severeColumn);
        intervalGrid.Children.Add(marketColumn);
        intervalPanel.Children.Add(intervalGrid);
        intervalCard.Child = intervalPanel;
        root.Children.Add(intervalCard);

        var saveRow = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        statusPanel.Children.Add(CreateMaintenanceText("设置修改后需要保存才能生效。", 13, "#8FAABD", FontWeights.Normal));
        _alertStatusText = CreateMaintenanceText(string.Empty, 12, "#9CAFC3", FontWeights.Normal, new Thickness(0, 3, 0, 0));
        _alertStatusText.VerticalAlignment = VerticalAlignment.Center;
        statusPanel.Children.Add(_alertStatusText);
        Grid.SetColumn(statusPanel, 0);
        saveRow.Children.Add(statusPanel);
        Button saveButton = CreateSettingsButton("保存预警设置", SettingsButtonKind.Primary);
        saveButton.Margin = new Thickness(0);
        saveButton.HorizontalAlignment = HorizontalAlignment.Right;
        saveButton.Click += (_, _) => SaveAlertSettingsFromUi();
        Grid.SetColumn(saveButton, 1);
        saveRow.Children.Add(saveButton);
        root.Children.Add(saveRow);

        return root;
    }

    private static Grid CreateAlertIntervalColumn(string label, out TextBox textBox)
    {
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock labelBlock = CreateMaintenanceText(label, 13, "#9CAFC3", FontWeights.SemiBold);
        Grid.SetRow(labelBlock, 0);
        column.Children.Add(labelBlock);
        var valueRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        textBox = new TextBox
        {
            Width = 92,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        valueRow.Children.Add(textBox);
        TextBlock unitBlock = CreateMaintenanceText("分钟", 12, "#9CAFC3", FontWeights.Normal, new Thickness(7, 0, 0, 0));
        unitBlock.VerticalAlignment = VerticalAlignment.Center;
        valueRow.Children.Add(unitBlock);
        Grid.SetRow(valueRow, 1);
        column.Children.Add(valueRow);
        return column;
    }

    private static CheckBox CreateAlertCheckBox(string text)
        => new()
        {
            Content = text,
            Foreground = BrushFrom("#E5EEF8"),
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Grid CreateAlertSettingRow(string label, out PasswordBox tokenBox)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 560 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = CreateMaintenanceText(label, 14, "#9CAFC3", FontWeights.SemiBold);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        tokenBox = new PasswordBox
        {
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = BrushFrom("#EAF6FF"),
            Background = BrushFrom("#071827"),
            BorderBrush = BrushFrom("#2A6F93"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(tokenBox, 1);
        row.Children.Add(tokenBox);
        return row;
    }

    private static Grid CreateIntervalRow(string label, string unit, out TextBox textBox)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0), MaxWidth = 380 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = CreateMaintenanceText(label, 14, "#9CAFC3", FontWeights.SemiBold);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        textBox = new TextBox
        {
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);

        var unitBlock = CreateMaintenanceText(unit, 13, "#9CAFC3", FontWeights.Normal, new Thickness(8, 0, 0, 0));
        unitBlock.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(unitBlock, 2);
        row.Children.Add(unitBlock);
        return row;
    }

    private void ApplyAlertSettingsToUi(AlertSettings settings)
    {
        _alertUiSettings = AlertSettings.Normalize(settings);
        if (_alertPushPlusEnabledBox is not null)
        {
            _alertPushPlusEnabledBox.IsChecked = _alertUiSettings.PushPlusEnabled;
        }

        if (_alertPushPlusTokenBox is not null)
        {
            _alertPushPlusTokenBox.Password = _alertUiSettings.PushPlusToken;
        }

        if (_alertVoiceEnabledBox is not null)
        {
            _alertVoiceEnabledBox.IsChecked = _alertUiSettings.VoiceEnabled;
        }

        if (_alertRepeatIntervalBox is not null)
        {
            _alertRepeatIntervalBox.Text = _alertUiSettings.RepeatIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        }

        if (_alertSevereIntervalBox is not null)
        {
            _alertSevereIntervalBox.Text = _alertUiSettings.SevereIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        }

        if (_alertMarketIntervalBox is not null)
        {
            _alertMarketIntervalBox.Text = _alertUiSettings.MarketIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        }

        ShowAlertStatus(string.Empty, false);
    }

    private bool SaveAlertSettingsFromUi()
    {
        if (!TryReadAlertSettingsFromUi(out AlertSettings settings, out string? error))
        {
            ShowAlertStatus(error ?? "预警设置无效。", true);
            return false;
        }

        try
        {
            _repository.SaveAlertSettings(settings);
            ApplyAlertSettingsToUi(settings);
            ShowAlertStatus("预警设置已保存。", false);
            return true;
        }
        catch (Exception ex)
        {
            ShowAlertStatus("预警设置保存失败。", true);
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "预警设置保存失败", ex.ToString());
            return false;
        }
    }

    private bool TryReadAlertSettingsFromUi(out AlertSettings settings, out string? error)
    {
        settings = AlertSettings.Default;
        error = null;
        if (!TryReadInterval(_alertRepeatIntervalBox, "重复提醒间隔", out int repeat, out error)
            || !TryReadInterval(_alertSevereIntervalBox, "严重风险间隔", out int severe, out error)
            || !TryReadInterval(_alertMarketIntervalBox, "行情异常间隔", out int market, out error))
        {
            return false;
        }

        settings = AlertSettings.Normalize(new AlertSettings
        {
            PushPlusEnabled = _alertPushPlusEnabledBox?.IsChecked == true,
            PushPlusToken = _alertPushPlusTokenBox?.Password ?? string.Empty,
            VoiceEnabled = _alertVoiceEnabledBox?.IsChecked == true,
            RepeatIntervalMinutes = repeat,
            SevereIntervalMinutes = severe,
            MarketIntervalMinutes = market
        });
        return true;
    }

    private static bool TryReadInterval(TextBox? box, string label, out int value, out string? error)
    {
        value = 0;
        error = null;
        if (box is null || !int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1 || value > 1440)
        {
            error = $"{label}必须是 1 到 1440 的整数分钟。";
            return false;
        }

        return true;
    }

    private async Task TestWechatAsync()
    {
        if (!SaveAlertSettingsFromUi() || !TryReadAlertSettingsFromUi(out AlertSettings settings, out _))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PushPlusToken))
        {
            ShowAlertStatus("PushPlus Token 未配置，未发送测试微信。", true);
            return;
        }

        ShowAlertStatus("正在发送测试微信...", false);
        try
        {
            using var sender = new PushPlusAlertSender();
            using var voice = new VoiceAlertPlayer();
            var service = new AlertDeliveryService(_repository, sender, voice);
            AlertDeliveryResult result = await service.SendTestWechatAsync(settings);
            ShowAlertStatus(result.Wechat.Success ? "测试微信已发送。" : result.Wechat.Error ?? "测试微信发送失败。", !result.Wechat.Success);
        }
        catch (Exception ex)
        {
            ShowAlertStatus("测试微信发送失败。", true);
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "测试微信发送失败", ex.Message);
        }
    }

    private async Task TestVoiceAsync()
    {
        if (!SaveAlertSettingsFromUi() || !TryReadAlertSettingsFromUi(out AlertSettings settings, out _))
        {
            return;
        }

        ShowAlertStatus("正在播放测试语音...", false);
        try
        {
            using var sender = new PushPlusAlertSender();
            using var voice = new VoiceAlertPlayer();
            var service = new AlertDeliveryService(_repository, sender, voice);
            AlertDeliveryResult result = await service.PlayTestVoiceAsync(settings);
            ShowAlertStatus(result.Voice.Success ? "测试语音已播放。" : result.Voice.Error ?? "测试语音播放失败。", !result.Voice.Success);
        }
        catch (Exception ex)
        {
            ShowAlertStatus("测试语音播放失败。", true);
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "测试语音播放失败", ex.Message);
        }
    }

    private void ShowAlertStatus(string message, bool isError)
    {
        if (_alertStatusText is null)
        {
            return;
        }

        _alertStatusText.Text = message;
        _alertStatusText.Foreground = BrushFrom(isError ? "#EF4444" : "#9CAFC3");
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message, isError);
        }
    }

    public void RefreshHotkeySettingsUi()
    {
        HotkeySettings settings;
        try
        {
            settings = _repository.ReadHotkeySettings();
        }
        catch
        {
            settings = HotkeySettings.Default;
        }

        ApplyHotkeySettingsToUi(settings);
    }

    private UIElement CreateHotkeySettingsPanel()
    {
        var border = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(20),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var panel = new StackPanel();
        panel.Children.Add(CreateMaintenanceText("界面快捷键", 17, "#E5EEF8", FontWeights.SemiBold));

        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = CreateMaintenanceText("显示/隐藏窗口", 14, "#E5EEF8", FontWeights.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        _hotkeyCaptureButton = CreateHotkeyPillButton();
        _hotkeyCaptureButton.Click += (_, _) => BeginHotkeyCapture();
        _hotkeyCaptureButton.PreviewKeyDown += HotkeyCaptureButton_PreviewKeyDown;
        Grid.SetColumn(_hotkeyCaptureButton, 1);
        row.Children.Add(_hotkeyCaptureButton);

        _hotkeyClearButton = CreateHotkeyClearButton();
        _hotkeyClearButton.Click += (_, _) => ClearHotkeySettings();
        Grid.SetColumn(_hotkeyClearButton, 2);
        row.Children.Add(_hotkeyClearButton);

        panel.Children.Add(row);

        _hotkeyStatusText = CreateMaintenanceText(string.Empty, 12, "#9CAFC3", FontWeights.Normal, new Thickness(0, 6, 0, 0));
        panel.Children.Add(_hotkeyStatusText);

        Button restoreButton = CreateSettingsButton("恢复默认设置");
        restoreButton.Margin = new Thickness(0, 14, 0, 0);
        restoreButton.HorizontalAlignment = HorizontalAlignment.Right;
        restoreButton.Click += (_, _) => RestoreDefaultHotkeySettings();
        panel.Children.Add(restoreButton);

        border.Child = panel;
        return border;
    }

    private Button CreateHotkeyPillButton()
    {
        return new Button
        {
            MinWidth = 120,
            Height = 30,
            Padding = new Thickness(16, 0, 16, 0),
            Foreground = BrushFrom("#EAF6FF"),
            Background = BrushFrom("#0B2538"),
            BorderBrush = BrushFrom("#2A6F93"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private Button CreateHotkeyClearButton()
    {
        return new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = BrushFrom("#EAF6FF"),
            Background = BrushFrom("#0B2538"),
            BorderBrush = BrushFrom("#2A6F93"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "清除快捷键"
        };
    }

    private void ApplyHotkeySettingsToUi(HotkeySettings settings)
    {
        _hotkeyUiSettings = settings;
        _isCapturingHotkey = false;
        if (_hotkeyCaptureButton is not null)
        {
            _hotkeyCaptureButton.Content = settings.DisplayText;
        }

        ShowHotkeyStatus(settings.Enabled ? string.Empty : "快捷键未启用。", false);
    }

    private void BeginHotkeyCapture()
    {
        _isCapturingHotkey = true;
        if (_hotkeyCaptureButton is not null)
        {
            _hotkeyCaptureButton.Content = "请按快捷键...";
            _hotkeyCaptureButton.Focus();
        }

        ShowHotkeyStatus("录入中，按 Esc 取消，Backspace/Delete 清除。", false);
    }

    private void HotkeyCaptureButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturingHotkey)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == Key.Escape)
        {
            ApplyHotkeySettingsToUi(_hotkeyUiSettings);
            ShowHotkeyStatus("已取消。", false);
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            ClearHotkeySettings();
            return;
        }

        if (IsPureModifierKey(key))
        {
            return;
        }

        if (!TryCreateHotkeySettingsFromInput(key, Keyboard.Modifiers, out HotkeySettings settings, out string? error))
        {
            ApplyHotkeySettingsToUi(_hotkeyUiSettings);
            ShowHotkeyStatus(error ?? "快捷键无效", true);
            return;
        }

        SaveHotkeySettingsInline(settings, "快捷键已保存并生效。");
    }

    private void ClearHotkeySettings()
    {
        var disabled = new HotkeySettings(false, HotkeyModifierKeys.None, HotkeySettings.Default.Key);
        SaveHotkeySettingsInline(disabled, "快捷键已清除。");
    }

    private void RestoreDefaultHotkeySettings()
        => SaveHotkeySettingsInline(HotkeySettings.Default, "已恢复默认快捷键。");

    private void SaveHotkeySettingsInline(HotkeySettings settings, string successMessage)
    {
        if (!settings.TryValidate(out string? error))
        {
            ApplyHotkeySettingsToUi(_hotkeyUiSettings);
            ShowHotkeyStatus(error ?? "快捷键无效", true);
            return;
        }

        if (SaveHotkeySettingsRequested is null)
        {
            ApplyHotkeySettingsToUi(_hotkeyUiSettings);
            ShowHotkeyStatus("快捷键注册器不可用，未保存。", true);
            return;
        }

        HotkeySettings previous = _hotkeyUiSettings;
        HotkeySettingsSaveResult result = SaveHotkeySettingsRequested(settings);
        if (result.Success)
        {
            ApplyHotkeySettingsToUi(settings);
            ShowHotkeyStatus(successMessage, false);
            return;
        }

        ApplyHotkeySettingsToUi(previous);
        string message = result.StatusText == "快捷键冲突"
            ? "快捷键冲突，未保存"
            : string.IsNullOrWhiteSpace(result.Message)
                ? "快捷键无效"
                : result.Message;
        ShowHotkeyStatus(message, true);
    }

    public static bool TryCreateHotkeySettingsFromInput(
        Key key,
        ModifierKeys modifiers,
        out HotkeySettings settings,
        out string? error)
    {
        settings = HotkeySettings.Default;
        error = null;

        if (IsPureModifierKey(key))
        {
            error = "快捷键无效";
            return false;
        }

        HotkeyModifierKeys hotkeyModifiers = ConvertModifiers(modifiers);
        if (hotkeyModifiers == HotkeyModifierKeys.None)
        {
            error = "快捷键无效";
            return false;
        }

        if (!TryNormalizeCapturedKey(key, out string storageKey))
        {
            error = "快捷键无效";
            return false;
        }

        settings = new HotkeySettings(true, hotkeyModifiers, storageKey);
        return settings.TryValidate(out error);
    }

    private static HotkeyModifierKeys ConvertModifiers(ModifierKeys modifiers)
    {
        HotkeyModifierKeys result = HotkeyModifierKeys.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifierKeys.Ctrl;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifierKeys.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifierKeys.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifierKeys.Win;
        }

        return result;
    }

    private static bool TryNormalizeCapturedKey(Key key, out string storageKey)
    {
        storageKey = string.Empty;
        if (key is >= Key.A and <= Key.Z)
        {
            storageKey = key.ToString();
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            int digit = key - Key.D0;
            storageKey = "D" + digit.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            int functionKey = key - Key.F1 + 1;
            storageKey = "F" + functionKey.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool IsPureModifierKey(Key key)
        => key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin
            or Key.System;

    private void ShowHotkeyStatus(string message, bool isError)
    {
        if (_hotkeyStatusText is null)
        {
            return;
        }

        _hotkeyStatusText.Text = message;
        _hotkeyStatusText.Foreground = BrushFrom(isError ? "#EF4444" : "#9CAFC3");
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message, isError);
        }
    }

    private static TextBlock CreateMaintenanceText(
        string text,
        double fontSize,
        string color,
        FontWeight fontWeight,
        Thickness? margin = null)
        => new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = BrushFrom(color),
            Margin = margin ?? new Thickness(0),
            TextWrapping = TextWrapping.Wrap
        };

    private UIElement CreateEditableTab(DataGrid grid, Action add, Action edit, Action delete, Action save)
    {
        var root = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var addButton = CreateButton("新增");
        addButton.Click += (_, _) => add();
        var editButton = CreateButton("编辑选中");
        editButton.Click += (_, _) => edit();
        var deleteButton = CreateButton("删除选中");
        deleteButton.Click += (_, _) => delete();
        var saveButton = CreateButton("保存");
        saveButton.Click += (_, _) => save();
        if (save.Method.Name == nameof(SaveTradeLogs))
        {
            _tradeLogSaveButton = saveButton;
        }

        toolbar.Children.Add(addButton);
        toolbar.Children.Add(editButton);
        toolbar.Children.Add(deleteButton);
        toolbar.Children.Add(saveButton);
        root.Children.Add(toolbar);

        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        return root;
    }

    private static DataGrid CreateDataGrid<T>(ObservableCollection<T> source)
    {
        return new DataGrid
        {
            ItemsSource = source,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanUserReorderColumns = true,
            Background = BrushFrom("#071827"),
            Foreground = BrushFrom("#E5EEF8"),
            RowBackground = BrushFrom("#061622"),
            AlternatingRowBackground = BrushFrom("#071927"),
            BorderBrush = BrushFrom("#24415B"),
            BorderThickness = new Thickness(1),
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
    }

    private void AddTextColumn(DataGrid grid, string header, string path, double width, bool readOnly = false)
    {
        var elementStyle = (Style)FindResource("EntryDataGridTextBlockStyle");
        var editingStyle = (Style)FindResource("EntryDataGridEditingTextBoxStyle");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
            SortMemberPath = path,
            ElementStyle = elementStyle,
            EditingElementStyle = editingStyle,
            Binding = new Binding(path)
            {
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                TargetNullValue = string.Empty
            }
        });
    }

    private static void AddCheckColumn(DataGrid grid, string header, string path, double width)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            SortMemberPath = path,
            Binding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
    }

    private void AddPercentColumn(DataGrid grid, string header, string path, double width)
    {
        var elementStyle = (Style)FindResource("EntryDataGridTextBlockStyle");
        var editingStyle = (Style)FindResource("EntryDataGridEditingTextBoxStyle");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            SortMemberPath = path,
            ElementStyle = elementStyle,
            EditingElementStyle = editingStyle,
            Binding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                Converter = PercentConverter,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true,
                TargetNullValue = string.Empty
            }
        });
    }

    private void AddComboColumn(DataGrid grid, string header, string path, string[] values, double width)
    {
        var comboStyle = (Style)FindResource("EntryComboBoxStyle");
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            SortMemberPath = path,
            ItemsSource = values,
            ElementStyle = comboStyle,
            EditingElementStyle = comboStyle,
            SelectedItemBinding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
    }

    private void AddTradeLogComboColumn(DataGrid grid, string header, string path, string[] values, double width)
    {
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            SortMemberPath = path,
            ItemsSource = values,
            ElementStyle = (Style)FindResource("TradeLogComboBoxStyle"),
            EditingElementStyle = (Style)FindResource("TradeLogComboBoxStyle"),
            SelectedItemBinding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                TargetNullValue = string.Empty
            }
        });
    }

    private void AddTradeLogNumberColumn(DataGrid grid, string header, string path, double width, bool readOnly = false)
    {
        var elementStyle = (Style)FindResource("EntryDataGridTextBlockStyle");
        var editingStyle = (Style)FindResource("EntryDataGridEditingTextBoxStyle");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
            SortMemberPath = path,
            ElementStyle = elementStyle,
            EditingElementStyle = editingStyle,
            Binding = new Binding(path)
            {
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = readOnly ? UpdateSourceTrigger.Default : UpdateSourceTrigger.LostFocus,
                ValidatesOnExceptions = !readOnly,
                NotifyOnValidationError = !readOnly
            }
        });
    }

    private void FinalizeManualEntryGrid(DataGrid grid, string tabKey)
    {
        grid.Tag = tabKey;
        ApplyManualEntryColumnLayout(grid, tabKey);
        grid.ColumnReordered -= ManualEntryGrid_ColumnReordered;
        grid.ColumnReordered += ManualEntryGrid_ColumnReordered;
    }

    private void ApplyManualEntryColumnLayout(DataGrid grid, string tabKey)
    {
        IReadOnlyList<string> defaultOrder = GetManualEntryColumnKeys(grid);
        if (defaultOrder.Count == 0)
        {
            return;
        }

        string? savedOrder = null;
        try
        {
            savedOrder = _repository.ReadAppSetting(ManualEntryColumnLayoutService.BuildSettingKey(tabKey));
        }
        catch (Exception ex)
        {
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "读取列顺序失败", ex.ToString());
        }

        IReadOnlyList<string> resolvedOrder = ManualEntryColumnLayoutService.ResolveOrder(defaultOrder, savedOrder);
        Dictionary<string, DataGridColumn> columnsByKey = grid.Columns
            .Select(column => new { Key = GetManualEntryColumnKey(column), Column = column })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);

        _isApplyingColumnLayout = true;
        try
        {
            int displayIndex = 0;
            foreach (string key in resolvedOrder)
            {
                if (columnsByKey.TryGetValue(key, out DataGridColumn? column))
                {
                    column.DisplayIndex = displayIndex++;
                }
            }
        }
        catch (Exception ex)
        {
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "应用列顺序失败", ex.ToString());
            SetStatus("列顺序恢复失败，已使用默认顺序。", true);
        }
        finally
        {
            _isApplyingColumnLayout = false;
        }
    }

    private void ManualEntryGrid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (_isApplyingColumnLayout || sender is not DataGrid grid || grid.Tag is not string tabKey)
        {
            return;
        }

        SaveManualEntryColumnLayout(grid, tabKey);
    }

    private void SaveManualEntryColumnLayout(DataGrid grid, string tabKey)
    {
        try
        {
            IReadOnlyList<string> defaultOrder = GetManualEntryColumnKeys(grid);
            string[] orderedKeys = grid.Columns
                .OrderBy(column => column.DisplayIndex)
                .Select(GetManualEntryColumnKey)
                .Where(key => key.Length > 0)
                .ToArray();
            string value = ManualEntryColumnLayoutService.SerializeOrder(orderedKeys, defaultOrder);
            _repository.SaveAppSetting(ManualEntryColumnLayoutService.BuildSettingKey(tabKey), value);
            SetStatus("列顺序已保存。", false);
        }
        catch (Exception ex)
        {
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "保存列顺序失败", ex.ToString());
            SetStatus("列顺序保存失败，数据编辑不受影响。", true);
        }
    }

    private static IReadOnlyList<string> GetManualEntryColumnKeys(DataGrid grid)
        => grid.Columns
            .Select(GetManualEntryColumnKey)
            .Where(key => key.Length > 0)
            .ToArray();

    private static string GetManualEntryColumnKey(DataGridColumn column)
        => string.IsNullOrWhiteSpace(column.SortMemberPath)
            ? column.Header?.ToString()?.Trim() ?? string.Empty
            : column.SortMemberPath.Trim();

    private void AddAccountField(Grid form, string key, string label, int row)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrushFrom("#9CAFC3")
        };
        Grid.SetRow(labelBlock, row);
        form.Children.Add(labelBlock);

        var textBox = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        form.Children.Add(textBox);
        _accountFields[key] = textBox;
    }

    private void AddBasePositionSettingsFields(Grid form, int startRow)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        var section = new TextBlock
        {
            Text = "底仓基准设置",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom("#E5EEF8")
        };
        Grid.SetRow(section, startRow);
        Grid.SetColumnSpan(section, 2);
        form.Children.Add(section);

        AddBasePositionModeField(form, startRow + 1);
        _basePositionRatioBox = AddBasePositionTextField(form, "比例", startRow + 2);
        _basePositionAmountBox = AddBasePositionTextField(form, "固定金额", startRow + 3);
    }

    private void AddBasePositionModeField(Grid form, int row)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        var labelBlock = new TextBlock
        {
            Text = "模式",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrushFrom("#9CAFC3")
        };
        Grid.SetRow(labelBlock, row);
        form.Children.Add(labelBlock);

        _basePositionModeCombo = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[] { "按本金比例", "固定金额" },
            SelectedIndex = 0,
            Foreground = BrushFrom("#E5EEF8"),
            Background = BrushFrom("#071827"),
            BorderBrush = BrushFrom("#24415B")
        };
        _basePositionModeCombo.SelectionChanged += (_, _) => UpdateBasePositionSettingsEditors();
        Grid.SetRow(_basePositionModeCombo, row);
        Grid.SetColumn(_basePositionModeCombo, 1);
        form.Children.Add(_basePositionModeCombo);
    }

    private static TextBox AddBasePositionTextField(Grid form, string label, int row)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrushFrom("#9CAFC3")
        };
        Grid.SetRow(labelBlock, row);
        form.Children.Add(labelBlock);

        var textBox = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        form.Children.Add(textBox);
        return textBox;
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Content = text,
            MinWidth = 78,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Foreground = BrushFrom("#E5EEF8"),
            Background = BrushFrom("#0B2538"),
            BorderBrush = BrushFrom("#24415B"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private Button CreateSettingsButton(string text, SettingsButtonKind kind = SettingsButtonKind.Secondary)
    {
        string styleKey = kind switch
        {
            SettingsButtonKind.Primary => "SettingsPrimaryButtonStyle",
            SettingsButtonKind.Danger => "SettingsDangerButtonStyle",
            _ => "SettingsSecondaryButtonStyle"
        };
        return new Button
        {
            Content = text,
            Style = (Style)FindResource(styleKey)
        };
    }

    private void LoadData()
    {
        try
        {
            ReplaceCollection(_strategies, _repository.ReadStrategyConfigs());
            ReplaceCollection(_positions, _repository.ReadPositionStates());
            ReplaceCollection(_otcChannels, _repository.ReadOtcChannels());
            _isLoadingTradeLogs = true;
            try
            {
                IReadOnlyList<TradeLogRecord> tradeLogs = _repository.ReadTradeLogs();
                ReplaceCollection(_tradeLogs, tradeLogs);
                _loadedTradeLogIds.Clear();
                foreach (long id in tradeLogs.Where(record => record.Id > 0).Select(record => record.Id))
                {
                    _loadedTradeLogIds.Add(id);
                }
            }
            finally
            {
                _isLoadingTradeLogs = false;
            }
            _accountState = _repository.ReadLatestAccountState() ?? new AccountStateRecord();
            _basePositionSettings = _repository.ReadBasePositionSettings();
            LoadAccountFields();
            ClearDeletedIds();
            SetStatus("已载入本地数据。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"读取失败：{ex.Message}", true);
        }
    }

    private void LoadAccountFields()
    {
        _accountFields["Principal"].Text = FormatEditorNumber(_accountState.Principal);
        _accountFields["CashBalance"].Text = FormatEditorNumber(_accountState.CashBalance);
        _accountFields["TotalAssets"].Text = FormatEditorNumber(_accountState.TotalAssets);
        _accountFields["BasePositionRatio"].Text = FormatEditorNumber(_accountState.BasePositionRatio);
        _accountFields["SniperPoolAmount"].Text = FormatEditorNumber(_accountState.SniperPoolAmount);
        _accountFields["Memo"].Text = _accountState.Memo ?? string.Empty;
        if (_basePositionModeCombo is not null)
        {
            _basePositionModeCombo.SelectedIndex = _basePositionSettings.Mode == BasePositionSettings.AmountMode ? 1 : 0;
        }
        if (_basePositionRatioBox is not null)
        {
            _basePositionRatioBox.Text = PercentValueParser.FormatPercent(_basePositionSettings.Ratio);
        }
        if (_basePositionAmountBox is not null)
        {
            _basePositionAmountBox.Text = FormatEditorNumber(_basePositionSettings.FixedAmount);
        }
        UpdateBasePositionSettingsEditors();
    }

    private void SaveAccount()
    {
        SaveWithStatus("账户状态已保存。", () =>
        {
            _accountState.Principal = ParseDouble(_accountFields["Principal"].Text, "本金");
            _accountState.CashBalance = ParseDouble(_accountFields["CashBalance"].Text, "当前现金");
            _accountState.TotalAssets = ParseDouble(_accountFields["TotalAssets"].Text, "当前总资产");
            _accountState.BasePositionRatio = ParseDouble(_accountFields["BasePositionRatio"].Text, "底仓完成度");
            _accountState.SniperPoolAmount = ParseDouble(_accountFields["SniperPoolAmount"].Text, "狙击资金池");
            _accountState.Memo = _accountFields["Memo"].Text;
            _repository.SaveAccountState(_accountState);
            LoadData();
        });
    }

    private void SaveBasePositionSettings()
    {
        SaveWithStatus("底仓基准设置已保存。", () =>
        {
            _basePositionSettings = ReadBasePositionSettingsFromEditors();
            _repository.SaveBasePositionSettings(_basePositionSettings);
            LoadData();
        });
    }

    private BasePositionSettings ReadBasePositionSettingsFromEditors()
    {
        string mode = _basePositionModeCombo?.SelectedIndex == 1
            ? BasePositionSettings.AmountMode
            : BasePositionSettings.RatioMode;

        if (!BasePositionSettingsService.TryParseRatio(_basePositionRatioBox?.Text, out double ratio, out string? ratioError))
        {
            throw new InvalidOperationException(ratioError ?? "底仓基准比例格式错误。");
        }

        double fixedAmount = ParseDouble(_basePositionAmountBox?.Text ?? string.Empty, "底仓固定金额");
        if (fixedAmount < 0)
        {
            throw new InvalidOperationException("底仓固定金额不能小于 0。");
        }

        return BasePositionSettingsService.Normalize(new BasePositionSettings
        {
            Mode = mode,
            Ratio = ratio,
            FixedAmount = fixedAmount
        });
    }

    private void UpdateBasePositionSettingsEditors()
    {
        bool amountMode = _basePositionModeCombo?.SelectedIndex == 1;
        if (_basePositionRatioBox is not null)
        {
            _basePositionRatioBox.IsEnabled = !amountMode;
            _basePositionRatioBox.Opacity = amountMode ? 0.72 : 1.0;
        }

        if (_basePositionAmountBox is not null)
        {
            _basePositionAmountBox.IsEnabled = amountMode;
            _basePositionAmountBox.Opacity = amountMode ? 1.0 : 0.72;
        }
    }

    private void SaveStrategies()
    {
        SaveWithStatus("策略配置已保存。", () =>
        {
            CommitGridEdits(_strategyGrid, "策略配置百分比字段无法保存，请检查收益止盈、溢价止盈、补仓溢价限制。");
            foreach (long id in _deletedStrategyIds)
            {
                _repository.DeleteStrategyConfig(id);
            }

            foreach (StrategyConfigRecord record in _strategies)
            {
                if (record.Id <= 0 && string.IsNullOrWhiteSpace(record.Code) && string.IsNullOrWhiteSpace(record.Name))
                {
                    continue;
                }
                _repository.SaveStrategyConfig(record);
            }
            LoadData();
        });
    }

    private void SavePositions()
    {
        SaveWithStatus("持仓已保存。", () =>
        {
            foreach (long id in _deletedPositionIds)
            {
                _repository.DeletePositionState(id);
            }

            foreach (PositionStateRecord record in _positions)
            {
                if (record.Id <= 0 && string.IsNullOrWhiteSpace(record.StrategyCode) && string.IsNullOrWhiteSpace(record.ActualCode))
                {
                    continue;
                }
                _repository.SavePositionState(record);
            }
            LoadData();
        });
    }

    private void SaveOtcChannels()
    {
        SaveWithStatus("OTCMap 已保存。", () =>
        {
            foreach (long id in _deletedOtcIds)
            {
                _repository.DeleteOtcChannel(id);
            }

            foreach (OtcChannelRecord record in _otcChannels)
            {
                if (record.Id <= 0 && string.IsNullOrWhiteSpace(record.StrategyCode) && string.IsNullOrWhiteSpace(record.OtcCode))
                {
                    continue;
                }
                _repository.SaveOtcChannel(record);
            }
            LoadData();
        });
    }

    private async void SaveTradeLogs()
    {
        if (_isSavingTradeLogs)
        {
            SetStatus("TradeLog 正在保存，请等待当前保存完成。", true);
            return;
        }

        using var _ = AppOperationContext.Begin("TradeLog 保存");
        _isSavingTradeLogs = true;
        if (_tradeLogSaveButton is not null)
        {
            _tradeLogSaveButton.IsEnabled = false;
        }

        try
        {
            if (!SafeCommitTradeLogGridEdits(out string? commitError))
            {
                SetStatus(commitError ?? "TradeLog 当前编辑内容无法保存。", true);
                return;
            }

            List<TradeLogRecord> recordsToSave = _tradeLogs
                .Where(record => !(record.Id <= 0 && IsUntouchedTradeLog(record)))
                .Select(CloneTradeLog)
                .ToList();

            var currentPersistedIds = recordsToSave
                .Where(record => record.Id > 0)
                .Select(record => record.Id)
                .ToHashSet();
            var idsToDelete = _deletedTradeLogIds
                .Concat(_loadedTradeLogIds.Where(id => !currentPersistedIds.Contains(id)))
                .Where(id => id > 0 && !currentPersistedIds.Contains(id))
                .Distinct()
                .ToArray();

            TradeLogSaveResult saveResult = await Task
                .Run(() => SaveTradeLogsCore(recordsToSave, idsToDelete))
                .ConfigureAwait(true);

            CopyTradeLogCalculatedFields(recordsToSave);
            LoadData();
            if (saveResult.ReplayResult.Account.ReplayStatus == "财务异常")
            {
                SetStatus($"TradeLog 已保存，账户回放异常：{saveResult.ReplayResult.Account.ReplayError}", true);
            }
            else if (saveResult.ReplayResult.Account.ReplayStatus == "估值不完整")
            {
                SetStatus($"TradeLog 已保存并已回放，估值不完整：{saveResult.ReplayResult.Account.ReplayError}", true);
            }
            else
            {
                SetStatus("TradeLog 已保存并完成账户回放。", false);
            }

            DataSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.WriteRuntime("ERROR", "TradeLog 保存失败", BuildTradeLogSaveFailureDetail(ex), ex);
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "保存失败", ex.ToString());
            SetStatus($"保存失败：{ex.Message}", true);
        }
        finally
        {
            _isSavingTradeLogs = false;
            if (_tradeLogSaveButton is not null)
            {
                _tradeLogSaveButton.IsEnabled = true;
            }
        }
    }

    private TradeLogSaveResult SaveTradeLogsCore(IReadOnlyList<TradeLogRecord> recordsToSave, IReadOnlyList<long> idsToDelete)
    {
        using var _ = AppOperationContext.Begin("TradeLog 保存后台：金额计算/账务推演/删除同步/账户回放");
        TradeLogLedgerNormalizer.AutoCalculateTradeAmounts(recordsToSave);
        if (!TradeLogLedgerNormalizer.TryNormalizeLedgerFieldsBeforeSave(recordsToSave, _repository.ReadMarketQuoteCache(), out string? normalizeError))
        {
            throw new InvalidOperationException(normalizeError ?? "TradeLog 账务字段自动推演失败。");
        }

        foreach (TradeLogRecord record in recordsToSave)
        {
            ValidateTradeLogForSave(record);
        }

        _repository.SaveTradeLogsSnapshot(idsToDelete, recordsToSave);
        AccountReplayResult replayResult = ReplayAccountFromTradeLogs();
        return new TradeLogSaveResult(replayResult);
    }

    private void TradeLogGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            QueueTradeLogAmountCalculation();
        }
    }

    private void TradeLogGrid_CurrentCellChanged(object? sender, EventArgs e)
        => QueueTradeLogAmountCalculation();

    private void QueueTradeLogAmountCalculation()
    {
        if (_isLoadingTradeLogs || _isSavingTradeLogs || _isApplyingTradeLogAutoCalc)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            TryApplyTradeLogAmountAutoCalc();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TryApplyTradeLogAmountAutoCalc()
    {
        if (_isLoadingTradeLogs || _isSavingTradeLogs || _isApplyingTradeLogAutoCalc)
        {
            return;
        }

        using var _ = AppOperationContext.Begin("TradeLog 金额自动计算");
        _isApplyingTradeLogAutoCalc = true;
        try
        {
            TradeLogLedgerNormalizer.AutoCalculateTradeAmounts(_tradeLogs.ToArray());
        }
        catch (Exception ex)
        {
            AppExceptionLogger.WriteRuntime("ERROR", "TradeLog 金额自动计算失败", ex.Message, ex);
            SetStatus($"金额自动计算失败：{ex.Message}", true);
        }
        finally
        {
            _isApplyingTradeLogAutoCalc = false;
        }
    }

    private AccountReplayResult ReplayAccountFromTradeLogs()
    {
        using var _ = AppOperationContext.Begin("TradeLog 保存后账户回放");
        var replayService = new AccountReplayService();
        AccountReplayResult replayResult = replayService.Replay(
            _repository.ReadTradeLogs(),
            _repository.ReadMarketQuoteCache());
        _repository.SaveAccountReplayResult(replayResult);
        return replayResult;
    }

    private void SaveWithStatus(string successMessage, Action save)
    {
        try
        {
            save();
            SetStatus(successMessage, false);
            DataSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "保存失败", ex.ToString());
            SetStatus($"保存失败：{ex.Message}", true);
        }
    }

    private bool SafeCommitTradeLogGridEdits(out string? error)
    {
        using var _ = AppOperationContext.Begin("TradeLog DataGrid 提交编辑");
        try
        {
            CommitGridEdits(_tradeLogGrid, "TradeLog 当前编辑内容无法保存，请检查数值格式。");
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            AppExceptionLogger.WriteRuntime("ERROR", "TradeLog DataGrid 提交失败", ex.Message, ex);
            TryWriteRuntimeLog("ERROR", "ManualDataEntryWindow", "TradeLog 编辑提交失败", ex.ToString());
            error = "TradeLog 当前编辑内容无法保存，请检查数值格式。";
            return false;
        }
    }

    private static void CommitGridEdits(DataGrid grid, string errorMessage)
    {
        bool cellCommitted = grid.CommitEdit(DataGridEditingUnit.Cell, true);
        bool rowCommitted = grid.CommitEdit(DataGridEditingUnit.Row, true);
        Keyboard.ClearFocus();
        cellCommitted &= grid.CommitEdit(DataGridEditingUnit.Cell, true);
        rowCommitted &= grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!cellCommitted || !rowCommitted || HasValidationError(grid))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static bool HasValidationError(DependencyObject root)
    {
        if (Validation.GetHasError(root))
        {
            return true;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            if (HasValidationError(VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUntouchedTradeLog(TradeLogRecord record)
        => string.IsNullOrWhiteSpace(record.StrategyCode)
           && string.IsNullOrWhiteSpace(record.ActualCode)
           && string.IsNullOrWhiteSpace(record.Tier)
           && string.IsNullOrWhiteSpace(record.Source)
           && string.IsNullOrWhiteSpace(record.Memo)
           && (string.IsNullOrWhiteSpace(record.Action) || record.Action == "买入")
           && record.Price == 0
           && record.Quantity == 0
           && record.Amount == 0
           && record.Fee == 0
           && record.NetCashImpact == 0
           && record.Principal == 0
           && record.CashBalance == 0
           && record.TotalAssets == 0;

    private static void ValidateTradeLogForSave(TradeLogRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Time))
        {
            throw new InvalidOperationException("TradeLog 时间不能为空。");
        }

        if (string.IsNullOrWhiteSpace(record.StrategyCode))
        {
            throw new InvalidOperationException("TradeLog 策略代码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(record.Action))
        {
            throw new InvalidOperationException("TradeLog 动作不能为空。");
        }

        if (!TradeLogActions.Contains(record.Action))
        {
            throw new InvalidOperationException($"TradeLog 动作只允许：{string.Join("、", TradeLogActions)}。");
        }
    }

    private void TryWriteRuntimeLog(string level, string module, string message, string detail)
    {
        try
        {
            _repository.WriteRuntimeLog(level, module, message, detail);
        }
        catch
        {
            // The visible status bar remains the primary feedback if failure logging itself fails.
        }
    }

    private void DeleteSelectedStrategy()
    {
        if (_strategyGrid.SelectedItem is StrategyConfigRecord record)
        {
            if (record.Id > 0)
            {
                _deletedStrategyIds.Add(record.Id);
            }
            _strategies.Remove(record);
            SetStatus("策略配置已从列表移除，点击保存后写入数据库。", false);
        }
    }

    private void DeleteSelectedPosition()
    {
        if (_positionGrid.SelectedItem is PositionStateRecord record)
        {
            if (record.Id > 0)
            {
                _deletedPositionIds.Add(record.Id);
            }
            _positions.Remove(record);
            SetStatus("持仓已从列表移除，点击保存后写入数据库。", false);
        }
    }

    private void DeleteSelectedOtc()
    {
        if (_otcGrid.SelectedItem is OtcChannelRecord record)
        {
            if (record.Id > 0)
            {
                _deletedOtcIds.Add(record.Id);
            }
            _otcChannels.Remove(record);
            SetStatus("OTCMap 记录已从列表移除，点击保存后写入数据库。", false);
        }
    }

    private void DeleteSelectedTradeLog()
    {
        using var operationContext = AppOperationContext.Begin("TradeLog 删除选中");
        if (_isSavingTradeLogs)
        {
            SetStatus("TradeLog 正在保存，暂不能删除。", true);
            return;
        }

        try
        {
            if (!SafeCommitTradeLogGridEdits(out string? _))
            {
                _tradeLogGrid.CancelEdit(DataGridEditingUnit.Cell);
                _tradeLogGrid.CancelEdit(DataGridEditingUnit.Row);
            }
        }
        catch (Exception ex)
        {
            AppExceptionLogger.WriteRuntime("ERROR", "TradeLog 删除前取消编辑失败", ex.Message, ex);
        }

        if (_tradeLogGrid.SelectedItem is TradeLogRecord record)
        {
            if (record.Id > 0)
            {
                _deletedTradeLogIds.Add(record.Id);
            }
            _tradeLogs.Remove(record);
            SetStatus("TradeLog 记录已从列表移除，点击保存后写入数据库。", false);
        }
    }

    private static void BeginEdit(DataGrid grid)
    {
        grid.Focus();
        grid.BeginEdit();
    }

    private void ClearDeletedIds()
    {
        _deletedStrategyIds.Clear();
        _deletedPositionIds.Clear();
        _deletedOtcIds.Clear();
        _deletedTradeLogIds.Clear();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = BrushFrom(isError ? "#EF4444" : "#9CAFC3");
    }

    private static double ParseDouble(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double localValue))
        {
            return localValue;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double invariantValue))
        {
            return invariantValue;
        }

        throw new InvalidOperationException($"{fieldName}必须是数字。");
    }

    private static string FormatEditorNumber(double value)
        => value == 0 ? "0" : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static SolidColorBrush BrushFrom(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    private string BuildTradeLogSaveFailureDetail(Exception ex)
        => $"TradeLog 保存失败。行数={_tradeLogs.Count}，待删除ID数={_deletedTradeLogIds.Count}，当前编辑ID={CurrentTradeLogId()}，异常={ex.Message}";

    private long CurrentTradeLogId()
        => _tradeLogGrid.SelectedItem is TradeLogRecord record ? record.Id : 0;

    private static TradeLogRecord CloneTradeLog(TradeLogRecord record)
        => new()
        {
            Id = record.Id,
            Time = record.Time,
            StrategyCode = record.StrategyCode,
            ActualCode = record.ActualCode,
            Action = record.Action,
            Price = record.Price,
            Quantity = record.Quantity,
            Amount = record.Amount,
            Tier = record.Tier,
            Source = record.Source,
            Fee = record.Fee,
            Memo = record.Memo,
            NetCashImpact = record.NetCashImpact,
            Principal = record.Principal,
            CashBalance = record.CashBalance,
            TotalAssets = record.TotalAssets
        };

    private void CopyTradeLogCalculatedFields(IReadOnlyList<TradeLogRecord> calculatedRecords)
    {
        var byId = calculatedRecords
            .Where(record => record.Id > 0)
            .GroupBy(record => record.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (TradeLogRecord target in _tradeLogs)
        {
            TradeLogRecord? source = target.Id > 0 && byId.TryGetValue(target.Id, out TradeLogRecord? byIdRecord)
                ? byIdRecord
                : calculatedRecords.FirstOrDefault(record => record.Id == target.Id
                                                             && string.Equals(record.Time, target.Time, StringComparison.Ordinal)
                                                             && string.Equals(record.StrategyCode, target.StrategyCode, StringComparison.Ordinal)
                                                             && string.Equals(record.Action, target.Action, StringComparison.Ordinal));
            if (source is null)
            {
                continue;
            }

            target.Amount = source.Amount;
            target.NetCashImpact = source.NetCashImpact;
            target.Principal = source.Principal;
            target.CashBalance = source.CashBalance;
            target.TotalAssets = source.TotalAssets;
        }
    }

    private sealed record TradeLogSaveResult(AccountReplayResult ReplayResult);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
