using System.Text.RegularExpressions;
using System.Windows.Interop;
using CrossETF.Terminal.UiShell.Reference;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class CapitalPositionWindowTests
{
    private static readonly string[] EtfHeaders =
    {
        "策略代码", "ETF名称", "实际代码", "持仓数量", "综合成本", "最新价格", "持仓市值",
        "浮动盈亏", "盈亏比例", "资产占比", "行情来源", "行情时间", "本地接收时间", "缓存状态"
    };

    private static readonly string[] OtcHeaders =
    {
        "策略代码", "基金代码", "基金名称", "持有份额", "持仓成本", "单位成本", "最新净值",
        "持仓市值", "浮动盈亏", "盈亏比例", "资产占比", "通道优先级", "净值来源", "净值时间", "缓存状态"
    };

    [Fact]
    public void Navigation_KeepsOriginalSingleEntryNameOrderAndIconArrays()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string block = Extract(code, "string[] icons =", "for (int i = 0; i < names.Length; i++)");
        string namesBlock = Extract(block, "string[] names =", "};");
        string[] names = Regex.Matches(namesBlock, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(9, names.Length);
        Assert.Equal(1, names.Count(name => name == "资金仓位"));
        Assert.Equal(4, Array.IndexOf(names, "资金仓位"));
        Assert.Contains("\"▣\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_CapitalPositionIsActionableButNeverManualEntryScope()
    {
        Assert.True(MainWindow.IsCapitalPositionNavigation("资金仓位"));
        Assert.True(MainWindow.IsActionableNavigation("资金仓位"));
        Assert.Null(MainWindow.ResolveManualEntryScopeForNavigation("资金仓位"));
        Assert.False(MainWindow.IsMarketMonitorNavigation("资金仓位"));
        Assert.False(MainWindow.IsRiskCenterNavigation("资金仓位"));
    }

    [Fact]
    public void Navigation_UsesDedicatedSingleInstanceWindowWithCorrectFirstShowOrder()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void OpenCapitalPosition()", "private void DrawSparklines()");
        string createPath = method[method.IndexOf("_capitalPositionWindow = new", StringComparison.Ordinal)..];

        Assert.Contains("private CapitalPositionWindow? _capitalPositionWindow;", code, StringComparison.Ordinal);
        Assert.Contains("new CapitalPositionWindow(_repository)", createPath, StringComparison.Ordinal);
        Assert.Contains("Owner = this", createPath, StringComparison.Ordinal);
        Assert.Contains("_capitalPositionWindow.Closed += (_, _) => _capitalPositionWindow = null;", createPath, StringComparison.Ordinal);
        Assert.Contains("_capitalPositionWindow.Show();", createPath, StringComparison.Ordinal);
        Assert.True(createPath.IndexOf("Owner = this", StringComparison.Ordinal)
                    < createPath.IndexOf("_capitalPositionWindow.Closed +=", StringComparison.Ordinal));
        Assert.True(createPath.IndexOf("_capitalPositionWindow.Closed +=", StringComparison.Ordinal)
                    < createPath.IndexOf("_capitalPositionWindow.Show();", StringComparison.Ordinal));
        Assert.DoesNotContain("Activate", createPath, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus", createPath, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_RepeatedClickRestoresMinimizedWindowAndOnlyActivates()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void OpenCapitalPosition()", "private void DrawSparklines()");
        string repeatPath = method[..method.IndexOf("_capitalPositionWindow = new", StringComparison.Ordinal)];

        Assert.Contains("_capitalPositionWindow is { IsVisible: true }", repeatPath, StringComparison.Ordinal);
        Assert.Contains("WindowState == WindowState.Minimized", repeatPath, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Normal", repeatPath, StringComparison.Ordinal);
        Assert.Contains("_capitalPositionWindow.Activate();", repeatPath, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus", repeatPath, StringComparison.Ordinal);
        Assert.Contains("return;", repeatPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_CloseHandlerMatchesMarketMonitorAndClearsMainWindowReference()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void OpenCapitalPosition()", "private void DrawSparklines()");

        Assert.Contains("_capitalPositionWindow.Closed += (_, _) => _capitalPositionWindow = null;", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CapitalPositionWindow_Closed", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesOpaqueNativeDarkResizableShell()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string window = Extract(xaml, "<Window", "<Window.Resources>");

        Assert.Contains("Title=\"资金仓位中心\"", window, StringComparison.Ordinal);
        Assert.Contains("Width=\"1500\"", window, StringComparison.Ordinal);
        Assert.Contains("Height=\"860\"", window, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1180\"", window, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"680\"", window, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", window, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", window, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", window, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", window, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Microsoft YaHei UI\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowsTransparency=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_RootAndUnifiedFirstFrameGuardUseSameDarkBackground()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string constructor = Extract(code, "public CapitalPositionWindow(LocalDataRepository repository)", "private void CapitalPositionWindow_Loaded");

        Assert.Contains("<Grid Margin=\"14\" Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0x05, 0x0B, 0x14)", code, StringComparison.Ordinal);
        Assert.True(constructor.IndexOf("InitializeComponent();", StringComparison.Ordinal)
                    < constructor.IndexOf("WindowWhiteFlashGuard.Attach(this, CapitalWindowBackgroundColor);", StringComparison.Ordinal));
        Assert.Contains("TryApplyDarkTitleBar();", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CompositionTarget.BackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 20", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 19", code, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_WhiteFlashContractMatchesStableMarketMonitorImplementation()
    {
        string capitalXaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string capitalCode = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string marketXaml = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml");
        string marketCode = ReadRepositoryFile("Views", "MarketMonitorWindow.xaml.cs");

        string capitalWindow = Extract(capitalXaml, "<Window", "<Window.Resources>");
        string marketWindow = Extract(marketXaml, "<Window", "<Window.Resources>");
        foreach (string value in new[]
                 {
                     "Background=\"#050B14\"", "ResizeMode=\"CanResize\"", "ShowInTaskbar=\"False\"",
                     "WindowStartupLocation=\"CenterOwner\""
                 })
        {
            Assert.Contains(value, capitalWindow, StringComparison.Ordinal);
            Assert.Contains(value, marketWindow, StringComparison.Ordinal);
        }

        Assert.Equal(
            Regex.Matches(marketCode, "DwmSetWindowAttribute\\(hwnd, (19|20|34)").Select(match => match.Groups[1].Value),
            Regex.Matches(capitalCode, "DwmSetWindowAttribute\\(hwnd, (19|20|34)").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("DwmSetWindowAttribute(hwnd, 35", capitalCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmSetWindowAttribute(hwnd, 36", capitalCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentRendered", capitalXaml + capitalCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Activated=", capitalXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_RejectsOpacityRevealAnimationShieldDelayAndSmoothOpen()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string combined = xaml + code;

        Assert.DoesNotContain("Opacity=\"0\"", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity = 0", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Reveal", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DoubleAnimation", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkStartupShield", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowInteractionEffects.ApplySmoothOpen", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = null", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_LoadsInitialReadModelInConstructorAndStartsFixedTimerOnlyAfterLoaded()
    {
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string constructor = Extract(code, "public CapitalPositionWindow(LocalDataRepository repository)", "private void CapitalPositionWindow_Loaded");
        string loaded = Extract(code, "private void CapitalPositionWindow_Loaded", "private void CapitalPositionWindow_Closed");

        Assert.Contains("TimeSpan.FromSeconds(2)", code, StringComparison.Ordinal);
        Assert.Contains("ReloadLocalSnapshot();", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("_localRefreshTimer.Start();", constructor, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Start();", loaded, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_BindsAllInitialTablesBeforeFirstShow()
    {
        string windowCode = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string mainCode = ReadRepositoryFile("MainWindow.xaml.cs");
        string apply = Extract(windowCode, "private void ApplySnapshot", "private void TryApplyDarkTitleBar()");
        string open = Extract(mainCode, "private void OpenCapitalPosition()", "private void DrawSparklines()");

        Assert.Contains("DataContext = snapshot;", apply, StringComparison.Ordinal);
        Assert.Contains("StrategyAllocationGrid.ItemsSource = snapshot.StrategyRows;", apply, StringComparison.Ordinal);
        Assert.Contains("EtfPositionGrid.ItemsSource = snapshot.EtfRows;", apply, StringComparison.Ordinal);
        Assert.Contains("OtcPositionGrid.ItemsSource = snapshot.OtcRows;", apply, StringComparison.Ordinal);
        Assert.True(open.IndexOf("new CapitalPositionWindow(_repository)", StringComparison.Ordinal)
                    < open.IndexOf("Owner = this", StringComparison.Ordinal));
        Assert.True(open.IndexOf("Owner = this", StringComparison.Ordinal)
                    < open.IndexOf("_capitalPositionWindow.Show();", StringComparison.Ordinal));
    }

    [Fact]
    public void Window_ClosedStopsTimerUnsubscribesAndGuardsClosedTicks()
    {
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string closed = Extract(code, "private void CapitalPositionWindow_Closed", "private void LocalRefreshTimer_Tick");
        string tick = Extract(code, "private void LocalRefreshTimer_Tick", "private void ReloadLocalSnapshot()");

        Assert.Contains("_closed = true;", closed, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Stop();", closed, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Tick -= LocalRefreshTimer_Tick;", closed, StringComparison.Ordinal);
        Assert.Contains("Loaded -= CapitalPositionWindow_Loaded;", closed, StringComparison.Ordinal);
        Assert.Contains("Closed -= CapitalPositionWindow_Closed;", closed, StringComparison.Ordinal);
        Assert.Contains("if (!_closed)", tick, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_RefreshReadsOnlyUnifiedRepositorySnapshotAndKeepsLastSuccessOnFailure()
    {
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string reload = Extract(code, "private void ReloadLocalSnapshot()", "private void ApplySnapshot");

        Assert.Contains("_repository.ReadCapitalPositionReadModel()", reload, StringComparison.Ordinal);
        Assert.Contains("_snapshotBuilder.Build", reload, StringComparison.Ordinal);
        Assert.Contains("保留上次成功数据", reload, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = null", reload, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", reload, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteRuntimeLog", reload, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EtfHeaderData))]
    public void EtfGrid_ContainsRequiredHeader(string header)
    {
        string grid = ExtractGrid("EtfPositionGrid");
        Assert.Contains($"Header=\"{header}\"", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void EtfGrid_HasExactlyFourteenColumnsAndNoForbiddenAvailableOrTierColumns()
    {
        string grid = ExtractGrid("EtfPositionGrid");

        Assert.Equal(14, Regex.Matches(grid, "Header=\"").Count);
        Assert.DoesNotContain("可用数量", grid, StringComparison.Ordinal);
        Assert.DoesNotContain("当前档位", grid, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(OtcHeaderData))]
    public void OtcGrid_ContainsRequiredHeader(string header)
    {
        string grid = ExtractGrid("OtcPositionGrid");
        Assert.Contains($"Header=\"{header}\"", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void OtcGrid_HasExactlyFifteenColumns()
    {
        string grid = ExtractGrid("OtcPositionGrid");
        Assert.Equal(15, Regex.Matches(grid, "Header=\"").Count);
    }

    [Fact]
    public void PositionGrids_AreStrictlyReadOnlyInternallyScrollableAndStableWidth()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");

        Assert.Contains("<Setter Property=\"IsReadOnly\" Value=\"True\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CanUserAddRows\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CanUserDeleteRows\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CanUserReorderColumns\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"AutoGenerateColumns\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ScrollViewer.VerticalScrollBarVisibility\" Value=\"Auto\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RowHeight\" Value=\"26\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ColumnHeaderHeight\" Value=\"32\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasTwelveRequiredSummaryCardsAndThreeReadOnlyStructureRatios()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string[] labels =
        {
            "总资产", "可用现金", "累计本金", "持仓总市值", "场内 ETF 市值", "场外基金市值",
            "持仓浮动盈亏", "已实现盈亏", "资金使用率", "狙击资金池", "底仓完成度", "回放/估值状态"
        };

        Assert.All(labels, label => Assert.Contains($"Text=\"{label}\"", xaml, StringComparison.Ordinal));
        Assert.Contains("Text=\"现金占比\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"策略市值占比\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding StrategyRows}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StructureRatios_UseReadableTrackAndThreeDistinctNeutralCategoryFills()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string progressStyle = Extract(xaml, "<Style x:Key=\"CapitalProgressStyle\"", "<Style x:Key=\"CapitalCashProgressStyle\"");

        Assert.Contains("<Setter Property=\"Height\" Value=\"9\" />", progressStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"#10283A\" />", progressStyle, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"4\"", progressStyle, StringComparison.Ordinal);
        Assert.Contains("CapitalCashProgressStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"#4F8FC9\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CapitalEtfProgressStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"#27A8C4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CapitalOtcProgressStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"#2A9D8F\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Summary.CashRatioText}\" Foreground=\"{Binding Summary.CashBalanceColor}\" FontSize=\"13\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyAllocationArea_IsElasticAndKeepsReadableRowsAndColumns()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string grid = ExtractGrid("StrategyAllocationGrid");

        Assert.Contains("<RowDefinition Height=\"1.25*\" MinHeight=\"125\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"6\" MinHeight=\"125\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"13\" RowHeight=\"26\" ColumnHeaderHeight=\"31\"", grid, StringComparison.Ordinal);
        Assert.Contains("Header=\"策略代码\" Binding=\"{Binding StrategyCode}\" Width=\"95\"", grid, StringComparison.Ordinal);
        Assert.Contains("Header=\"策略名称\" Binding=\"{Binding StrategyName}\" Width=\"*\" MinWidth=\"180\" MaxWidth=\"300\"", grid, StringComparison.Ordinal);
        Assert.Contains("Header=\"总资产占比\" Width=\"100\"", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void PositionNameColumns_AreBoundedTrimmedAndKeepInternalHorizontalScroll()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string etfGrid = ExtractGrid("EtfPositionGrid");
        string otcGrid = ExtractGrid("OtcPositionGrid");

        Assert.Contains("Header=\"ETF名称\" Width=\"240\"", etfGrid, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EtfName}\" ToolTip=\"{Binding EtfName}\" TextTrimming=\"CharacterEllipsis\"", etfGrid, StringComparison.Ordinal);
        Assert.Contains("Header=\"基金名称\" Width=\"270\"", otcGrid, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FundName}\" ToolTip=\"{Binding FundName}\" TextTrimming=\"CharacterEllipsis\"", otcGrid, StringComparison.Ordinal);
        Assert.Equal(14, Regex.Matches(etfGrid, "Header=\"").Count);
        Assert.Equal(15, Regex.Matches(otcGrid, "Header=\"").Count);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryCards_KeepAbsoluteValuesNeutralAndOnlyPnlUsesDirectionalColors()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string cards = Extract(xaml, "<UniformGrid Grid.Row=\"4\"", "</UniformGrid>");

        Assert.Contains("x:Key=\"CapitalSummaryTitleStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource CapitalTextBrush}\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary.TotalAssetsColor", cards, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary.CashBalanceColor", cards, StringComparison.Ordinal);
        Assert.Contains("Summary.TotalUnrealizedPnlColor", cards, StringComparison.Ordinal);
        Assert.Contains("Summary.TotalRealizedPnlColor", cards, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(cards, "Total(Unrealized|Realized)PnlColor").Count);
        Assert.Contains("Summary.ReplayStatusColor", cards, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_IsReadableFixedHeightTrimmedAndFullyAvailableByTooltip()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string footer = Extract(xaml, "<Grid Grid.Row=\"12\"", "</Grid>");

        Assert.Contains("MinHeight=\"28\"", footer, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(footer, "FontSize=\"12.5\"").Count);
        Assert.Equal(2, Regex.Matches(footer, "TextTrimming=\"CharacterEllipsis\"").Count);
        Assert.Contains("ToolTip=\"{Binding DataSourceExplanation}\"", footer, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding ReplayError}\"", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasNoManualRefreshEditSaveImportExportOrBusinessServiceCalls()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs");
        string combined = xaml + code;
        string[] forbidden =
        {
            "手动刷新", "Content=\"刷新\"", "Content=\"保存\"", "Content=\"编辑\"", "导入", "导出",
            "AccountReplayService", "StrategyDecisionService", "OrderDraftService", "MarketDataClient",
            "MarketDataRefreshService", "RefreshAsync", "SaveTradeLog", "SaveMarketQuote", "WriteRuntimeLog"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, combined, StringComparison.Ordinal));
    }

    [Fact]
    public void Window_DarkScrollBarsAreScopedLocallyAndSupportBothOrientations()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string appXaml = ReadRepositoryFile("App.xaml");
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");

        Assert.Contains("x:Key=\"CapitalScrollBarStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CapitalVerticalScrollTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CapitalHorizontalScrollTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Orientation\" Value=\"Horizontal\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CapitalScrollBarStyle", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CapitalScrollBarStyle", mainXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesNoOuterScrollViewerAndTablesOwnTheirScrolling()
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        string body = xaml[xaml.IndexOf("<Grid Margin=\"14\"", StringComparison.Ordinal)..];

        Assert.DoesNotContain("<ScrollViewer", body, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EtfPositionGrid\"", body, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OtcPositionGrid\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossModuleBoundary_DoesNotTouchMainWindowXamlOrLockedWindowTypes()
    {
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");
        string code = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml.cs")
                      + ReadRepositoryFile("Core", "Services", "CapitalPositionSnapshotBuilder.cs");

        Assert.DoesNotContain("CapitalPositionWindow", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualDataEntryWindow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketMonitorWindow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RiskCenterWindow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContract_IsV880AndAssemblyNameRemainsSdkDefault()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains("<Version>8.10.5</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>8.10.5.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>8.10.5.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.10.5</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
        Assert.Equal("V8.10.5", MainWindow.ResolveDisplayVersion());
    }

    [Fact]
    public void Window_ConstructsOnStaWithIsolatedTemporaryDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), "cross-etf-capital-window-tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "window.db");
        Exception? failure = null;
        string? title = null;
        IntPtr handle = IntPtr.Zero;
        bool hasEtfGrid = false;
        bool hasOtcGrid = false;
        var thread = new Thread(() =>
        {
            try
            {
                var repository = new LocalDataRepository(new LocalDatabase(databasePath));
                var window = new CapitalPositionWindow(repository);
                title = window.Title;
                window.Show();
                handle = new WindowInteropHelper(window).Handle;
                hasEtfGrid = window.FindName("EtfPositionGrid") is not null;
                hasOtcGrid = window.FindName("OtcPositionGrid") is not null;
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        try
        {
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA window construction timed out.");
            Assert.Null(failure);
            Assert.Equal("资金仓位中心", title);
            Assert.NotEqual(IntPtr.Zero, handle);
            Assert.True(hasEtfGrid);
            Assert.True(hasOtcGrid);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static IEnumerable<object[]> EtfHeaderData()
        => EtfHeaders.Select(header => new object[] { header });

    public static IEnumerable<object[]> OtcHeaderData()
        => OtcHeaders.Select(header => new object[] { header });

    private static string ExtractGrid(string name)
    {
        string xaml = ReadRepositoryFile("Views", "CapitalPositionWindow.xaml");
        int start = xaml.IndexOf($"<DataGrid x:Name=\"{name}\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("</DataGrid>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to extract grid {name}.");
        return xaml[start..(end + "</DataGrid>".Length)];
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
