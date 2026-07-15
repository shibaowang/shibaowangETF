using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class DatabaseMaintenanceUiTests
{
    [Fact]
    public void SystemMaintenance_ContainsFourControlledBackupActions()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateDatabaseBackupPanel()", "private async Task CreateManualDatabaseBackupAsync()");

        Assert.Contains("备份记录", panel, StringComparison.Ordinal);
        Assert.Contains("恢复数据库", panel, StringComparison.Ordinal);
        Assert.Contains("立即备份", panel, StringComparison.Ordinal);
        Assert.Contains("刷新列表", panel, StringComparison.Ordinal);
        Assert.Contains("打开备份目录", panel, StringComparison.Ordinal);
        Assert.Contains("恢复选中备份", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemMaintenance_DisplaysOnlySavedDataNoticeAndControlledPaths()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateDatabaseBackupPanel()", "private async Task CreateManualDatabaseBackupAsync()");

        Assert.Contains("备份仅包含已经保存到数据库的数据", panel, StringComparison.Ordinal);
        Assert.Contains("DatabasePath", panel, StringComparison.Ordinal);
        Assert.Contains("BackupDirectory", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenFileDialog", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("PushPlus", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLogRecord", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupList_UsesRequiredColumnsAndDefaultsToNewestFirstInService()
    {
        string window = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string service = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseBackupService.cs");

        Assert.Contains("备份时间", window, StringComparison.Ordinal);
        Assert.Contains("类型", window, StringComparison.Ordinal);
        Assert.Contains("版本", window, StringComparison.Ordinal);
        Assert.Contains("文件大小", window, StringComparison.Ordinal);
        Assert.Contains("状态", window, StringComparison.Ordinal);
        Assert.Contains("文件名", window, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(item => item.CreatedAt)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreButton_IsDisabledWithoutValidSelectionOrWhileBusy()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("SelectedItem is DatabaseBackupValidationResult { CanRestore: true }", code, StringComparison.Ordinal);
        Assert.Contains("enabled = !_databaseBackupOperationInProgress", code, StringComparison.Ordinal);
        Assert.Contains("_restoreDatabaseBackupButton.IsEnabled = enabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreFlow_RequiresTwoExplicitConfirmationsBeforeStaging()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string restore = Slice(code, "private async Task ConfirmAndStageDatabaseRestoreAsync()", "private void SetDatabaseBackupBusy");

        Assert.Contains("确认数据库恢复", restore, StringComparison.Ordinal);
        Assert.Contains("确认恢复此备份并关闭程序", restore, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(restore, "MessageBoxButton.YesNo"));
        Assert.True(restore.IndexOf("StageRestoreAsync", StringComparison.Ordinal) > restore.LastIndexOf("MessageBoxButton.YesNo", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreFlow_CancelPathDoesNotWriteMarkerOrCloseApplication()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string restore = Slice(code, "private async Task ConfirmAndStageDatabaseRestoreAsync()", "private void SetDatabaseBackupBusy");

        Assert.True(CountOccurrences(restore, "return;") >= 2);
        Assert.True(restore.IndexOf("StageRestoreAsync", StringComparison.Ordinal) < restore.IndexOf("Application.Current.Shutdown", StringComparison.Ordinal));
        Assert.DoesNotContain("CreateBackupAsync(DatabaseBackupKind.PreRestore)", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualBackup_DoesNotSaveUnsavedEditorsOrTriggerBusinessRefresh()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string operation = Slice(code, "private async Task CreateManualDatabaseBackupAsync()", "private async Task RefreshDatabaseBackupListAsync()");

        Assert.Contains("CreateBackupAsync(DatabaseBackupKind.Manual)", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLogs", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveStrategies", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountReplay", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyDecision", operation, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupPanel_DoesNotExposeExternalFilePickerOrNetworkAction()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string backupSection = Slice(code, "private UIElement CreateDatabaseBackupPanel()", "public void RefreshAlertSettingsUi()");

        Assert.DoesNotContain("OpenFileDialog", backupSection, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", backupSection, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketData", backupSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLocalDataAndUi", backupSection, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupUri_RemainsMainWindowAndStartupProtectionRunsBeforeBase()
    {
        string xaml = ReadRepositoryFile("App.xaml");
        string app = ReadRepositoryFile("App.xaml.cs");

        Assert.Contains("StartupUri=\"MainWindow.xaml\"", xaml, StringComparison.Ordinal);
        Assert.True(app.IndexOf("RunPreInitialize()", StringComparison.Ordinal) < app.IndexOf("base.OnStartup(e)", StringComparison.Ordinal));
        Assert.True(app.IndexOf("DispatcherUnhandledException +=", StringComparison.Ordinal) < app.IndexOf("RunPreInitialize()", StringComparison.Ordinal));
    }

    [Fact]
    public void BackupRows_OnlyAllowValidControlledFilesToRestore()
    {
        var invalid = new DatabaseBackupValidationResult(
            false,
            "error",
            "x",
            "x.db",
            1,
            DateTimeOffset.Now,
            "--",
            null,
            "invalid",
            false);
        var validButUncontrolled = invalid with { IsValid = true };
        var valid = validButUncontrolled with { IsControlledName = true };

        Assert.False(invalid.CanRestore);
        Assert.False(validButUncontrolled.CanRestore);
        Assert.True(valid.CanRestore);
    }

    [Fact]
    public void DatabaseBackupUi_DoesNotChangeManualWindowTitleBarOrChrome()
    {
        string xaml = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml");
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");

        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TryApplyDarkTitleBar", code, StringComparison.Ordinal);
        Assert.Contains("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenBackupDirectory_UsesShellExecuteOnFixedServiceDirectory()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string operation = Slice(code, "private void OpenDatabaseBackupDirectory()", "private async Task ConfirmAndStageDatabaseRestoreAsync()");

        Assert.Contains("FileName = _databaseBackupService.BackupDirectory", operation, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenFileDialog", operation, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupPanel_DoesNotDisplayTokensOrTradeLogContents()
    {
        string code = ReadRepositoryFile("Views", "ManualDataEntryWindow.xaml.cs");
        string panel = Slice(code, "private UIElement CreateDatabaseBackupPanel()", "private async Task CreateManualDatabaseBackupAsync()");

        Assert.DoesNotContain("Token", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TradeLog", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Memo", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionContract_IsV870WithoutChangingAssemblyName()
    {
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.Contains("<Version>8.7.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>8.7.0.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>8.7.0.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.7.0</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", project, StringComparison.Ordinal);
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
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
