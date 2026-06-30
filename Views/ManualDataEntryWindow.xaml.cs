using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ObservableCollection<StrategyConfigRecord> _strategies = new();
    private readonly ObservableCollection<PositionStateRecord> _positions = new();
    private readonly ObservableCollection<OtcChannelRecord> _otcChannels = new();
    private readonly ObservableCollection<TradeLogRecord> _tradeLogs = new();
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

    public ManualDataEntryWindow(LocalDataRepository repository, string databasePath)
        : this(repository, databasePath, ManualEntryScope.All)
    {
    }

    public ManualDataEntryWindow(LocalDataRepository repository, string databasePath, ManualEntryScope scope)
    {
        _repository = repository;
        InitializeComponent();
        WindowInteractionEffects.ApplySmoothOpen(this);
        SourceInitialized += (_, _) => TryApplyDarkTitleBar();
        DatabasePathText.Text = databasePath;
        BuildTabs();
        LoadData();
        ApplyScope(scope);
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
            ManualEntryScope.SystemSettings => "系统设置 / 数据维护",
            _ => "本地数据手动录入"
        };

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
        }
        catch
        {
            // Keep the native title bar unchanged on Windows builds without this DWM attribute.
        }
    }

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
        var border = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(24),
            Background = BrushFrom("#071827"),
            BorderBrush = BrushFrom("#24415B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var content = new StackPanel
        {
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        content.Children.Add(CreateMaintenanceText("系统设置 / 数据维护", 20, "#E5EEF8", FontWeights.SemiBold));
        content.Children.Add(CreateMaintenanceText("数据库路径：", 14, "#9CAFC3", FontWeights.SemiBold, new Thickness(0, 18, 0, 0)));
        content.Children.Add(CreateMaintenanceText(DatabasePathText.Text, 13, "#E5EEF8", FontWeights.Normal, new Thickness(0, 6, 0, 0)));
        content.Children.Add(CreateMaintenanceText("说明：", 14, "#9CAFC3", FontWeights.SemiBold, new Thickness(0, 22, 0, 0)));
        content.Children.Add(CreateMaintenanceText("账户状态和持仓已由 TradeLog 自动回放生成，不再手动维护。", 14, "#E5EEF8", FontWeights.Normal, new Thickness(0, 6, 0, 0)));
        content.Children.Add(CreateMaintenanceText("策略配置、OTCMap、底仓基准请到“溢价决策”维护。", 14, "#E5EEF8", FontWeights.Normal, new Thickness(0, 6, 0, 0)));
        content.Children.Add(CreateMaintenanceText("TradeLog 请到“交易日志”维护。", 14, "#E5EEF8", FontWeights.Normal, new Thickness(0, 6, 0, 0)));
        content.Children.Add(CreateMaintenanceText("后续维护功能：数据备份 / 恢复、运行日志、版本信息。", 14, "#9CAFC3", FontWeights.Normal, new Thickness(0, 22, 0, 0)));
        content.Children.Add(CreateAlertSettingsPanel());
        content.Children.Add(CreateHotkeySettingsPanel());

        border.Child = content;
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = BrushFrom("#050B14"),
            Content = border
        };
        SystemMaintenanceTabRoot.Children.Add(scroll);
        RefreshAlertSettingsUi();
        RefreshHotkeySettingsUi();
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
        var border = new Border
        {
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(18),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var panel = new StackPanel();
        panel.Children.Add(CreateMaintenanceText("预警设置", 17, "#E5EEF8", FontWeights.SemiBold));

        _alertPushPlusEnabledBox = CreateAlertCheckBox("启用微信预警");
        _alertPushPlusEnabledBox.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(_alertPushPlusEnabledBox);

        var tokenRow = CreateAlertSettingRow("PushPlus Token", out PasswordBox tokenBox);
        _alertPushPlusTokenBox = tokenBox;
        panel.Children.Add(tokenRow);

        var wechatButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(150, 10, 0, 0) };
        Button saveButton = CreateButton("保存预警设置");
        saveButton.Click += (_, _) => SaveAlertSettingsFromUi();
        Button testWechatButton = CreateButton("测试微信");
        testWechatButton.Click += async (_, _) => await TestWechatAsync();
        wechatButtons.Children.Add(saveButton);
        wechatButtons.Children.Add(testWechatButton);
        panel.Children.Add(wechatButtons);

        _alertVoiceEnabledBox = CreateAlertCheckBox("启用系统语音");
        _alertVoiceEnabledBox.Margin = new Thickness(0, 18, 0, 0);
        panel.Children.Add(_alertVoiceEnabledBox);

        var voiceButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(150, 10, 0, 0) };
        Button testVoiceButton = CreateButton("测试语音");
        testVoiceButton.Click += async (_, _) => await TestVoiceAsync();
        voiceButtons.Children.Add(testVoiceButton);
        panel.Children.Add(voiceButtons);

        panel.Children.Add(CreateIntervalRow("重复提醒间隔", "分钟", out _alertRepeatIntervalBox));
        panel.Children.Add(CreateIntervalRow("严重风险间隔", "分钟", out _alertSevereIntervalBox));
        panel.Children.Add(CreateIntervalRow("行情异常间隔", "分钟", out _alertMarketIntervalBox));

        _alertStatusText = CreateMaintenanceText(string.Empty, 12, "#9CAFC3", FontWeights.Normal, new Thickness(150, 8, 0, 0));
        panel.Children.Add(_alertStatusText);

        border.Child = panel;
        return border;
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
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(18),
            Background = BrushFrom("#061B2A"),
            BorderBrush = BrushFrom("#1F4E68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var panel = new StackPanel();
        panel.Children.Add(CreateMaintenanceText("界面快捷键", 17, "#E5EEF8", FontWeights.SemiBold));

        var row = new Grid { Margin = new Thickness(0, 16, 0, 0), MaxWidth = 460 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
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

        _hotkeyStatusText = CreateMaintenanceText(string.Empty, 12, "#9CAFC3", FontWeights.Normal, new Thickness(160, 6, 0, 0));
        panel.Children.Add(_hotkeyStatusText);

        Button restoreButton = CreateButton("恢复默认设置");
        restoreButton.Margin = new Thickness(0, 14, 0, 0);
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
