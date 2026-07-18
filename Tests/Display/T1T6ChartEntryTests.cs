using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class T1T6ChartEntryTests
{
    [Fact]
    public void ExistingNavigationEntryIsConnectedWithoutManualEntryScopeOrSecondEntry()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string mainXaml = ReadRepositoryFile("MainWindow.xaml");
        string buildNavigation = Extract(main, "private void BuildNavigation()", "private void OpenIndicatorDrawdown()");

        Assert.True(MainWindow.IsT1T6ChartCenterNavigation("T1-T6看图"));
        Assert.True(MainWindow.IsActionableNavigation("T1-T6看图"));
        Assert.Null(MainWindow.ResolveManualEntryScopeForNavigation("T1-T6看图"));
        Assert.Equal(1, Count(buildNavigation, "\"T1-T6看图\""));
        Assert.Contains("\"◱\"", buildNavigation, StringComparison.Ordinal);
        Assert.DoesNotContain("T1T6ChartCenterWindow", mainXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationOrderAndNameRemainLocked()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string navigation = Extract(main, "private void BuildNavigation()", "private void OpenIndicatorDrawdown()");
        string[] expected =
        {
            "作战总览", "行情监控", "溢价决策", "指标回撤", "资金仓位",
            "T1-T6看图", "交易日志", "风险中心", "系统设置"
        };

        int previous = -1;
        foreach (string name in expected)
        {
            int current = navigation.IndexOf($"\"{name}\"", StringComparison.Ordinal);
            Assert.True(current > previous, $"Navigation entry {name} is missing or out of order.");
            previous = current;
        }
    }

    [Fact]
    public void NavigationClickRoutesT1T6BeforeManualEntryFallback()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string handler = Extract(main, "private void NavigationButton_Click", "private void SelectNavigation");

        int route = handler.IndexOf("IsT1T6ChartCenterNavigation", StringComparison.Ordinal);
        int manual = handler.IndexOf("ResolveManualEntryScopeForNavigation", StringComparison.Ordinal);
        Assert.True(route >= 0 && manual > route);
        Assert.Contains("OpenT1T6ChartCenter();", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstOpenReadsBuildsCreatesBindsOwnerClosedThenOnlyShows()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(main, "private void OpenT1T6ChartCenter()", "private void OpenT1T6SecurityChart");
        string firstOpen = method[method.IndexOf("var snapshotBuilder", StringComparison.Ordinal)..];

        int read = firstOpen.IndexOf("ReadT1T6ChartCenterReadModel()", StringComparison.Ordinal);
        int build = firstOpen.IndexOf("snapshotBuilder.Build", StringComparison.Ordinal);
        int create = firstOpen.IndexOf("new T1T6ChartCenterWindow", StringComparison.Ordinal);
        int owner = firstOpen.IndexOf("Owner = this", StringComparison.Ordinal);
        int closed = firstOpen.IndexOf(".Closed +=", StringComparison.Ordinal);
        int show = firstOpen.IndexOf(".Show();", StringComparison.Ordinal);

        Assert.True(read >= 0 && read < build && build < create && create < owner && owner < closed && closed < show);
        Assert.DoesNotContain("ShowDialog", firstOpen, StringComparison.Ordinal);
        Assert.DoesNotContain("Activate()", firstOpen, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus()", firstOpen, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowState =", firstOpen, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatOpenRestoresMinimizedWindowActivatesFocusesAndDoesNotCreateSecondInstance()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(main, "private void OpenT1T6ChartCenter()", "private void OpenT1T6SecurityChart");
        string repeatBranch = Extract(method, "if (_t1T6ChartCenterWindow is { IsVisible: true })", "var snapshotBuilder");

        Assert.Contains("WindowState == WindowState.Minimized", repeatBranch, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Normal", repeatBranch, StringComparison.Ordinal);
        Assert.Contains("Activate();", repeatBranch, StringComparison.Ordinal);
        Assert.Contains("Focus();", repeatBranch, StringComparison.Ordinal);
        Assert.Contains("return;", repeatBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("new T1T6ChartCenterWindow", repeatBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedHandlerClearsOnlyCenterReference()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string method = Extract(main, "private void OpenT1T6ChartCenter()", "private void OpenT1T6SecurityChart");

        Assert.Contains("_t1T6ChartCenterWindow.Closed += (_, _) => _t1T6ChartCenterWindow = null", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_chartWindowManager.Close", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ChartCallbackReusesExistingManagerAndNormalizedSecurityKey()
    {
        string main = ReadRepositoryFile("MainWindow.xaml.cs");
        string callback = Extract(main, "private void OpenT1T6SecurityChart", "private void BuildNavigation()");

        Assert.Contains("MarketSymbolNormalizer.DigitsOnly(request.Symbol)", callback, StringComparison.Ordinal);
        Assert.Contains("ChartDataService.CreateSecurityInfo", callback, StringComparison.Ordinal);
        Assert.Contains("normalizedSymbol,\n            displayName,\n            normalizedSymbol", callback.Replace("\r", string.Empty), StringComparison.Ordinal);
        Assert.Contains("_chartWindowManager.OpenOrActivate(security)", callback, StringComparison.Ordinal);
        Assert.Contains("QueueChartRefresh();", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("new SecurityChartWindow", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyCode", callback, StringComparison.Ordinal);
    }

    [Fact]
    public void EntryDoesNotModifyChartManagerSecurityWindowOrMainXamlContracts()
    {
        string center = ReadRepositoryFile("Views", "T1T6ChartCenterWindow.xaml.cs");

        Assert.DoesNotContain("new ChartWindowManager", center, StringComparison.Ordinal);
        Assert.DoesNotContain("new SecurityChartWindow", center, StringComparison.Ordinal);
        Assert.DoesNotContain("ChartDataService", center, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityChartWindow", ReadRepositoryFile("MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContractIsV8100AndAssemblyNameRemainsSdkDefault()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains("<Version>8.10.5</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>8.10.5.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>8.10.5.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.10.5</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
        Assert.Equal("V8.10.5", MainWindow.ResolveDisplayVersion());
    }

    private static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

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
