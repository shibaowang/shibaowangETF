using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class TradeLogWorkspaceUiTests
{
    [Fact]
    public void TradeLogScope_KeepsSingleExistingTabAndWindowTitle()
    {
        Assert.Equal(new[] { "TradeLog" }, ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.TradeLog));
        Assert.Equal("TradeLog", ManualDataEntryWindow.GetDefaultTabHeader(ManualEntryScope.TradeLog));
        Assert.Equal("交易日志录入", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.TradeLog));

        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        Assert.Equal(1, CountOccurrences(xaml, "Header=\"TradeLog\""));
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "Views", "TradeLogWindow.xaml")));
    }

    [Fact]
    public void TradeLogScope_HidesSharedHeadingAndTabHeaderWithoutChangingOtherScopeStyles()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string applyScope = Slice(code, "public void ApplyScope(ManualEntryScope scope)", "public static IReadOnlyList<string> GetVisibleTabHeaders");
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");

        Assert.Contains("bool tradeLogOnly = scope == ManualEntryScope.TradeLog", applyScope, StringComparison.Ordinal);
        Assert.Contains("WindowHeadingArea.Visibility = sharedHeadingVisibility", applyScope, StringComparison.Ordinal);
        Assert.Contains("WindowTitleText.Visibility = sharedHeadingVisibility", applyScope, StringComparison.Ordinal);
        Assert.Contains("DatabasePathText.Visibility = sharedHeadingVisibility", applyScope, StringComparison.Ordinal);
        Assert.Contains("WindowHeadingRow.Height = tradeLogOnly ? new GridLength(0) : new GridLength(58)", applyScope, StringComparison.Ordinal);
        Assert.Contains("scope == ManualEntryScope.SystemSettings || tradeLogOnly", applyScope, StringComparison.Ordinal);
        Assert.Contains("EntryHeaderlessTabControlStyle", applyScope, StringComparison.Ordinal);
        Assert.Contains("EntryNormalTabControlStyle", applyScope, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"EntryHeaderlessTabControlStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"EntryNormalTabControlStyle\"", xaml, StringComparison.Ordinal);
        Assert.Equal(new[] { "策略配置", "账户状态", "持仓", "OTCMap", "底仓基准设置", "TradeLog", "系统维护" }, ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.All));
    }

    [Fact]
    public void Workspace_ShowsProfessionalHeadingAndFactSourceBoundary()
    {
        string workspace = WorkspaceSource();

        Assert.Contains("CreateMaintenanceText(\"交易日志\", 24", workspace, StringComparison.Ordinal);
        Assert.Contains("记录真实资金变动和交易事实。", workspace, StringComparison.Ordinal);
        Assert.Contains("TradeLog是账户、持仓、成本和回放的唯一事实源", workspace, StringComparison.Ordinal);
        Assert.Contains("只有点击“保存全部”后才会写入数据库", workspace, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workspace, "CreateMaintenanceText(\"交易日志\", 24"));
    }

    [Fact]
    public void Toolbar_ContainsOnlyFourExistingBusinessActionsAndSaveIsRightAligned()
    {
        string workspace = WorkspaceSource();

        Assert.Equal(4, CountOccurrences(workspace, "CreateTradeLogActionButton("));
        Assert.Contains("\"新增记录\"", workspace, StringComparison.Ordinal);
        Assert.Contains("\"编辑选中\"", workspace, StringComparison.Ordinal);
        Assert.Contains("\"删除选中\"", workspace, StringComparison.Ordinal);
        Assert.Contains("\"保存全部\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(_tradeLogSaveButton, 1)", workspace, StringComparison.Ordinal);
        Assert.Contains("TradeLogDangerButtonStyle", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("重新加载", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("导入", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("导出", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("搜索", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogGrid_HasExactlySixteenColumnsInRequiredDefaultOrder()
    {
        string build = BuildTradeLogSource();
        string[] orderedHeaders =
        {
            "ID", "时间", "策略代码", "实际代码", "动作", "来源", "档位", "数量",
            "价格", "金额", "手续费", "备注", "净现金流", "本金", "现金余额", "总资产"
        };

        Assert.Equal(16, CountOccurrences(build, "AddTradeLog"));
        int previous = -1;
        foreach (string header in orderedHeaders)
        {
            int current = build.IndexOf($"_tradeLogGrid, \"{header}\"", StringComparison.Ordinal);
            Assert.True(current > previous, $"Column is missing or out of order: {header}");
            previous = current;
        }

        Assert.DoesNotContain("名称", build, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogGrid_UsesRequiredColumnWidths()
    {
        string build = BuildTradeLogSource();
        string[] definitions =
        {
            "\"ID\", nameof(TradeLogRecord.Id), 64",
            "\"时间\", nameof(TradeLogRecord.Time), 155",
            "\"策略代码\", nameof(TradeLogRecord.StrategyCode), 110",
            "\"实际代码\", nameof(TradeLogRecord.ActualCode), 110",
            "\"动作\", nameof(TradeLogRecord.Action), TradeLogActions, 100",
            "\"来源\", nameof(TradeLogRecord.Source), TradeLogSources, 105",
            "\"档位\", nameof(TradeLogRecord.Tier), TradeLogTiers, 140",
            "\"数量\", nameof(TradeLogRecord.Quantity), 100",
            "\"价格\", nameof(TradeLogRecord.Price), 100",
            "\"金额\", nameof(TradeLogRecord.Amount), 115",
            "\"手续费\", nameof(TradeLogRecord.Fee), 95",
            "\"备注\", nameof(TradeLogRecord.Memo), 180",
            "\"净现金流\", nameof(TradeLogRecord.NetCashImpact), 115",
            "\"本金\", nameof(TradeLogRecord.Principal), 115",
            "\"现金余额\", nameof(TradeLogRecord.CashBalance), 115",
            "\"总资产\", nameof(TradeLogRecord.TotalAssets), 115"
        };

        foreach (string definition in definitions)
        {
            Assert.Contains(definition, build, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TradeLogWorkspace_StretchesAndRemarksAbsorbRemainingWidthWithoutExtraColumn()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string workspace = WorkspaceSource();
        string build = BuildTradeLogSource();
        string textColumn = Slice(code, "private void AddTradeLogTextColumn(", "private void AddTradeLogComboColumn(");
        string comboColumn = Slice(code, "private void AddTradeLogComboColumn(", "private void AddTradeLogNumberColumn(");
        string numberColumn = Slice(code, "private void AddTradeLogNumberColumn(", "private static TextBlock CreateTradeLogColumnHeader");
        string grid = Slice(code, "private DataGrid CreateTradeLogDataGrid()", "private void AddTradeLogRecord()");

        Assert.Contains("x:Name=\"TradeLogTabRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", Slice(xaml, "x:Name=\"TradeLogTabItem\"", "</TabItem>"), StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", Slice(xaml, "x:Name=\"TradeLogTabItem\"", "</TabItem>"), StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", workspace, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Stretch", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth", workspace, StringComparison.Ordinal);
        Assert.Contains("\"备注\", nameof(TradeLogRecord.Memo), 180", build, StringComparison.Ordinal);
        Assert.Contains("fillRemainingWidth: true, minWidth: 180", build, StringComparison.Ordinal);
        Assert.Contains("new DataGridLength(1, DataGridLengthUnitType.Star)", textColumn, StringComparison.Ordinal);
        Assert.Contains("MinWidth = minWidth > 0 ? minWidth : width", textColumn, StringComparison.Ordinal);
        Assert.Contains("MinWidth = width", comboColumn, StringComparison.Ordinal);
        Assert.Contains("MinWidth = width", numberColumn, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", grid, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment = HorizontalAlignment.Stretch", grid, StringComparison.Ordinal);
        Assert.Equal(16, CountOccurrences(build, "AddTradeLog"));
        Assert.DoesNotContain("填充列", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Spacer", build, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogGrid_ReadOnlyAndEditableColumnsRemainLocked()
    {
        string build = BuildTradeLogSource();

        Assert.Contains("\"ID\", nameof(TradeLogRecord.Id), 64, true", build, StringComparison.Ordinal);
        Assert.Contains("\"净现金流\", nameof(TradeLogRecord.NetCashImpact), 115, true", build, StringComparison.Ordinal);
        Assert.Contains("\"本金\", nameof(TradeLogRecord.Principal), 115, true", build, StringComparison.Ordinal);
        Assert.Contains("\"现金余额\", nameof(TradeLogRecord.CashBalance), 115, true", build, StringComparison.Ordinal);
        Assert.Contains("\"总资产\", nameof(TradeLogRecord.TotalAssets), 115, true", build, StringComparison.Ordinal);
        Assert.Contains("\"金额\", nameof(TradeLogRecord.Amount), 115, toolTip:", build, StringComparison.Ordinal);
        Assert.DoesNotContain("\"金额\", nameof(TradeLogRecord.Amount), 115, true", build, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyFinancialColumns_FormatOnlyTheirDisplayWithTwoDecimalThousands()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = BuildTradeLogSource();
        string numberColumn = Slice(code, "private void AddTradeLogNumberColumn(", "private static TextBlock CreateTradeLogColumnHeader");
        string model = ReadRepositoryFile("Core", "Models", "TradeLogRecord.cs");

        Assert.Contains("TradeLogFinancialDisplayFormat = \"{0:N2}\"", code, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(build, "TradeLogFinancialDisplayFormat"));
        Assert.Contains("StringFormat = displayFormat", numberColumn, StringComparison.Ordinal);

        foreach (string property in new[] { "NetCashImpact", "Principal", "CashBalance", "TotalAssets" })
        {
            Assert.Contains($"nameof(TradeLogRecord.{property}), 115, true", build, StringComparison.Ordinal);
            Assert.Contains($"public double {property}", model, StringComparison.Ordinal);
        }

        string quantityColumn = build.Split('\n').Single(line => line.Contains("nameof(TradeLogRecord.Quantity)", StringComparison.Ordinal));
        string priceColumn = build.Split('\n').Single(line => line.Contains("nameof(TradeLogRecord.Price)", StringComparison.Ordinal));
        Assert.DoesNotContain("TradeLogFinancialDisplayFormat", quantityColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLogFinancialDisplayFormat", priceColumn, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericEditing_PreservesLostFocusConversionAndValidation()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string method = Slice(code, "private void AddTradeLogNumberColumn(", "private static TextBlock CreateTradeLogColumnHeader");

        Assert.Contains("UpdateSourceTrigger.LostFocus", method, StringComparison.Ordinal);
        Assert.Contains("ValidatesOnExceptions = !readOnly", method, StringComparison.Ordinal);
        Assert.Contains("NotifyOnValidationError = !readOnly", method, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewTextInput", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DropdownOptions_RemainExactlyLocked()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string actions = Slice(code, "private static readonly string[] TradeLogActions", "private static readonly string[] TradeLogSources");
        string sources = Slice(code, "private static readonly string[] TradeLogSources", "private static readonly string[] TradeLogTiers");
        string tiers = Slice(code, "private static readonly string[] TradeLogTiers", "private const string StrategyColumnLayoutKey");

        Assert.Contains("\"买入\", \"卖出\", \"分红\", \"送股\", \"拆分\", \"合并\", \"除权校准\", \"CASH\", \"入金\", \"出金\"", actions, StringComparison.Ordinal);
        Assert.Contains("string.Empty, \"场内ETF\", \"场外替代\", \"CASH\", \"手动录入\"", sources, StringComparison.Ordinal);
        foreach (string tier in new[] { "战略底仓", "止盈减仓(留底)", "狙击一档", "狙击二档", "狙击三档", "狙击四档", "狙击五档", "狙击六档", "周期结束" })
        {
            Assert.Contains($"\"{tier}\"", tiers, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ColumnOrderPersistence_RemainsEnabledWithoutFrozenColumns()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = BuildTradeLogSource();
        string grid = Slice(code, "private DataGrid CreateTradeLogDataGrid()", "private void AddTradeLogRecord()");

        Assert.Contains("private const string TradeLogColumnLayoutKey = \"trade_log\"", code, StringComparison.Ordinal);
        Assert.Contains("FinalizeManualEntryGrid(_tradeLogGrid, TradeLogColumnLayoutKey)", build, StringComparison.Ordinal);
        Assert.Contains("CanUserReorderColumns = true", grid, StringComparison.Ordinal);
        Assert.Contains("SaveManualEntryColumnLayout", code, StringComparison.Ordinal);
        Assert.Contains("ManualEntryColumnLayoutService.ResolveOrder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FrozenColumnCount", build + grid, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogStyles_AreDedicatedAndDoNotReplaceGlobalControls()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string styles = Slice(xaml, "<!-- TradeLog-only workspace resources.", "</Window.Resources>");
        string[] keys =
        {
            "TradeLogWorkspaceCardStyle", "TradeLogToolbarStyle", "TradeLogPrimaryButtonStyle",
            "TradeLogSecondaryButtonStyle", "TradeLogDangerButtonStyle", "TradeLogDataGridStyle",
            "TradeLogDataGridRowStyle", "TradeLogEditableCellStyle", "TradeLogReadOnlyCellStyle",
            "TradeLogColumnHeaderStyle", "TradeLogValidationErrorTemplate"
        };

        foreach (string key in keys)
        {
            Assert.Contains($"x:Key=\"{key}\"", styles, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("<Style TargetType=\"Button\">", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style TargetType=\"DataGrid\">", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogGrid_UsesDenseReadableVisualMetricsAndInternalScrolling()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string styles = Slice(xaml, "x:Key=\"TradeLogDataGridStyle\"", "</Window.Resources>");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string grid = Slice(code, "private DataGrid CreateTradeLogDataGrid()", "private void AddTradeLogRecord()");

        Assert.Contains("ColumnHeaderHeight\" Value=\"38\"", styles, StringComparison.Ordinal);
        Assert.Contains("RowHeight\" Value=\"36\"", styles, StringComparison.Ordinal);
        Assert.Contains("MinRowHeight\" Value=\"34\"", styles, StringComparison.Ordinal);
        Assert.Contains("FontSize\" Value=\"13\"", styles, StringComparison.Ordinal);
        Assert.Contains("GridLinesVisibility\" Value=\"Horizontal\"", styles, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", grid, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableAndCalculatedCells_HaveDistinctVisualsWithoutDivider()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string styles = Slice(xaml, "<!-- TradeLog-only workspace resources.", "</Window.Resources>");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string build = BuildTradeLogSource();

        Assert.Contains("IsEditing", styles, StringComparison.Ordinal);
        Assert.Contains("#0D3048", styles, StringComparison.Ordinal);
        Assert.Contains("TradeLogReadOnlyCellStyle", styles, StringComparison.Ordinal);
        Assert.Contains("#142331", styles, StringComparison.Ordinal);
        Assert.Contains("TradeLogCalculatedColumnHeaderStyle", styles, StringComparison.Ordinal);
        Assert.Contains("#182838", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLogCalculatedDividerCellStyle", styles + code, StringComparison.Ordinal);
        Assert.DoesNotContain("#B27A3D", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("startsCalculatedSection", code, StringComparison.Ordinal);
        Assert.Contains("NetCashImpact), 115, true", build, StringComparison.Ordinal);
    }

    [Fact]
    public void NewUnsavedRowsAndEmptyCollection_HaveNonPersistentVisualFeedback()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("<DataTrigger Binding=\"{Binding Id}\" Value=\"0\">", xaml, StringComparison.Ordinal);
        Assert.Contains("新增记录，尚未保存。", xaml, StringComparison.Ordinal);
        Assert.Contains("暂无交易记录，点击‘新增记录’开始录入。", code, StringComparison.Ordinal);
        Assert.Contains("_tradeLogEmptyStateText.Visibility = _tradeLogs.Count == 0", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLog", Slice(code, "private void UpdateTradeLogWorkspaceState()", "private void SetTradeLogEditState"), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationError_UsesRedCellBorderAndExistingWpfErrorText()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string validation = Slice(xaml, "x:Key=\"TradeLogValidationErrorTemplate\"", "x:Key=\"TradeLogColumnHeaderStyle\"");

        Assert.Contains("BorderBrush=\"#F87171\"", validation, StringComparison.Ordinal);
        Assert.Contains("Validation.Errors", validation, StringComparison.Ordinal);
        Assert.Contains("Validation.HasError", validation, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", validation, StringComparison.Ordinal);
    }

    [Fact]
    public void EditState_CoversLoadAddEditDeleteSavingSuccessAndFailure()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string state = Slice(code, "private void TradeLogs_CollectionChanged", "private static DataGrid CreateDataGrid");
        string save = Slice(code, "private async void SaveTradeLogs()", "private TradeLogSaveResult SaveTradeLogsCore");
        string edit = Slice(code, "private void TradeLogGrid_CellEditEnding", "private void TradeLogGrid_CurrentCellChanged");
        string delete = Slice(code, "private void DeleteSelectedTradeLog()", "private static void BeginEdit");
        string load = Slice(code, "private void LoadData()", "private void LoadAccountFields");

        Assert.Contains("TradeLogEditState.Pending", state, StringComparison.Ordinal);
        Assert.Contains("TradeLogEditState.Saved", load, StringComparison.Ordinal);
        Assert.Contains("TradeLogEditState.Pending", edit, StringComparison.Ordinal);
        Assert.Contains("_tradeLogs.Remove(record)", delete, StringComparison.Ordinal);
        Assert.Contains("TradeLogEditState.Saving", save, StringComparison.Ordinal);
        Assert.Contains("TradeLogEditState.Saved", save, StringComparison.Ordinal);
        Assert.Contains("TradeLogEditState.Failed", save, StringComparison.Ordinal);
        Assert.Contains("当前记录：{_tradeLogs.Count", state, StringComparison.Ordinal);
    }

    [Fact]
    public void UiState_DoesNotPersistOrIntroduceAutoSaveAndClosePrompt()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string state = Slice(code, "private void TradeLogs_CollectionChanged", "private static DataGrid CreateDataGrid");

        Assert.DoesNotContain("_repository", state, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLogs", state, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLog", state, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing +=", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnClosing", code, StringComparison.Ordinal);
        Assert.DoesNotContain("未保存，是否", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SavePipeline_StillCommitsNormalizesPersistsReplaysAndSerializesSaves()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string save = Slice(code, "private async void SaveTradeLogs()", "private TradeLogSaveResult SaveTradeLogsCore");
        string core = Slice(code, "private TradeLogSaveResult SaveTradeLogsCore", "private void TradeLogGrid_CellEditEnding");

        Assert.Contains("if (_isSavingTradeLogs)", save, StringComparison.Ordinal);
        Assert.Contains("SafeCommitTradeLogGridEdits", save, StringComparison.Ordinal);
        Assert.Contains("_tradeLogSaveButton.IsEnabled = false", save, StringComparison.Ordinal);
        Assert.Contains("_tradeLogSaveButton.IsEnabled = true", save, StringComparison.Ordinal);
        Assert.Contains("TradeLogLedgerNormalizer.AutoCalculateTradeAmounts", core, StringComparison.Ordinal);
        Assert.Contains("TryNormalizeLedgerFieldsBeforeSave", core, StringComparison.Ordinal);
        Assert.Contains("ValidateTradeLogForSave", core, StringComparison.Ordinal);
        Assert.Contains("_repository.SaveTradeLogsSnapshot", core, StringComparison.Ordinal);
        Assert.Contains("ReplayAccountFromTradeLogs", core, StringComparison.Ordinal);
        Assert.Contains("AppExceptionLogger.WriteRuntime", save, StringComparison.Ordinal);
        Assert.Contains("TryWriteRuntimeLog", save, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeLogUi_DoesNotChangeSettingsCenterOrWindowFrameContract()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Equal(new[] { "通用设置", "预警与通知", "数据安全", "运行与诊断" }, ManualDataEntryWindow.GetSystemSettingsPageTitles());
        Assert.Contains("MinWidth=\"260\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkStartupShield", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", xaml + code, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContract_IsV860AndAssemblyNameRemainsDefault()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains("<Version>8.6.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>8.6.0.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>8.6.0.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.6.0</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceUi_DoesNotCallPersistenceOrMarketServices()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string workspace = Slice(code, "private UIElement CreateTradeLogWorkspace()", "private static DataGrid CreateDataGrid");

        Assert.DoesNotContain("_repository.", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDataClient", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLogsSnapshot", workspace, StringComparison.Ordinal);
    }

    private static string BuildTradeLogSource()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        return Slice(code, "private void BuildTradeLogTab()", "private void BuildSystemMaintenanceTab()");
    }

    private static string WorkspaceSource()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        return Slice(code, "private UIElement CreateTradeLogWorkspace()", "private Button CreateTradeLogActionButton");
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        int end = text.IndexOf(endMarker, Math.Max(0, start) + startMarker.Length, StringComparison.Ordinal);
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
        string path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray());
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
