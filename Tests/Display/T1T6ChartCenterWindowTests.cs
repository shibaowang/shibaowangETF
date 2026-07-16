using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Interop;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class T1T6ChartCenterWindowTests
{
    [Fact]
    public void Window_UsesLockedNativeFrameDimensionsAndOpaqueDeepBackground()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");

        Assert.Contains("Title=\"T1-T6看图中心\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1500\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"860\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"680\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Microsoft YaHei UI\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SnapsToDevicePixels=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesNativeTitleBarWithoutTransparencyWindowChromeOpacityRevealOrShield()
    {
        string combined = ReadWindowFiles();
        string[] forbidden =
        {
            "WindowStyle=\"None\"", "AllowsTransparency=\"True\"", "WindowChrome", "Opacity=\"0\"",
            "Reveal", "Fade", "Storyboard", "DoubleAnimation", "DarkStartupShield", "Thread.Sleep",
            "Task.Delay", "ApplySmoothOpen", "Content = null", "BeginAnimation"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, combined, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Window_AppliesDwmThenCompositionBackgroundInSourceInitialized()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");
        string handler = Extract(source, "private void T1T6ChartCenterWindow_SourceInitialized", "private void T1T6ChartCenterWindow_Loaded");

        Assert.True(handler.IndexOf("TryApplyDarkTitleBar();", StringComparison.Ordinal)
                    < handler.IndexOf("ApplyDarkHwndBackground();", StringComparison.Ordinal));
        Assert.Contains("CompositionTarget.BackgroundColor = WindowBackgroundColor", source, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0x05, 0x0B, 0x14)", source, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_BindsStrategyItemsAndInitialSnapshotInConstructorBeforeAnyShow()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");
        string constructor = Extract(source, "public T1T6ChartCenterWindow(", "private void T1T6ChartCenterWindow_SourceInitialized");

        Assert.Contains("StrategyList.ItemsSource = _strategyRows;", constructor, StringComparison.Ordinal);
        Assert.Contains("ApplySnapshot(initialSnapshot, preserveScroll: false);", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("Show()", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("Activate()", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus()", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesTwoSecondLocalTimerStartedOnlyAfterLoadedAndStoppedOnClose()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");
        string constructor = Extract(source, "public T1T6ChartCenterWindow(", "private void T1T6ChartCenterWindow_SourceInitialized");
        string loaded = Extract(source, "private void T1T6ChartCenterWindow_Loaded", "private void T1T6ChartCenterWindow_Closed");
        string closed = Extract(source, "private void T1T6ChartCenterWindow_Closed", "private void LocalRefreshTimer_Tick");

        Assert.Contains("TimeSpan.FromSeconds(2)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_localRefreshTimer.Start()", constructor, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Start()", loaded, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Stop()", closed, StringComparison.Ordinal);
        Assert.Contains("_localRefreshTimer.Tick -= LocalRefreshTimer_Tick", closed, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_LocalReadFailureKeepsLastSnapshotWithoutMessageBoxOrRuntimeLog()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");
        string tick = Extract(source, "private void LocalRefreshTimer_Tick", "private void StrategyList_SelectionChanged");

        Assert.Contains("LocalReadStatusText.Text = \"本地读取失败\"", tick, StringComparison.Ordinal);
        Assert.Contains("LocalReadStatusText.ToolTip = readModel.ReadError", tick, StringComparison.Ordinal);
        Assert.Contains("return;", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySnapshot", Extract(tick, "if (!string.IsNullOrWhiteSpace(readModel.ReadError))", "string? selectedStrategyCode"), StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", tick, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime_log", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WriteRuntimeLog", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_PreservesSelectionAndScrollWithoutRebuildingWindow()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");

        Assert.Contains("selectedStrategyCode", source, StringComparison.Ordinal);
        Assert.Contains("fallbackSelectionIndex", source, StringComparison.Ordinal);
        Assert.Contains("strategyScrollViewer?.VerticalOffset", source, StringComparison.Ordinal);
        Assert.Contains("ScrollToVerticalOffset(verticalOffset)", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeRows(snapshot.Rows)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyList.ItemsSource = snapshot.Rows", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasFourSummaryCardsStrategyListCurrentStateAndThreeByTwoTierLayout()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");

        Assert.Contains("Text=\"启用策略\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"行情正常\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"无决策\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"同标的多策略\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StrategyList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Single\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"当前策略状态\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedRow.Tiers}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"3\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"132\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("策略代码")]
    [InlineData("策略名称")]
    [InlineData("ETF名称")]
    [InlineData("ETF代码")]
    [InlineData("跟踪指数")]
    [InlineData("最新价格")]
    [InlineData("行情来源")]
    [InlineData("行情时间")]
    [InlineData("当前指数回撤")]
    [InlineData("当前溢价")]
    [InlineData("当前动作（派生建议）")]
    [InlineData("当前档位（派生建议）")]
    [InlineData("决策计算时间")]
    public void Window_CurrentStateContainsOnlyRequiredReadableFields(string label)
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");

        Assert.Contains($"Text=\"{label}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DoesNotExposeAccountHoldingDraftTargetOrExecutionFields()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");
        string[] forbiddenLabels =
        {
            "可用现金", "狙击资金池", "底仓完成度", "持仓数量", "持仓成本", "持仓盈亏",
            "目标价格", "目标金额", "目标数量", "成交数量", "成交状态", "委托草案", "订单腿"
        };

        Assert.All(forbiddenLabels, value => Assert.DoesNotContain($"Text=\"{value}", xaml, StringComparison.Ordinal));
    }

    [Fact]
    public void Window_UsesDifferentVisualSemanticsForConditionMetAndCurrentSuggestion()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");
        string tiers = Extract(xaml, "<ItemsControl Grid.Row=\"4\"", "</ItemsControl>");

        Assert.Contains("Binding=\"{Binding IsConditionMet}\"", tiers, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsCurrentSuggestedTier}\"", tiers, StringComparison.Ordinal);
        Assert.Contains("ChartCenterAccentBrush", tiers, StringComparison.Ordinal);
        Assert.Contains("ChartCenterSuggestedBrush", tiers, StringComparison.Ordinal);
        Assert.Contains("Text=\"当前建议\"", tiers, StringComparison.Ordinal);
        Assert.DoesNotContain("已成交", tiers, StringComparison.Ordinal);
        Assert.DoesNotContain("已触发买入", tiers, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HasExactlyOneChartButtonAndNoEmbeddedOrPeriodSpecificCharts()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");

        Assert.Single(Regex.Matches(xaml, "<Button\\b").Cast<Match>());
        Assert.Contains("Content=\"打开图表\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("打开日线", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("打开周线", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("打开月线", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Canvas", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Polyline", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<PathGeometry", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DisplaysLockedDisclaimerAndHasNoManualRefreshSearchFilterOrEdit()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");

        Assert.Contains("当前动作、当前档位和回撤状态来自最近一次持久化策略决策，仅用于观察和看图，不代表已成交，不会生成或执行委托，也不会写入TradeLog。", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("手动刷新", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"刷新", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchTextBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextMenu", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DarkScrollBarsAreWindowScopedAndNoGlobalStyleWasAdded()
    {
        string xaml = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml");
        string app = ReadRepositoryFile("App.xaml");

        Assert.Contains("x:Key=\"T1T6ScrollBarStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"T1T6VerticalScrollBarTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#071724\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("T1T6ScrollBarStyle", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DoesNotCallBusinessReplayDecisionDraftMarketOrPersistenceWrites()
    {
        string source = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");
        string[] forbidden =
        {
            "AccountReplayService", "StrategyDecisionService", "OrderDraftService", "MarketDataRefreshService",
            "MarketDataClient", "GlobalMarketRequestScheduler", "SaveTrade", "SaveOrder", "SaveMarket",
            "Delete", "ExecuteNonQuery", "WriteRuntimeLog"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.Ordinal));
    }

    [Fact]
    public void Window_ConstructsAndBindsInitialRowsOnIsolatedSta()
    {
        string root = Path.Combine(Path.GetTempPath(), "cross-etf-t1t6-window-tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "window.db");
        Exception? failure = null;
        int rowCount = -1;
        IntPtr handle = IntPtr.Zero;
        var thread = new Thread(() =>
        {
            try
            {
                var repository = new LocalDataRepository(new LocalDatabase(databasePath));
                var strategy = new StrategyConfigRecord
                {
                    Id = 1,
                    Code = "159941",
                    Name = "策略甲",
                    Enabled = true,
                    CreatedAt = "2026-07-16 09:00:00",
                    UpdatedAt = "2026-07-16 09:00:00"
                };
                var readModel = new T1T6ChartCenterReadModel
                {
                    EnabledStrategies = new[] { strategy },
                    ReadAt = DateTimeOffset.Now
                };
                var snapshot = new T1T6ChartCenterSnapshotBuilder().Build(readModel, DateTimeOffset.Now);
                var window = new T1T6ChartCenterWindow(repository, _ => { }, readModel, snapshot);
                rowCount = ((ListBox)window.FindName("StrategyList")).Items.Count;
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
            Assert.Equal(1, rowCount);
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

    private static string ReadWindowFiles()
        => ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml")
           + ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");

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
