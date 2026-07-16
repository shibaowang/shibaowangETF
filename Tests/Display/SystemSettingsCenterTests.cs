using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class SystemSettingsCenterTests
{
    [Fact]
    public void SystemSettingsScope_UsesHeaderlessOuterTabsWhileOtherScopesUseNormalTabs()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string applyScope = Slice(code, "public void ApplyScope(ManualEntryScope scope)", "public static IReadOnlyList<string> GetVisibleTabHeaders");

        Assert.Contains("EntryNormalTabControlStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("EntryHeaderlessTabControlStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentSource=\"SelectedContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("scope == ManualEntryScope.SystemSettings", applyScope, StringComparison.Ordinal);
        Assert.Contains("EntryHeaderlessTabControlStyle", applyScope, StringComparison.Ordinal);
        Assert.Contains("EntryNormalTabControlStyle", applyScope, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemSettingsScope_KeepsOnlyMaintenanceTabAndDefaultsToGeneralSettings()
    {
        Assert.Equal(new[] { "系统维护" }, ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.SystemSettings));
        Assert.Equal(
            new[] { "策略配置", "账户状态", "持仓", "OTCMap", "底仓基准设置", "TradeLog", "系统维护" },
            ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.All));
        Assert.Equal("通用设置", ManualDataEntryWindow.GetDefaultSystemSettingsPageTitle());
    }

    [Fact]
    public void SettingsCenter_ContainsOrderedFourCategoryMenuAndDescriptions()
    {
        Assert.Equal(
            new[] { "通用设置", "预警与通知", "数据安全", "运行与诊断" },
            ManualDataEntryWindow.GetSystemSettingsPageTitles());
        Assert.Equal(
            new[]
            {
                "快捷键、版本与程序目录",
                "微信通知、语音与提醒频率",
                "数据维护、备份与恢复",
                "行情诊断与运行健康"
            },
            ManualDataEntryWindow.GetSystemSettingsPageDescriptions());
    }

    [Fact]
    public void SettingsCenter_UsesFixedNavigationAndStretchingCachedPageHost()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = Slice(code, "private void BuildSystemMaintenanceTab()", "private Button CreateSystemSettingsMenuButton");

        Assert.Contains("new GridLength(226)", build, StringComparison.Ordinal);
        Assert.Contains("new GridLength(16)", build, StringComparison.Ordinal);
        Assert.Contains("GridUnitType.Star", build, StringComparison.Ordinal);
        Assert.Contains("_systemSettingsPageHost", build, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment = HorizontalAlignment.Stretch", build, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment = VerticalAlignment.Stretch", build, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth = 1040", build, StringComparison.Ordinal);
        Assert.DoesNotContain("选择要管理的系统功能", build, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_AreCreatedOnceCachedAndSwitchKeepsObjectReferences()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = Slice(code, "private void BuildSystemMaintenanceTab()", "private Button CreateSystemSettingsMenuButton");
        string switchMethod = Slice(code, "private void SwitchSystemSettingsPage", "private void UpdateSystemSettingsMenuVisual");

        Assert.Equal(4, CountOccurrences(build, "_systemSettingsPages[SystemSettingsPage."));
        Assert.Contains("SystemMaintenanceTabRoot.Children.Count > 0", build, StringComparison.Ordinal);
        Assert.Contains("_systemSettingsPageHost.Content = content", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDatabaseBackupPanel", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAlertSettingsPanel", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRuntimeHealthPanel", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("+=", switchMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_HaveIndependentVerticalScrollAndNoWholePageHorizontalScroll()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string pageFactory = Slice(code, "private static ScrollViewer CreateSystemSettingsPage", "private UIElement CreateDataMaintenancePanel");

        Assert.Contains("MinWidth = 850", pageFactory, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 1220", pageFactory, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", pageFactory, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(36, 28, 36, 28)", pageFactory, StringComparison.Ordinal);
        Assert.Contains("CreateMaintenanceText(title, 24", pageFactory, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", pageFactory, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", pageFactory, StringComparison.Ordinal);
        Assert.Contains("Background = BrushFrom(\"#050B14\")", pageFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void DataMaintenancePage_OnlyExplainsExistingDataAndMaintenanceBoundaries()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string page = Slice(code, "private UIElement CreateDataMaintenancePanel()", "private UIElement CreateSystemDiagnosticsPanel(MarketDiagnosticsSnapshot snapshot)");

        Assert.Contains("当前数据库路径", page, StringComparison.Ordinal);
        Assert.Contains("数据目录", page, StringComparison.Ordinal);
        Assert.Contains("TradeLog 是账户和持仓事实源", page, StringComparison.Ordinal);
        Assert.Contains("系统不自动写入交易记录", page, StringComparison.Ordinal);
        Assert.Contains("备份恢复不触发行情、策略或委托", page, StringComparison.Ordinal);
        Assert.DoesNotContain("打开数据目录", page, StringComparison.Ordinal);
        Assert.DoesNotContain("清空数据库", page, StringComparison.Ordinal);
        Assert.DoesNotContain("重建数据库", page, StringComparison.Ordinal);
        Assert.DoesNotContain("清理缓存", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupRestorePage_KeepsFourActionsAndSeparatesRestoreDangerArea()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string backupPage = Slice(code, "private UIElement CreateDatabaseBackupPanel()", "private UIElement CreateDatabaseRestorePanel()");
        string restorePage = Slice(code, "private UIElement CreateDatabaseRestorePanel()", "private async Task CreateManualDatabaseBackupAsync()");
        string page = backupPage + restorePage;
        int toolbarStart = page.IndexOf("var toolbar", StringComparison.Ordinal);
        int toolbarEnd = page.IndexOf("Grid.SetColumn(toolbar, 1)", StringComparison.Ordinal);
        string toolbar = page[toolbarStart..toolbarEnd];

        Assert.Contains("立即备份", page, StringComparison.Ordinal);
        Assert.Contains("刷新列表", page, StringComparison.Ordinal);
        Assert.Contains("打开备份目录", page, StringComparison.Ordinal);
        Assert.Contains("恢复选中备份", page, StringComparison.Ordinal);
        Assert.DoesNotContain("toolbar.Children.Add(_restoreDatabaseBackupButton)", toolbar, StringComparison.Ordinal);
        Assert.DoesNotContain("restoreDangerArea", backupPage, StringComparison.Ordinal);
        Assert.Contains("restoreDangerArea", restorePage, StringComparison.Ordinal);
        Assert.Contains("恢复数据库", page, StringComparison.Ordinal);
        Assert.Contains("下次启动前替换当前数据库", page, StringComparison.Ordinal);
        Assert.Contains("双重确认流程", page, StringComparison.Ordinal);
        Assert.Contains("_databaseBackupGrid.Height = 280", page, StringComparison.Ordinal);
        Assert.Contains("DataGridLengthUnitType.Star", page, StringComparison.Ordinal);
        Assert.Contains("recordsCard.Child = records", page, StringComparison.Ordinal);
        Assert.DoesNotContain("border.Child = root", backupPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupRestorePage_InitializesListOnceAndKeepsOriginalRestoreFlow()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = Slice(code, "private void BuildSystemMaintenanceTab()", "private Button CreateSystemSettingsMenuButton");
        string restore = Slice(code, "private async Task ConfirmAndStageDatabaseRestoreAsync()", "private void SetDatabaseBackupBusy");

        Assert.Equal(1, CountOccurrences(build, "RefreshDatabaseBackupListAsync"));
        Assert.Equal(2, CountOccurrences(restore, "MessageBoxButton.YesNo"));
        Assert.Contains("StageRestoreAsync", restore, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Shutdown", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertPage_UsesTwoColumnChannelsThreeColumnIntervalsAndPlainSaveRow()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateAlertSettingsPanel()", "private static CheckBox CreateAlertCheckBox");
        string wechat = Slice(code, "private async Task TestWechatAsync()", "private async Task TestVoiceAsync()");
        string voice = Slice(code, "private async Task TestVoiceAsync()", "private void ShowAlertStatus");

        Assert.Contains("微信通知", panel, StringComparison.Ordinal);
        Assert.Contains("系统语音", panel, StringComparison.Ordinal);
        Assert.Contains("提醒频率", panel, StringComparison.Ordinal);
        Assert.Contains("CreateTwoColumnSettingsGrid(wechatCard, voiceCard)", panel, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(repeatColumn, 0)", panel, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(severeColumn, 2)", panel, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(marketColumn, 4)", panel, StringComparison.Ordinal);
        Assert.Contains("var saveRow = new Grid", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("保存与状态", panel, StringComparison.Ordinal);
        Assert.Contains("out PasswordBox tokenBox", code, StringComparison.Ordinal);
        Assert.Contains("SaveAlertSettingsFromUi", wechat, StringComparison.Ordinal);
        Assert.Contains("SaveAlertSettingsFromUi", voice, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayTestVoiceAsync", wechat, StringComparison.Ordinal);
        Assert.DoesNotContain("SendTestWechatAsync", voice, StringComparison.Ordinal);
        Assert.Contains("value < 1 || value > 1440", code, StringComparison.Ordinal);
    }

    [Fact]
    public void HotkeyPage_StillExposesOnlyDisplayHideShortcut()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateHotkeySettingsPanel()", "private Button CreateHotkeyPillButton()");

        Assert.Contains("显示/隐藏窗口", panel, StringComparison.Ordinal);
        Assert.Contains("BeginHotkeyCapture", panel, StringComparison.Ordinal);
        Assert.Contains("ClearHotkeySettings", panel, StringComparison.Ordinal);
        Assert.Contains("恢复默认设置", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("风险中心快捷键", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("交易日志快捷键", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("图表快捷键", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemDiagnosticsPage_ReusesReadOnlySnapshotSummaryWithoutNetworkingOrWrites()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateSystemDiagnosticsPanel(MarketDiagnosticsSnapshot snapshot)", "private UIElement CreateGeneralSettingsPanel()");

        Assert.Contains("总体诊断状态", panel, StringComparison.Ordinal);
        Assert.Contains("本地数据库状态", panel, StringComparison.Ordinal);
        Assert.Contains("更详细的行情、盈亏、配置和运行诊断请在风险中心查看。", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDataClient", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Write", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("RiskCenterWindow", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthPage_KeepsSixteenMetricsThreeActionsAndSingleSubscription()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateRuntimeHealthPanel()", "private void RuntimeHealthMonitor_SnapshotAvailable");

        Assert.Equal(16, Slice(panel, "string[] fields", "_runtimeHealthValueTexts.Clear()").Split('"').Where((_, index) => index % 2 == 1).Count());
        Assert.Contains("bool fullWidth = index >= 14", panel, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(value, 3)", panel, StringComparison.Ordinal);
        Assert.Contains("刷新状态", panel, StringComparison.Ordinal);
        Assert.Contains("打开健康日志目录", panel, StringComparison.Ordinal);
        Assert.Contains("导出最近24小时报告", panel, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(code, "SnapshotAvailable += RuntimeHealthMonitor_SnapshotAvailable"));
        Assert.Equal(1, CountOccurrences(code, "SnapshotAvailable -= RuntimeHealthMonitor_SnapshotAvailable"));
    }

    [Fact]
    public void GeneralPage_ShowsVersionPathsAndFixedBrokerBoundaryWithoutUpdateFeature()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateGeneralSettingsPanel()", "private UIElement CreateRuntimeDiagnosticsPanel()");

        Assert.Contains("MainWindow.ResolveDisplayVersion()", panel, StringComparison.Ordinal);
        Assert.Contains("FileVersion", panel, StringComparison.Ordinal);
        Assert.Contains("构建标识", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("程序集 InformationalVersion", panel, StringComparison.Ordinal);
        Assert.Contains("备份目录", panel, StringComparison.Ordinal);
        Assert.Contains("恢复目录", panel, StringComparison.Ordinal);
        Assert.Contains("健康日志目录", panel, StringComparison.Ordinal);
        Assert.Contains("本系统仅提供投资分析与委托建议，不连接券商，不自动执行交易。", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("检查更新", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsMenu_ItemsUseCompactProfessionalDimensions()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string menu = Slice(code, "private Button CreateSystemSettingsMenuButton", "private void SwitchSystemSettingsPage");

        Assert.Contains("Height = 66", menu, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(0, 0, 0, 5)", menu, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(6)", menu, StringComparison.Ordinal);
        Assert.Contains("Padding = new Thickness(0, 8, 7, 8)", menu, StringComparison.Ordinal);
        Assert.Contains("definition.Icon, 19", menu, StringComparison.Ordinal);
        Assert.Contains("definition.Title, 15", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralPage_UsesTwoColumnsThenFullWidthDirectoriesAndBoundary()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateGeneralSettingsPanel()", "private UIElement CreateSoftwareInformationPanel()");

        Assert.Contains("CreateTwoColumnSettingsGrid", panel, StringComparison.Ordinal);
        Assert.Contains("CreateHotkeySettingsPanel", panel, StringComparison.Ordinal);
        Assert.Contains("CreateSoftwareInformationPanel", panel, StringComparison.Ordinal);
        Assert.Contains("CreateLocalDataDirectoryPanel", panel, StringComparison.Ordinal);
        Assert.Contains("CreateSystemBoundaryPanel", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void DataSecurityPage_UsesFourSummariesAndExistingBackupSummarySource()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateDataSecurityPanel()", "private UIElement CreateRuntimeDiagnosticsPanel()");
        string refresh = Slice(code, "private async Task RefreshDatabaseBackupListCoreAsync()", "private void OpenDatabaseBackupDirectory()");

        Assert.Contains("CreateSettingsSummaryGrid", panel, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(panel, "CreateSettingsSummaryCard("));
        Assert.Contains("BuildSummary(backups)", refresh, StringComparison.Ordinal);
        Assert.Contains("_databaseSummaryLatestBackupText.Text = latest", refresh, StringComparison.Ordinal);
        Assert.Contains("_databaseSummaryValidCountText.Text", refresh, StringComparison.Ordinal);
        Assert.Contains("_databaseSummaryAutomaticStatusText.Text", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeDiagnosticsPage_UsesFourSummariesTwoColumnsAndSeparateActions()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateRuntimeDiagnosticsPanel()", "private static Border CreateSystemSettingsCard()");

        Assert.Contains("CreateSettingsSummaryGrid", panel, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(panel, "CreateSettingsSummaryCard("));
        Assert.Contains("CreateTwoColumnSettingsGrid", panel, StringComparison.Ordinal);
        Assert.Contains("CreateSystemDiagnosticsPanel(snapshot)", panel, StringComparison.Ordinal);
        Assert.Contains("CreateRuntimeHealthPanel()", panel, StringComparison.Ordinal);
        Assert.Contains("CreateRuntimeHealthActionsPanel()", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsCenter_DoesNotAddPageWideHorizontalScrollingOrNewCommands()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = Slice(code, "private void BuildSystemMaintenanceTab()", "private Button CreateSystemSettingsMenuButton");
        string pageFactory = Slice(code, "private static ScrollViewer CreateSystemSettingsPage", "private UIElement CreateDataMaintenancePanel");

        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", pageFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("手动刷新", build + pageFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemSettingsWindow", build + pageFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDiagnosticsWindow", build + pageFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageSwitch_AlwaysRestoresCachedPageScrollToTopWithoutBusinessRefresh()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string switchMethod = Slice(code, "private void SwitchSystemSettingsPage", "private void UpdateSystemSettingsMenuVisual");

        Assert.Contains("pageScrollViewer.ScrollToTop()", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshDatabaseBackupListAsync", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshRuntimeHealthPanel", switchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository", switchMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void DataSecurityPage_OrdersSummaryBackupMaintenanceThenRestore()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateDataSecurityPanel()", "private UIElement CreateRuntimeDiagnosticsPanel()");

        int summary = panel.IndexOf("root.Children.Add(summaries)", StringComparison.Ordinal);
        int backup = panel.IndexOf("root.Children.Add(backup)", StringComparison.Ordinal);
        int maintenance = panel.IndexOf("root.Children.Add(maintenance)", StringComparison.Ordinal);
        int restore = panel.IndexOf("root.Children.Add(restore)", StringComparison.Ordinal);
        Assert.True(summary >= 0 && summary < backup && backup < maintenance && maintenance < restore);
    }

    [Fact]
    public void PathsAndBuildIdentifiers_TrimVisuallyAndExposeFullTooltip()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string helpers = Slice(code, "private static Grid CreateInformationGrid", "private UIElement CreateRuntimeHealthPanel()");

        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", helpers, StringComparison.Ordinal);
        Assert.Contains("value.ToolTip = value.Text", helpers, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.NoWrap", helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsButtons_UseLocalPrimarySecondaryAndDangerStylesOnly()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("x:Key=\"SettingsSecondaryButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsPrimaryButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsDangerButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsButton(\"保存预警设置\", SettingsButtonKind.Primary)", code, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsButton(\"立即备份\", SettingsButtonKind.Primary)", code, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsButton(\"恢复选中备份\", SettingsButtonKind.Danger)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsCardsAndSummaries_UseUnifiedSpacingAndTypography()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string helpers = Slice(code, "private static Border CreateSystemSettingsCard()", "private static Grid CreateInformationGrid");

        Assert.Contains("Padding = new Thickness(20)", helpers, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(8)", helpers, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 94", helpers, StringComparison.Ordinal);
        Assert.Contains("CreateMaintenanceText(title, 13", helpers, StringComparison.Ordinal);
        Assert.Contains("CreateMaintenanceText(value, 19", helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingSettingsActions_KeepTheirOriginalClickHandlers()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("testWechatButton.Click += async (_, _) => await TestWechatAsync()", code, StringComparison.Ordinal);
        Assert.Contains("testVoiceButton.Click += async (_, _) => await TestVoiceAsync()", code, StringComparison.Ordinal);
        Assert.Contains("saveButton.Click += (_, _) => SaveAlertSettingsFromUi()", code, StringComparison.Ordinal);
        Assert.Contains("_createDatabaseBackupButton.Click += async (_, _) => await CreateManualDatabaseBackupAsync()", code, StringComparison.Ordinal);
        Assert.Contains("_restoreDatabaseBackupButton.Click += async (_, _) => await ConfirmAndStageDatabaseRestoreAsync()", code, StringComparison.Ordinal);
        Assert.Contains("refreshButton.Click += (_, _) => RefreshRuntimeHealthPanel()", code, StringComparison.Ordinal);
        Assert.Contains("restoreButton.Click += (_, _) => RestoreDefaultHotkeySettings()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemSettingsHeader_UsesConciseDescriptionAndOtherScopesRestoreDatabasePath()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string applyScope = Slice(code, "public void ApplyScope(ManualEntryScope scope)", "public static IReadOnlyList<string> GetVisibleTabHeaders");

        Assert.Equal("系统设置", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.SystemSettings));
        Assert.Contains("管理数据、备份、通知与系统运行状态", applyScope, StringComparison.Ordinal);
        Assert.Contains(": _databasePath", applyScope, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWindow_DarkFrameAndNativeTitleBarProtectionsRemainUnchanged()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("Width=\"1280\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"820\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"260\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TryApplyDarkTitleBar", code, StringComparison.Ordinal);
        Assert.Contains("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkStartupShield", xaml + code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowConstructor_KeepsRequiredBuildLoadScopeAndHealthLifecycleOrder()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string constructor = Slice(code, "public ManualDataEntryWindow(\n        LocalDataRepository repository,", "public ManualEntryScope Scope");

        Assert.True(constructor.IndexOf("InitializeComponent();", StringComparison.Ordinal) < constructor.IndexOf("BuildTabs();", StringComparison.Ordinal));
        Assert.True(constructor.IndexOf("BuildTabs();", StringComparison.Ordinal) < constructor.IndexOf("LoadData();", StringComparison.Ordinal));
        Assert.True(constructor.IndexOf("LoadData();", StringComparison.Ordinal) < constructor.IndexOf("ApplyScope(scope);", StringComparison.Ordinal));
        Assert.True(constructor.IndexOf("ApplyScope(scope);", StringComparison.Ordinal) < constructor.IndexOf("SnapshotAvailable +=", StringComparison.Ordinal));
        Assert.Contains("Closed += ManualDataEntryWindow_RuntimeHealthClosed", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsCenter_DoesNotIntroduceIndependentWindowOrBusinessServiceChanges()
    {
        string root = FindRepositoryRoot();
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.False(File.Exists(Path.Combine(root, "Views", "SystemSettingsWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "Views", "MarketDiagnosticsWindow.xaml")));
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContract_IsV870AndDoesNotChangeAssemblyName()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains("<Version>8.10.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>8.10.0.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>8.10.0.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.10.0</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, "Start marker not found: " + startMarker);
        Assert.True(end > start, "End marker not found: " + endMarker);
        return text[start..end];
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrossETF.Terminal.UiShell.Reference.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
