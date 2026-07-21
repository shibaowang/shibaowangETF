using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrossETF.Terminal.UiShell.Reference;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class MarketMonitorWindowTests
{
    public static IEnumerable<object[]> QuoteHeaders()
    {
        string[] headers =
        {
            "分类", "名称", "代码", "最新价 / 净值", "涨跌额", "涨跌幅", "昨收", "开盘", "最高",
            "最低", "成交量", "成交额", "IOPV", "数据来源", "行情时间", "本地接收时间", "缓存状态", "缓存年龄"
        };
        return headers.Select(header => new object[] { header });
    }

    public static IEnumerable<object[]> SourceHeaders()
    {
        string[] headers =
        {
            "数据源", "当前状态", "最近成功", "最近失败", "连续失败次数", "冷却至", "最近错误", "更新时间"
        };
        return headers.Select(header => new object[] { header });
    }

    public static IEnumerable<object[]> ForbiddenMonitorCalls()
    {
        string[] calls =
        {
            "MarketDataClient", "MarketDataRefreshService", "RefreshAsync", "FetchTencentEtfQuotesAsync",
            "FetchEastMoneyQuotesAsync", "FetchSinaFundQuotesAsync", "FetchEastMoneyHistoryAsync", "HttpClient",
            "WebRequest", "SaveTradeLog", "SaveMarketQuote", "SaveMarketSourceStatus", "SaveAppSetting",
            "WriteRuntimeLog", "AccountReplayService", "StrategyDecisionService", "OrderDraftService", "MessageBox.Show"
        };
        return calls.Select(call => new object[] { call });
    }

    public static IEnumerable<object[]> RequiredLocalReads()
    {
        string[] calls =
        {
            "ReadStrategyConfigs()", "ReadPositionStates()", "ReadOtcChannels()", "ReadMarketQuoteCache()",
            "ReadMarketSourceStatuses()"
        };
        return calls.Select(call => new object[] { call });
    }

    public static IEnumerable<object[]> VersionFragments()
    {
        string[] fragments =
        {
            "<Version>8.10.7</Version>",
            "<AssemblyVersion>8.10.7.0</AssemblyVersion>",
            "<FileVersion>8.10.7.0</FileVersion>",
            "<InformationalVersion>8.10.7</InformationalVersion>"
        };
        return fragments.Select(fragment => new object[] { fragment });
    }

    [Fact]
    public void Navigation_KeepsOriginalNineItemsAndSingleMarketMonitorName()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string namesBlock = Extract(code, "string[] names =", "for (int i = 0; i < names.Length; i++)");
        string[] names = Regex.Matches(namesBlock, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(9, names.Length);
        Assert.Equal(1, names.Count(name => name == "行情监控"));
        Assert.Equal(
            new[] { "作战总览", "行情监控", "溢价决策", "指标回撤", "资金仓位", "T1-T6看图", "交易日志", "风险中心", "系统设置" },
            names);
    }

    [Fact]
    public void Navigation_MarketMonitorIsActionableButNotManualEntryScope()
    {
        Assert.True(MainWindow.IsMarketMonitorNavigation("行情监控"));
        Assert.True(MainWindow.IsActionableNavigation("行情监控"));
        Assert.Null(MainWindow.ResolveManualEntryScopeForNavigation("行情监控"));
        Assert.False(MainWindow.IsRiskCenterNavigation("行情监控"));
    }

    [Fact]
    public void Navigation_ExistingActionMappingsRemainUnchanged()
    {
        Assert.NotNull(MainWindow.ResolveManualEntryScopeForNavigation("溢价决策"));
        Assert.NotNull(MainWindow.ResolveManualEntryScopeForNavigation("交易日志"));
        Assert.NotNull(MainWindow.ResolveManualEntryScopeForNavigation("系统设置"));
        Assert.True(MainWindow.IsRiskCenterNavigation("风险中心"));
    }

    [Fact]
    public void Navigation_OpensSingleNonModalOwnedMonitorWindow()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void OpenMarketMonitor()", "private void OpenRiskCenter()");

        Assert.Contains("_marketMonitorWindow is { IsVisible: true }", method, StringComparison.Ordinal);
        Assert.Contains("_marketMonitorWindow.Activate();", method, StringComparison.Ordinal);
        Assert.Contains("new MarketMonitorWindow(_repository)", method, StringComparison.Ordinal);
        Assert.Contains("Owner = this", method, StringComparison.Ordinal);
        Assert.Contains("_marketMonitorWindow.Closed += (_, _) => _marketMonitorWindow = null;", method, StringComparison.Ordinal);
        Assert.Contains("_marketMonitorWindow.Show();", method, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(method, "_marketMonitorWindow\\.Activate\\(\\);").Cast<Match>());
        Assert.Single(Regex.Matches(method, "_marketMonitorWindow\\.Show\\(\\);").Cast<Match>());
        Assert.True(method.IndexOf("Owner = this", StringComparison.Ordinal)
                    < method.IndexOf("_marketMonitorWindow.Show();", StringComparison.Ordinal));
        Assert.DoesNotContain("WindowState =", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_SelectsThenRoutesMarketMonitorBeforeOtherWindows()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void NavigationButton_Click", "private void SelectNavigation");

        Assert.True(method.IndexOf("SelectNavigation(navigationName);", StringComparison.Ordinal)
                    < method.IndexOf("IsMarketMonitorNavigation(navigationName)", StringComparison.Ordinal));
        Assert.True(method.IndexOf("OpenMarketMonitor();", StringComparison.Ordinal)
                    < method.IndexOf("IsRiskCenterNavigation(navigationName)", StringComparison.Ordinal));
    }

    [Fact]
    public void Window_UsesRequiredOpaqueNativeDarkShellWithoutDisplayGateOrAnimation()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("Title=\"行情监控中心\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1500\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"860\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"680\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Microsoft YaHei UI\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SnapsToDevicePixels=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0\"", Extract(xaml, "<Window", "<Window.Resources>"), StringComparison.Ordinal);
        Assert.DoesNotContain("ContentRendered=", Extract(xaml, "<Window", "<Window.Resources>"), StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowsTransparency=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DoesNotModifyMainWindowXamlOrAddSecondNavigationEntry()
    {
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");

        Assert.DoesNotContain("MarketMonitorWindow", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("行情监控中心", mainXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasFiveSnapshotSummaryCardsAndNoHardcodedCounts()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("Text=\"{Binding TotalCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding NormalCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DelayedCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ExpiredOrMissingCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SourceSummaryText}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasFourLocalFiltersAndMemoryOnlySearch()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");

        Assert.Equal(4, Regex.Matches(xaml, "GroupName=\"MarketTypeFilter\"").Count);
        Assert.Contains("Content=\"全部\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"指数/汇率\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"场内ETF\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"场外基金\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MarketMonitorSnapshotBuilder.FilterRows", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)\n    {", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasNoManualRefreshButton()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.DoesNotContain("手动刷新", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"刷新\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"重新读取", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(QuoteHeaders))]
    public void QuoteGrid_ContainsEveryRequiredColumn(string header)
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains($"Header=\"{header}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteGrid_IsStrictlyReadOnlyAndInternallyScrollable()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("<Setter Property=\"CanUserAddRows\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CanUserDeleteRows\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"IsReadOnly\" Value=\"True\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ScrollViewer.VerticalScrollBarVisibility\" Value=\"Auto\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteGrid_NameIsStarCodeAndTimesHaveStableWidths()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("Header=\"名称\" Binding=\"{Binding Name}\" Width=\"*\" MinWidth=\"210\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"代码\" Binding=\"{Binding Code}\" Width=\"105\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"行情时间\" Binding=\"{Binding QuoteTime}\" Width=\"154\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"本地接收时间\" Binding=\"{Binding ReceivedAt}\" Width=\"154\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesUnifiedFirstFrameGuardAndKeepsExistingDwmContract()
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");

        string manualCode = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string constructor = Extract(code, "public MarketMonitorWindow(LocalDataRepository repository)", "private void MarketMonitorWindow_Loaded");
        Assert.Contains("SourceInitialized += (_, _) =>", constructor, StringComparison.Ordinal);
        Assert.True(constructor.IndexOf("InitializeComponent();", StringComparison.Ordinal)
                    < constructor.IndexOf("WindowWhiteFlashGuard.Attach(this, MarketMonitorWindowBackgroundColor);", StringComparison.Ordinal));
        Assert.Contains("TryApplyDarkTitleBar();", constructor, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 20", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 19", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 20", manualCode, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 19", manualCode, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmSetWindowAttribute(hwnd, 35", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmSetWindowAttribute(hwnd, 36", code, StringComparison.Ordinal);
        string darkTitleBar = Extract(code, "private void TryApplyDarkTitleBar()", "private static int ToColorRef");
        Assert.Contains("WindowWhiteFlashGuard.Attach(this, MarketMonitorWindowBackgroundColor);", code, StringComparison.Ordinal);
        Assert.Contains("WindowWhiteFlashGuard.Attach(this, ManualWindowBackgroundColor);", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CompositionTarget.BackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0x05, 0x0B, 0x14)", code, StringComparison.Ordinal);
        Assert.Contains("catch", darkTitleBar, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteRuntimeLog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", darkTitleBar, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_RejectsRevealSmoothOpenDelayShieldAndContentClearing()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");

        Assert.DoesNotContain("Opacity=\"0\"", Extract(xaml, "<Window", "<Window.Resources>"), StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity = 0", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_revealQueued", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_revealed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RevealAfterFirstDarkFrame", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Render", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentRendered", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowInteractionEffects.ApplySmoothOpen", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginAnimation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkStartupShield", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = null", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_LoadsInitialLocalSnapshotInConstructorBeforeShowAndFailureStaysInline()
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");
        string mainCode = ReadRepositoryFile("MainWindow.xaml.cs");
        string constructor = Extract(code, "public MarketMonitorWindow(LocalDataRepository repository)", "private void MarketMonitorWindow_Loaded");
        string loaded = Extract(code, "private void MarketMonitorWindow_Loaded", "private void MarketMonitorWindow_Closed");
        string reload = Extract(code, "private void ReloadLocalSnapshot()", "private void FilterButton_Checked");
        string openMonitor = Extract(mainCode, "private void OpenMarketMonitor()", "private void OpenRiskCenter()");

        Assert.Contains("ReloadLocalSnapshot();", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadLocalSnapshot();", loaded, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Start();", loaded, StringComparison.Ordinal);
        Assert.Contains("catch", reload, StringComparison.Ordinal);
        Assert.Contains("本地行情读取失败", reload, StringComparison.Ordinal);
        Assert.True(openMonitor.IndexOf("new MarketMonitorWindow(_repository)", StringComparison.Ordinal)
                    < openMonitor.IndexOf("Owner = this", StringComparison.Ordinal));
        Assert.True(openMonitor.IndexOf("Owner = this", StringComparison.Ordinal)
                    < openMonitor.IndexOf("_marketMonitorWindow.Show();", StringComparison.Ordinal));
        Assert.True(openMonitor.IndexOf("_marketMonitorWindow.Closed +=", StringComparison.Ordinal)
                    < openMonitor.IndexOf("_marketMonitorWindow.Show();", StringComparison.Ordinal));
    }

    [Fact]
    public void Window_WindowAndFullClientRootUseSameDarkBackground()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("Background=\"#050B14\"", Extract(xaml, "<Window", "<Window.Resources>"), StringComparison.Ordinal);
        Assert.Contains("<Grid Margin=\"16\" Background=\"#050B14\" ClipToBounds=\"True\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid Margin=\"16\" Background=\"Transparent\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DwmStylingIsLocalAndKeepsNativeCaptionButtons()
    {
        string monitorXaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string monitorCode = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");
        string appXaml = ReadRepositoryFile("App.xaml");

        Assert.Contains("DwmSetWindowAttribute", monitorCode, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", monitorXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", monitorXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketMonitorScrollBarStyle", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketMonitorScrollBarStyle", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DataGrids_UseWindowScopedDarkScrollBarsForBothOrientations()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("x:Key=\"MarketMonitorScrollBarStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MarketMonitorScrollBarThumbStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MarketMonitorScrollBarLineButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MarketMonitorScrollBarPageButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MarketMonitorVerticalScrollBarTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MarketMonitorHorizontalScrollBarTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(xaml, "TargetType=\"ScrollBar\" BasedOn=\"\\{StaticResource MarketMonitorScrollBarStyle\\}\"").Count);
        Assert.Contains("<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Orientation\" Value=\"Horizontal\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketMonitorScrollBarStyle", ReadRepositoryFile("Views", "RiskCenterWindow.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("MarketMonitorScrollBarStyle", ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteGrid_UsesReadableTypographyAndKeepsAllEighteenColumns()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string quoteGrid = Extract(xaml, "<DataGrid x:Name=\"QuoteGrid\"", "<TextBlock Grid.Row=\"8\"");

        Assert.Contains("Style=\"{StaticResource MarketMonitorQuoteGridStyle}\"", quoteGrid, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ColumnHeaderHeight\" Value=\"32\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RowHeight\" Value=\"26\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinRowHeight\" Value=\"25\" />", xaml, StringComparison.Ordinal);
        Assert.Equal(18, Regex.Matches(quoteGrid, "<DataGrid(?:Text|Template)Column Header=").Count);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersAndSearch_UseReadableFixedHeightsWithoutManualRefresh()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        string filterStyle = Extract(xaml, "<Style x:Key=\"MonitorFilterStyle\"", "<Style x:Key=\"MonitorSearchTextBoxStyle\"");
        string searchStyle = Extract(xaml, "<Style x:Key=\"MonitorSearchTextBoxStyle\"", "<Style x:Key=\"MarketMonitorDataGridBaseStyle\"");

        Assert.Contains("<Setter Property=\"Height\" Value=\"32\" />", filterStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", filterStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"32\" />", searchStyle, StringComparison.Ordinal);
        Assert.Contains("Width=\"250\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin", searchStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"刷新\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGrid_HasReadableHeightTypographyAndFullErrorToolTip()
    {
        string xaml = SourceTextTestHelper.NormalizeLineEndings(
            ReadRepositoryFile("Views", "MarketMonitorWindow.xaml"));
        string sourceGrid = SourceTextTestHelper.Slice(
            xaml,
            "<DataGrid x:Name=\"SourceStatusGrid\"",
            "</Grid>\n</Window>");

        Assert.Contains("MinHeight=\"150\"", sourceGrid, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MarketMonitorSourceGridStyle}\"", sourceGrid, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ColumnHeaderHeight\" Value=\"30\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RowHeight\" Value=\"26\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"最近错误\" Width=\"*\" MinWidth=\"240\"", sourceGrid, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LastError}\" ToolTip=\"{Binding LastError}\"", sourceGrid, StringComparison.Ordinal);
        Assert.Contains("Binding FailureCountText", sourceGrid, StringComparison.Ordinal);
        Assert.Contains("Binding CooldownUntil", sourceGrid, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_HasNoOuterScrollViewerWhileTextBoxKeepsItsRequiredContentHost()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement[] scrollViewers = XDocument.Parse(xaml).Descendants(presentation + "ScrollViewer").ToArray();

        Assert.Single(scrollViewers);
        Assert.Equal("PART_ContentHost", (string?)scrollViewers[0].Attribute(x + "Name"));
    }

    [Fact]
    public void QuoteGrid_UsesTrendColorsAndNullDisplayModelsWithoutFakeZero()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string model = ReadRepositoryFile("Core", "Models", "MarketMonitorQuoteRow.cs");

        Assert.Contains("Color=\"#FF4D57\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#84CC16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"上涨\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"下跌\"", xaml, StringComparison.Ordinal);
        Assert.Contains("= \"--\";", model, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetNullValue=0", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuoteGrid_LongSourceAndFullVolumeAmountUseToolTips()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("ToolTip=\"{Binding Source}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding VolumeFullText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding AmountFullText}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SourceHeaders))]
    public void SourceGrid_ContainsEveryRequiredColumn(string header)
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains($"Header=\"{header}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGrid_DoesNotHideFailureCooldownOrFullError()
    {
        string xaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");

        Assert.Contains("Binding FailureCountText", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding CooldownUntil", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LastError}\" ToolTip=\"{Binding LastError}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredLocalReads))]
    public void Timer_UsesEachRequiredReadOnlyRepositoryQuery(string readCall)
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");

        Assert.Contains(readCall, code, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ForbiddenMonitorCalls))]
    public void MonitorWindowAndBuilder_DoNotCallForbiddenBusinessOrNetworkPath(string forbiddenCall)
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs")
                      + ReadRepositoryFile("Core", "Services", "MarketMonitorSnapshotBuilder.cs");

        Assert.DoesNotContain(forbiddenCall, code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Timer_StartsAfterConstructionSnapshotAndRefreshesEveryTwoSeconds()
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");
        string constructor = Extract(code, "public MarketMonitorWindow(LocalDataRepository repository)", "private void MarketMonitorWindow_Loaded");
        string loadedMethod = Extract(code, "private void MarketMonitorWindow_Loaded", "private void MarketMonitorWindow_Closed");

        Assert.Contains("TimeSpan.FromSeconds(2)", code, StringComparison.Ordinal);
        Assert.Contains("ReloadLocalSnapshot();", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadLocalSnapshot();", loadedMethod, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Start();", loadedMethod, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Timer_CloseStopsAndDetachesWithoutBackgroundThread()
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");
        string closedMethod = Extract(code, "private void MarketMonitorWindow_Closed", "private void LocalRefreshTimer_Tick");

        Assert.Contains("_localRefreshTimer.Stop();", closedMethod, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Tick -= LocalRefreshTimer_Tick;", closedMethod, StringComparison.Ordinal);
        Assert.Contains("Loaded -= MarketMonitorWindow_Loaded;", closedMethod, StringComparison.Ordinal);
        Assert.Contains("Closed -= MarketMonitorWindow_Closed;", closedMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thread", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Timer_ReadFailureKeepsLastSnapshotAndOnlyShowsInlineStatus()
    {
        string code = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");
        string method = Extract(code, "private void ReloadLocalSnapshot()", "private void FilterButton_Checked");
        string catchBlock = method[method.IndexOf("catch", StringComparison.Ordinal)..];

        Assert.Contains("本地行情读取失败", catchBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_lastSnapshot = null", catchBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("QuoteGrid.ItemsSource", catchBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", catchBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", catchBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotBuilder_IsPureAndHasNoWpfRepositoryOrGlobalClockDependency()
    {
        string builder = ReadRepositoryFile("Core", "Services", "MarketMonitorSnapshotBuilder.cs");

        Assert.DoesNotContain("System.Windows", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDataRepository", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Http", builder, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(VersionFragments))]
    public void VersionContract_IsV870(string fragment)
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains(fragment, project, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContract_PreservesAssemblyNameAndExistingReader()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string settings = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
        Assert.Equal("V8.10.7", MainWindow.ResolveDisplayVersion());
        Assert.Contains("AssemblyInformationalVersionAttribute", main, StringComparison.Ordinal);
        Assert.Contains("(\"当前版本\", MainWindow.ResolveDisplayVersion())", settings, StringComparison.Ordinal);
    }

    private static string Extract(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to extract source between {startMarker} and {endMarker}.");
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(parts));
    }
}
