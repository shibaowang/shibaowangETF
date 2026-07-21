using CrossETF.Terminal.UiShell.Reference;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class MainWindowRefreshStabilityTests
{
    [Fact]
    public void Coordinator_OneHundredUnchangedCyclesRenderSurfaceOnce()
    {
        var coordinator = new MainWindowUiRefreshCoordinator();

        for (int cycle = 0; cycle < 100; cycle++)
        {
            Assert.True(coordinator.Request(MainWindowDirtyFlags.All));
            Assert.Equal(MainWindowDirtyFlags.All, coordinator.TakePending());
            Assert.Equal(cycle == 0, coordinator.ShouldRender("etf-table", "stable"));
        }

        Assert.Equal(100, coordinator.RefreshCycleCount);
        Assert.Equal(100, coordinator.SurfaceUpdateAttemptCount);
        Assert.Equal(1, coordinator.SurfaceRenderCount);
        Assert.Equal(99, coordinator.SkippedUnchangedSurfaceCount);
    }

    [Fact]
    public void Coordinator_CoalescesDirtyFlagsIntoOneDispatcherCycle()
    {
        var coordinator = new MainWindowUiRefreshCoordinator();

        Assert.True(coordinator.Request(MainWindowDirtyFlags.Account));
        Assert.False(coordinator.Request(MainWindowDirtyFlags.EtfTable));
        Assert.False(coordinator.Request(MainWindowDirtyFlags.OrderDraft));

        Assert.Equal(
            MainWindowDirtyFlags.Account | MainWindowDirtyFlags.EtfTable | MainWindowDirtyFlags.OrderDraft,
            coordinator.TakePending());
        Assert.Equal(1, coordinator.RefreshCycleCount);
    }

    [Fact]
    public void Coordinator_RendersOnlyWhenSurfaceSignatureChanges()
    {
        var coordinator = new MainWindowUiRefreshCoordinator();

        Assert.True(coordinator.ShouldRender("trade-log", "one"));
        Assert.False(coordinator.ShouldRender("trade-log", "one"));
        Assert.True(coordinator.ShouldRender("trade-log", "two"));
        coordinator.Invalidate("trade-log");
        Assert.True(coordinator.ShouldRender("trade-log", "two"));
    }

    [Fact]
    public void EtfCellIndex_UsesStrategyCodeAndColumnKey()
    {
        Assert.Equal("159941|daily_pnl", MainWindow.BuildEtfCellKey("159941", "daily_pnl"));
        Assert.Equal("159941|price", MainWindow.BuildEtfCellKey("sz159941", "price"));
    }

    [Fact]
    public void MainWindow_EtfOrdinaryRefreshUsesStableCellReferences()
    {
        string source = ReadRepositoryFile("MainWindow.xaml.cs");
        string updateMethod = ExtractMethod(source, "private void UpdateEtfTableCells(", "private static void UpdateEtfCellToolTip(");

        Assert.Contains("_etfCellIndex", source, StringComparison.Ordinal);
        Assert.Contains("BuildEtfCellKey(strategyCode, orderedColumns[c].Key)", source, StringComparison.Ordinal);
        Assert.Contains("cell.TextBlock.Text = value", updateMethod, StringComparison.Ordinal);
        Assert.Contains("if (textChanged)", updateMethod, StringComparison.Ordinal);
        Assert.Contains("cell.TextBlock.Foreground", updateMethod, StringComparison.Ordinal);
        Assert.Contains("cell.Border.Tag", updateMethod, StringComparison.Ordinal);
        Assert.Contains("UpdateEtfCellToolTip", updateMethod, StringComparison.Ordinal);
        Assert.Contains("ApplyEtfValueChangeHighlight", updateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Clear", updateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("new TextBlock", updateMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_EtfStructuralSignatureExcludesOrdinaryCellValues()
    {
        string source = ReadRepositoryFile("MainWindow.xaml.cs");
        string tableMethod = ExtractMethod(source, "private void BuildEtfTable()", "internal static string BuildEtfCellKey");
        string signatureBlock = ExtractMethod(tableMethod, "string structureSignature =", "int expectedCellCount");

        Assert.Contains("orderedColumns.Select(column => column.Key)", signatureBlock, StringComparison.Ordinal);
        Assert.Contains("displayRows.Select(row => row.Length > 0 ? NormalizePinnedEtfSymbol(row[0])", signatureBlock, StringComparison.Ordinal);
        Assert.Contains("_pinnedEtfSymbols", signatureBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("row[sourceColumn]", signatureBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Price", signatureBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("dailyPnl", signatureBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UnchangedGridsAndChartsAreSignatureGated()
    {
        string source = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("ShouldRender(\"trade-log\", signature)", source, StringComparison.Ordinal);
        Assert.Contains("ShouldRender(\"order-draft\", signature)", source, StringComparison.Ordinal);
        Assert.Contains("ShouldRender(\"sparklines\", signature)", source, StringComparison.Ordinal);
        Assert.Contains("ShouldRender(\"drawdown\", signature)", source, StringComparison.Ordinal);
        Assert.Contains("ShouldRender(\"ring\", signature)", source, StringComparison.Ordinal);
        Assert.Contains("if (_poolInitialized)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_BackgroundCompletionsQueueDirtyFlagsInsteadOfFullRefresh()
    {
        string source = ReadRepositoryFile("MainWindow.xaml.cs");
        string account = ExtractMethod(source, "private void QueueAccountReplayIfNeeded()", "private string BuildAccountReplaySignature()");
        string strategy = ExtractMethod(source, "private void QueueStrategyDecisionIfNeeded()", "private void QueueOrderDraftIfNeeded()");
        string order = ExtractMethod(source, "private void QueueOrderDraftIfNeeded()", "private void QueueAlertDeliveryIfNeeded()");

        Assert.Contains("QueueUiRefresh", account, StringComparison.Ordinal);
        Assert.Contains("QueueUiRefresh", strategy, StringComparison.Ordinal);
        Assert.Contains("QueueUiRefresh", order, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLocalDataAndUi();", account, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLocalDataAndUi();", strategy, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLocalDataAndUi();", order, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_RetainsRandomTwoToFourSecondRefreshInterval()
    {
        string source = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("_random.Next(2000, 4001)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", source, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", relativePath);
    }
}
