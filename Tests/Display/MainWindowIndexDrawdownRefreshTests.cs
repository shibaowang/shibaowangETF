namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class MainWindowIndexDrawdownRefreshTests
{
    [Fact]
    public void QueueMarketRefresh_RefreshesIndexDrawdownUiAfterQuoteRefresh()
    {
        string source = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml.cs"));

        Assert.Contains("RefreshIndexQuoteDependentUiIfChanged", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(new Action(RefreshIndexQuoteDependentUiIfChanged)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshIndexQuoteDependentUi_ReadsQuoteCacheAndRedrawsDrawdownChartsOnly()
    {
        string source = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml.cs"));
        int methodStart = source.IndexOf("private void RefreshIndexQuoteDependentUiIfChanged()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf("private void QueueChartRefresh()", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];

        Assert.Contains("ReadMarketQuoteCache()", method, StringComparison.Ordinal);
        Assert.Contains("IndexDrawdownQuoteRefreshHelper.HasQuoteChanged", method, StringComparison.Ordinal);
        Assert.Contains("UpdateTopMarketQuotes()", method, StringComparison.Ordinal);
        Assert.Contains("DrawDrawdownCharts()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueMarketRefresh", method, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueStrategyDecisionIfNeeded", method, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueOrderDraftIfNeeded", method, StringComparison.Ordinal);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Workspace file not found: " + Path.Combine(parts));
    }
}
