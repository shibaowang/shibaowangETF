namespace CrossETF.Terminal.UiShell.Reference.Tests.Regression;

public sealed class RuntimeHealthBoundaryTests
{
    [Fact]
    public void RuntimeHealthSources_DoNotCreateOrWriteSqliteTables()
    {
        string sources = ReadRuntimeHealthSources();

        Assert.DoesNotContain("Microsoft.Data.Sqlite", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sources, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Infrastructure/Persistence/DatabaseBackupService.cs")]
    [InlineData("Infrastructure/Persistence/DatabaseRestoreBootstrap.cs")]
    [InlineData("Infrastructure/Persistence/DatabaseStartupCoordinator.cs")]
    [InlineData("Views/ChartWindowManager.cs")]
    [InlineData("Infrastructure/Market/MarketDataClient.cs")]
    [InlineData("Core/Services/AccountReplayService.cs")]
    public void LockedServices_DoNotDependOnRuntimeHealth(string relativePath)
    {
        string source = ReadRepositoryFile(relativePath.Split('/'));

        Assert.DoesNotContain("RuntimeHealth", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthSources_DoNotReferenceTradingStrategyOrNetworkTypes()
    {
        string sources = ReadRuntimeHealthSources();

        Assert.DoesNotContain("TradeLog", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OrderDraft", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StrategyDecision", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MarketDataClient", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalMarketRequestScheduler", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void MainRefreshRandomSchedule_RemainsTwoToFourSeconds()
    {
        string mainWindow = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("_random.Next(2000, 4001)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("_refreshTimer.Interval = RuntimeHealth", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthWindowCounting_DoesNotUseChartWindowManagerOrStrongWindowCollection()
    {
        string monitor = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs");

        Assert.Contains("Application.Current", monitor, StringComparison.Ordinal);
        Assert.Contains("application?.Windows.Cast<Window>()", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("ChartWindowManager", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("List<Window>", monitor, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthLifecycle_UsesNoStaticEventOrForegroundThread()
    {
        string monitor = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs");

        Assert.DoesNotContain("static event", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thread", monitor, StringComparison.Ordinal);
        Assert.Contains("Task.Run", monitor, StringComparison.Ordinal);
        Assert.Contains("CancellationToken", monitor, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishScript_RemainsIndependentOfRuntimeHealthTask()
    {
        string publishScript = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.DoesNotContain("RuntimeHealth", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("v8.4.0", publishScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssemblyName_RemainsSdkDefault()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<Version>8.10.5</Version>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRestoreOrder_RemainsBeforeWpfStartup()
    {
        string app = ReadRepositoryFile("App.xaml.cs");

        Assert.True(app.IndexOf("RunPreInitialize()", StringComparison.Ordinal) < app.IndexOf("base.OnStartup(e)", StringComparison.Ordinal));
        Assert.DoesNotContain("RuntimeHealth", app, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthTests_UseTemporaryDirectoriesAndNoRealMarketAccess()
    {
        string testDirectory = FindRepositoryDirectory("Tests", "Runtime");
        string tests = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(testDirectory, "*.cs").Select(File.ReadAllText));

        Assert.Contains("Path.GetTempPath()", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("SpecialFolder.LocalApplicationData", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("cross_etf_terminal.db", tests, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_DoesNotAddManualRefreshButton()
    {
        string xaml = ReadRepositoryFile("MainWindow.xaml");

        Assert.DoesNotContain("手动刷新", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("刷新行情", xaml, StringComparison.Ordinal);
    }

    private static string ReadRuntimeHealthSources()
        => string.Join(
            Environment.NewLine,
            ReadRepositoryFile("Core", "Models", "RuntimeHealthModels.cs"),
            ReadRepositoryFile("Core", "Services", "RuntimeHealthEvaluator.cs"),
            ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs"),
            ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthFileStore.cs"),
            ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthReportExporter.cs"));

    private static string FindRepositoryDirectory(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(Path.Combine(segments));
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
