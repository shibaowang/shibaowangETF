using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class IndicatorDrawdownWindowTests
{
    private static readonly string[] ExpectedHeaders =
    {
        "分类", "名称", "代码", "关联策略", "最新价", "历史最高收盘", "高点日期", "当前回撤",
        "20 日回撤", "60 日回撤", "120 日回撤", "252 日回撤", "年初至今回撤", "历史最大回撤",
        "最大回撤区间", "历史数据", "数据来源", "数据状态"
    };

    [Fact]
    public void Window_UsesLockedNativeFrameDimensionsAndDeepBackground()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");

        Assert.Contains("Width=\"1500\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"860\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"680\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Microsoft YaHei UI\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesNativeTitleBarWithoutTransparencyOrWindowChrome()
    {
        string combined = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml")
                          + ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");

        Assert.DoesNotContain("WindowStyle=\"None\"", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowsTransparency=\"True\"", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleMinimizeButton", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleMaximizeButton", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleCloseButton", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasExactlyEighteenLockedStaticColumns()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");
        MatchCollection columns = Regex.Matches(xaml, "<DataGrid(?:Text|Template)Column\\b");

        Assert.Equal(18, columns.Count);
        foreach (string header in ExpectedHeaders)
        {
            Assert.Single(Regex.Matches(xaml, $"Header=\"{Regex.Escape(header)}\"").Cast<Match>());
        }
    }

    [Fact]
    public void Window_StatusAndPeriodCellsExposeAuditToolTips()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");

        Assert.Contains("x:Key=\"IndicatorPeriodTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding PeriodDataToolTip}", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding DataStatusDetail}", xaml, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(xaml, "ElementStyle=\"{StaticResource IndicatorPeriodTextStyle}\"").Count);
    }

    [Fact]
    public void Window_BindsAnEmptySnapshotBeforeShowWhenInitialLocalReadFails()
    {
        string source = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");
        string failureBranch = ExtractAfter(source, "if (!string.IsNullOrWhiteSpace(model.ReadError))");

        Assert.Contains("_lastSnapshot = new IndicatorDrawdownSnapshot", failureBranch, StringComparison.Ordinal);
        Assert.Contains("ApplySnapshot(_lastSnapshot);", failureBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_IsStrictlyReadOnlyAndDataGridOwnsBothScrollDirections()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");

        Assert.Contains("IsReadOnly\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanUserDeleteRows\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility\" Value=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", ExtractAfter(xaml, "<Grid Background=\"#050B14\""), StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DarkScrollBarsAreLocallyScopedOnly()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");
        string app = ReadRepositoryFile("App.xaml");
        string main = ReadRepositoryFile("MainWindow.xaml");

        Assert.Contains("x:Key=\"IndicatorScrollBarStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IndicatorVerticalScrollTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IndicatorHorizontalScrollTemplate\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IndicatorScrollBarStyle", app, StringComparison.Ordinal);
        Assert.DoesNotContain("IndicatorScrollBarStyle", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasSevenSummaryCardsThreeFiltersSearchDetailAndMethodology()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");

        Assert.Contains("<UniformGrid Grid.Row=\"2\" Columns=\"7\">", xaml, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Matches(Extract(xaml, "<UniformGrid Grid.Row=\"2\" Columns=\"7\">", "</UniformGrid>"), "<Border\\b").Count);
        Assert.Contains("Text=\"异常/缺失\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AbnormalOrMissingCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding AbnormalOrMissingToolTip}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"无历史 / 损坏\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"全部\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"指数\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"场内 ETF\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedDetailPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("方法说明", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_SelectedInstrumentDetailIsAReadableThreeRowPanelWithFullValueToolTips()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");
        string detail = Extract(xaml, "<Grid x:Name=\"SelectedDetailPanel\">", "</Border>");

        Assert.Contains("Height=\"104\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"90\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#071825\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"#163A50\"", xaml, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(detail, "<RowDefinition Height=\"\\*\" />").Count);
        Assert.Contains("选中标的：", detail, StringComparison.Ordinal);
        Assert.Contains("分类：", detail, StringComparison.Ordinal);
        Assert.Contains("关联策略：", detail, StringComparison.Ordinal);
        Assert.Contains("历史来源：", detail, StringComparison.Ordinal);
        Assert.Contains("实时来源：", detail, StringComparison.Ordinal);
        Assert.Contains("历史区间：", detail, StringComparison.Ordinal);
        Assert.Contains("数据点数：", detail, StringComparison.Ordinal);
        Assert.Contains("历史最高：", detail, StringComparison.Ordinal);
        Assert.Contains("当前回撤：", detail, StringComparison.Ordinal);
        Assert.Contains("历史最大回撤：", detail, StringComparison.Ordinal);
        Assert.Contains("最大回撤区间：", detail, StringComparison.Ordinal);
        Assert.Contains("状态说明：", detail, StringComparison.Ordinal);
        Assert.Contains("FontSize\" Value=\"13\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming\" Value=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_MethodologyRemainsSeparateReadableAndDoesNotAddAChart()
    {
        string xaml = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml");
        string methodology = Extract(xaml, "<TextBlock Grid.Row=\"10\"", "</Window>");

        Assert.Contains("MinHeight=\"28\"", methodology, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"12.5\"", methodology, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", methodology, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", methodology, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Polyline", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasNoManualRefreshWriteNetworkReplayOrChartDependencies()
    {
        string combined = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml")
                          + ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs")
                          + ReadRepositoryFile("Core", "Services", "IndicatorDrawdownMetricsBuilder.cs");
        string[] forbidden =
        {
            "手动刷新", "Content=\"刷新\"", "MarketDataClient", "MarketDataRefreshService", "RefreshAsync",
            "SaveMarket", "SaveTrade", "WriteRuntimeLog", "TradeLog", "AccountReplayService", "StrategyDecisionService",
            "OrderDraftService", "SecurityChartWindow", "IndexDrawdownChartSeriesBuilder", "KLineAggregator"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, combined, StringComparison.Ordinal));
    }

    [Fact]
    public void Window_UsesNoOpacityRevealFadeShieldSleepOrFixedDelay()
    {
        string combined = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml")
                          + ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");
        string[] forbidden =
        {
            "Opacity=\"0\"", "Reveal", "Fade", "DarkStartupShield", "Thread.Sleep", "Task.Delay",
            "ApplySmoothOpen", "Content = null", "BeginAnimation"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, combined, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Window_AttachesUnifiedFirstFrameGuardAndPreloadsBeforeShow()
    {
        string code = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");
        string sourceInitialized = Extract(code, "private void IndicatorDrawdownWindow_SourceInitialized", "private void IndicatorDrawdownWindow_Loaded");
        string constructor = Extract(code, "public IndicatorDrawdownWindow", "private void IndicatorDrawdownWindow_SourceInitialized");

        Assert.True(constructor.IndexOf("InitializeComponent();", StringComparison.Ordinal)
                    < constructor.IndexOf("WindowWhiteFlashGuard.Attach(this, IndicatorWindowBackgroundColor);", StringComparison.Ordinal));
        Assert.Contains("TryApplyDarkTitleBar();", sourceInitialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CompositionTarget.BackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0x05, 0x0B, 0x14)", code, StringComparison.Ordinal);
        Assert.Contains("LoadInitialSnapshotBeforeShow();", constructor, StringComparison.Ordinal);
        Assert.Contains("ApplySnapshot(_lastSnapshot);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesTwoSecondRealtimeAndThirtySecondMetadataCadence()
    {
        string code = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");

        Assert.Contains("TimeSpan.FromSeconds(2)", code, StringComparison.Ordinal);
        Assert.Contains("HistoryMetadataCheckTickInterval = 15", code, StringComparison.Ordinal);
        Assert.Contains("ReadIndicatorDrawdownRealtimeState(includeHistoryMetadata)", code, StringComparison.Ordinal);
        Assert.Contains("ReadIndicatorDrawdownHistoryCandidates(changed)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadIndicatorDrawdownReadModel();\n        ApplySnapshot", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HandlesAddedInstrumentsOnTheOrdinaryTwoSecondReadWithoutWaitingForMetadataTick()
    {
        string code = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");
        string tick = Extract(code, "private async void LocalRefreshTimer_Tick", "private async Task RefreshChangedHistoryAsync");

        Assert.Contains("addedInstruments", tick, StringComparison.Ordinal);
        Assert.Contains("if (!includeHistoryMetadata)", tick, StringComparison.Ordinal);
        Assert.Contains("RefreshChangedHistoryAsync", tick, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HistoryRecomputeIsBackgroundCancelableAndPreservesLastSnapshotOnFailure()
    {
        string code = ReadRepositoryFile("Views", "IndicatorDrawdownWindow.xaml.cs");

        Assert.Contains("Task.Run", code, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", code, StringComparison.Ordinal);
        Assert.Contains("_historyRefreshRunning", code, StringComparison.Ordinal);
        Assert.Contains("保留上次结果", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigation_ConnectsExistingIndicatorEntryAsSingleInstanceShowWindow()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(code, "private void OpenIndicatorDrawdown()", "private void DrawSparklines()");

        Assert.True(MainWindow.IsIndicatorDrawdownNavigation("指标回撤"));
        Assert.True(MainWindow.IsActionableNavigation("指标回撤"));
        Assert.Contains("_indicatorDrawdownWindow is { IsVisible: true }", method, StringComparison.Ordinal);
        Assert.Contains("WindowState.Minimized", method, StringComparison.Ordinal);
        Assert.Contains("Activate();", method, StringComparison.Ordinal);
        Assert.Contains("Owner = this", method, StringComparison.Ordinal);
        Assert.Contains(".Show();", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXamlRemainsUnchangedByDynamicNavigationConnection()
    {
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");

        Assert.DoesNotContain("IndicatorDrawdownWindow", mainXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ConstructsAndBindsInitialRowsOnStaWithTemporaryDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), "cross-etf-indicator-window-tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "window.db");
        Exception? failure = null;
        int rowCount = -1;
        IntPtr handle = IntPtr.Zero;
        var thread = new Thread(() =>
        {
            try
            {
                var repository = new LocalDataRepository(new LocalDatabase(databasePath));
                var window = new IndicatorDrawdownWindow(repository);
                rowCount = ((DataGrid)window.FindName("DrawdownGrid")).Items.Count;
                window.Show();
                handle = new WindowInteropHelper(window).Handle;
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
            Assert.Equal(2, rowCount);
            Assert.NotEqual(IntPtr.Zero, handle);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ExtractAfter(string source, string marker)
    {
        int index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        return source[index..];
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
