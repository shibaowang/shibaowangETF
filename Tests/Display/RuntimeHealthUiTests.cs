namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class RuntimeHealthUiTests
{
    [Fact]
    public void SystemMaintenance_ContainsRuntimeStabilityPanelAndRequiredMetrics()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateRuntimeHealthPanel()", "private void RuntimeHealthMonitor_SnapshotAvailable");

        Assert.Contains("运行健康", panel, StringComparison.Ordinal);
        Assert.Contains("当前状态", panel, StringComparison.Ordinal);
        Assert.Contains("当前工作集", panel, StringComparison.Ordinal);
        Assert.Contains("当前私有内存", panel, StringComparison.Ordinal);
        Assert.Contains("30 分钟内存变化", panel, StringComparison.Ordinal);
        Assert.Contains("最近 Dispatcher 延迟", panel, StringComparison.Ordinal);
        Assert.Contains("当前走势图窗口数量", panel, StringComparison.Ordinal);
        Assert.Contains("健康日志目录", panel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("刷新状态")]
    [InlineData("打开健康日志目录")]
    [InlineData("导出最近24小时报告")]
    public void RuntimeStabilityPanel_ContainsExactlyRequiredActions(string action)
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateRuntimeHealthPanel()", "private void RuntimeHealthMonitor_SnapshotAvailable");

        Assert.Contains(action, panel, StringComparison.Ordinal);
        Assert.DoesNotContain("自动重启", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("强制GC", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("释放内存", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("清理数据库", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshStatus_ReadsCurrentSnapshotWithoutBusinessRefresh()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string refresh = Slice(code, "private void RefreshRuntimeHealthPanel()", "private void UpdateRuntimeHealthPanel");

        Assert.Contains("_runtimeHealthMonitor?.CurrentSnapshot", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLocalDataAndUi", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketData", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenHealthDirectory_UsesShellExecute()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string operation = Slice(code, "private void OpenRuntimeHealthDirectory()", "private async Task ExportRuntimeHealthReportAsync()");

        Assert.Contains("FileName = _runtimeHealthMonitor.HealthDirectory", operation, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenFileDialog", operation, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportAction_UsesMonitorReportExporterOnly()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string operation = Slice(code, "private async Task ExportRuntimeHealthReportAsync()", "private static string FormatRuntimeDuration");

        Assert.Contains("ExportLast24HoursAsync", operation, StringComparison.Ordinal);
        Assert.Contains("Task.Run", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketData", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", operation, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UnsubscribesRuntimeHealthEventOnClose()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("_runtimeHealthMonitor.SnapshotAvailable += RuntimeHealthMonitor_SnapshotAvailable", code, StringComparison.Ordinal);
        Assert.Contains("Closed += ManualDataEntryWindow_RuntimeHealthClosed", code, StringComparison.Ordinal);
        Assert.Contains("_runtimeHealthMonitor.SnapshotAvailable -= RuntimeHealthMonitor_SnapshotAvailable", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotUiUpdate_IsDispatchedToUiThread()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string handler = Slice(code, "private void RuntimeHealthMonitor_SnapshotAvailable", "private void ManualDataEntryWindow_RuntimeHealthClosed");

        Assert.Contains("Dispatcher.BeginInvoke", handler, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.HasShutdownStarted", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.Invoke(", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthStatus_UsesNormalWarningCriticalColors()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string update = Slice(code, "private void UpdateRuntimeHealthPanel", "private void SetRuntimeHealthValue");

        Assert.Contains("RuntimeHealthStatus.Normal", update, StringComparison.Ordinal);
        Assert.Contains("#84CC16", update, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthStatus.Warning", update, StringComparison.Ordinal);
        Assert.Contains("#F59E0B", update, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthStatus.Critical", update, StringComparison.Ordinal);
        Assert.Contains("#EF4444", update, StringComparison.Ordinal);
        Assert.Contains("监测异常（业务继续运行）", update, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthPanel_DoesNotDisplaySensitiveBusinessData()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateRuntimeHealthPanel()", "private static string FormatSignedRuntimeBytes");

        Assert.DoesNotContain("PushPlus", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TradeLogRecord", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountBalance", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PositionState", panel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_CreatesStartsAndStopsMonitorAtRequiredLifecycleBoundaries()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("RuntimeHealthMonitor.CreateDefault(ResolveDisplayVersion(), Dispatcher)", code, StringComparison.Ordinal);
        Assert.Contains("Loaded +=", code, StringComparison.Ordinal);
        Assert.Contains("_runtimeHealthMonitor.Start();", code, StringComparison.Ordinal);
        Assert.Contains("Closed +=", code, StringComparison.Ordinal);
        Assert.Contains("_runtimeHealthMonitor.StopAsync(TimeSpan.FromSeconds(3))", code, StringComparison.Ordinal);
        Assert.Contains("_runtimeHealthMonitor.Dispose();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainRefresh_IsObservedWithFinallyWithoutChangingSchedule()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string refresh = Slice(code, "private void RefreshLocalDataAndUi(MainWindowDirtyFlags dirtyFlags = MainWindowDirtyFlags.All)", "private void UpdateClock()");

        Assert.Contains("NotifyUiRefreshStarted", refresh, StringComparison.Ordinal);
        Assert.Contains("finally", refresh, StringComparison.Ordinal);
        Assert.Contains("NotifyUiRefreshCompleted", refresh, StringComparison.Ordinal);
        Assert.Contains("_random.Next(2000, 4001)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeMonitor_UsesIndependentThirtyAndFiveSecondIntervals()
    {
        string code = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs");

        Assert.Contains("TimeSpan.FromSeconds(30)", code, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(5)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthChanges_DoNotTouchManualWindowChromeOrTitleBar()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string healthSection = Slice(code, "private UIElement CreateRuntimeHealthPanel()", "private UIElement CreateDatabaseBackupPanel()");

        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", healthSection, StringComparison.Ordinal);
        Assert.DoesNotContain("TryApplyDarkTitleBar", healthSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthInfrastructure_DoesNotReferenceBusinessOrNetworkServices()
    {
        string monitor = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthMonitor.cs");
        string store = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthFileStore.cs");
        string exporter = ReadRepositoryFile("Infrastructure", "Diagnostics", "RuntimeHealthReportExporter.cs");
        string combined = monitor + store + exporter;

        Assert.DoesNotContain("MarketDataClient", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalMarketRequestScheduler", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountReplayService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLogRecord", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderDraft", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyDecision", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", combined, StringComparison.Ordinal);
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, "Start marker not found: " + startMarker);
        Assert.True(end > start, "End marker not found: " + endMarker);
        return text[start..end];
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
