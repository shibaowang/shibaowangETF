using CrossETF.Terminal.UiShell.Reference;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Views;
using System.Windows.Input;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class ManualDataEntryStyleTests
{
    [Fact]
    public void ManualDataEntry_DefinesReadableDataGridEditingTextBoxStyle()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));

        Assert.Contains("EntryDataGridEditingTextBoxStyle", xaml);
        Assert.Contains("SelectionTextBrush", xaml);
        Assert.Contains("CaretBrush", xaml);
        Assert.Contains("EntryComboSelectedBrush", xaml);
    }

    [Fact]
    public void ManualDataEntry_TextColumnsUseEditingElementStyle()
    {
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("ElementStyle = elementStyle", code);
        Assert.Contains("EditingElementStyle = editingStyle", code);
        Assert.True(CountOccurrences(code, "EditingElementStyle = editingStyle") >= 2);
        Assert.Contains("EditingElementStyle = (Style)FindResource(\"TradeLogEditingTextBoxStyle\")", code);
    }

    [Fact]
    public void ManualDataEntry_ComboColumnsKeepEditingElementStyle()
    {
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("EditingElementStyle = comboStyle", code);
        Assert.Contains("EditingElementStyle = (Style)FindResource(\"TradeLogComboBoxStyle\")", code);
    }

    [Fact]
    public void ManualDataEntryScope_PremiumDecisionShowsStrategyOtcMapAndBaseSettingsTabs()
    {
        IReadOnlyList<string> visibleTabs = ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.PremiumDecision);

        Assert.Equal(new[] { "策略配置", "OTCMap", "底仓基准设置" }, visibleTabs);
        Assert.Equal("策略配置", ManualDataEntryWindow.GetDefaultTabHeader(ManualEntryScope.PremiumDecision));
        Assert.Equal("溢价决策配置", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.PremiumDecision));
        Assert.DoesNotContain("账户状态", visibleTabs);
        Assert.DoesNotContain("持仓", visibleTabs);
        Assert.DoesNotContain("TradeLog", visibleTabs);
    }

    [Fact]
    public void ManualDataEntryScope_TradeLogShowsOnlyTradeLogTab()
    {
        IReadOnlyList<string> visibleTabs = ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.TradeLog);

        Assert.Equal(new[] { "TradeLog" }, visibleTabs);
        Assert.Equal("TradeLog", ManualDataEntryWindow.GetDefaultTabHeader(ManualEntryScope.TradeLog));
        Assert.Equal("交易日志录入", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.TradeLog));
    }

    [Fact]
    public void ManualDataEntryScope_SystemSettingsShowsOnlyMaintenanceTab()
    {
        IReadOnlyList<string> visibleTabs = ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.SystemSettings);

        Assert.Equal(new[] { "系统维护" }, visibleTabs);
        Assert.Equal("系统维护", ManualDataEntryWindow.GetDefaultTabHeader(ManualEntryScope.SystemSettings));
        Assert.Equal("系统设置", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.SystemSettings));
        Assert.DoesNotContain("账户状态", visibleTabs);
        Assert.DoesNotContain("持仓", visibleTabs);
        Assert.DoesNotContain("策略配置", visibleTabs);
        Assert.DoesNotContain("OTCMap", visibleTabs);
        Assert.DoesNotContain("底仓基准设置", visibleTabs);
        Assert.DoesNotContain("TradeLog", visibleTabs);
    }

    [Fact]
    public void ManualDataEntry_SystemSettingsContainsHotkeySettingsWithoutBusinessTabs()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("界面快捷键", code);
        Assert.Contains("预警设置", code);
        Assert.Contains("PushPlus Token", code);
        Assert.Contains("测试微信", code);
        Assert.Contains("测试语音", code);
        Assert.Contains("重复提醒间隔", code);
        Assert.Contains("严重风险间隔", code);
        Assert.Contains("行情异常间隔", code);
        Assert.Contains("SystemMaintenanceTabRoot.Children.Add(root)", code);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", code);
        Assert.Contains("显示/隐藏窗口", code);
        Assert.Contains("请按快捷键", code);
        Assert.Contains("恢复默认设置", code);
        Assert.DoesNotContain("启用隐藏/显示快捷键", code);
        Assert.DoesNotContain("保存快捷键设置", code);
        Assert.DoesNotContain("修饰键：", code);
        Assert.DoesNotContain("主键：", code);
        Assert.DoesNotContain("HotkeyStatusProvider", code);
        Assert.Contains("Header=\"系统维护\"", xaml);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { StrategyTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { AccountTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { PositionTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { OtcTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { TradeLogTabHeader", code);
    }

    [Fact]
    public void ManualDataEntry_SystemSettingsContainsAlertSettingsWithoutBusinessTabs()
    {
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("CreateAlertSettingsPanel", code);
        Assert.Contains("保存预警设置", code);
        Assert.Contains("启用微信预警", code);
        Assert.Contains("PushPlus Token", code);
        Assert.Contains("测试微信", code);
        Assert.Contains("启用系统语音", code);
        Assert.Contains("测试语音", code);
        Assert.Contains("重复提醒间隔", code);
        Assert.Contains("严重风险间隔", code);
        Assert.Contains("行情异常间隔", code);
        Assert.Contains("alert_pushplus_token", ReadRepositoryFile(Path.Combine("Core", "Models", "AlertSettings.cs")));
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { AccountTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { PositionTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { StrategyTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { OtcTabHeader", code);
        Assert.DoesNotContain("ManualEntryScope.SystemSettings => new[] { TradeLogTabHeader", code);
    }

    [Fact]
    public void ManualDataEntry_HotkeyCaptureCreatesCtrlShiftF9()
    {
        Assert.True(ManualDataEntryWindow.TryCreateHotkeySettingsFromInput(
            Key.F9,
            ModifierKeys.Control | ModifierKeys.Shift,
            out var settings,
            out string? error), error);

        Assert.True(settings.Enabled);
        Assert.Equal(HotkeyModifierKeys.Ctrl | HotkeyModifierKeys.Shift, settings.Modifiers);
        Assert.Equal("F9", settings.Key);
        Assert.Equal("Ctrl+Shift+F9", settings.DisplayText);
    }

    [Fact]
    public void ManualDataEntry_HotkeyCaptureCreatesAltNumberWithDStorageAndDigitDisplay()
    {
        Assert.True(ManualDataEntryWindow.TryCreateHotkeySettingsFromInput(
            Key.D1,
            ModifierKeys.Alt,
            out var settings,
            out string? error), error);

        Assert.Equal(HotkeyModifierKeys.Alt, settings.Modifiers);
        Assert.Equal("D1", settings.Key);
        Assert.Equal("Alt+1", settings.DisplayText);
    }

    [Theory]
    [InlineData(Key.H, ModifierKeys.None)]
    [InlineData(Key.Escape, ModifierKeys.Control)]
    [InlineData(Key.Enter, ModifierKeys.Control)]
    [InlineData(Key.Tab, ModifierKeys.Control)]
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]
    public void ManualDataEntry_HotkeyCaptureRejectsInvalidCombinations(Key key, ModifierKeys modifiers)
    {
        Assert.False(ManualDataEntryWindow.TryCreateHotkeySettingsFromInput(
            key,
            modifiers,
            out _,
            out string? error));
        Assert.Equal("快捷键无效", error);
    }

    [Fact]
    public void ManualDataEntryScope_AllKeepsOriginalTabs()
    {
        IReadOnlyList<string> visibleTabs = ManualDataEntryWindow.GetVisibleTabHeaders(ManualEntryScope.All);

        Assert.Equal(new[] { "策略配置", "账户状态", "持仓", "OTCMap", "底仓基准设置", "TradeLog", "系统维护" }, visibleTabs);
        Assert.Equal("策略配置", ManualDataEntryWindow.GetDefaultTabHeader(ManualEntryScope.All));
        Assert.Equal("本地数据手动录入", ManualDataEntryWindow.GetWindowTitle(ManualEntryScope.All));
    }

    [Fact]
    public void MainWindowNavigation_MapsPremiumDecisionAndTradeLogToManualEntryScopes()
    {
        Assert.Equal(ManualEntryScope.PremiumDecision, MainWindow.ResolveManualEntryScopeForNavigation("溢价决策"));
        Assert.Equal(ManualEntryScope.TradeLog, MainWindow.ResolveManualEntryScopeForNavigation("交易日志"));
        Assert.Equal(ManualEntryScope.SystemSettings, MainWindow.ResolveManualEntryScopeForNavigation("系统设置"));
        Assert.Null(MainWindow.ResolveManualEntryScopeForNavigation("作战总览"));
    }

    [Fact]
    public void MainWindowGearEntry_OpensSystemSettingsScopeInsteadOfAllScope()
    {
        string code = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("=> OpenManualEntry(ManualEntryScope.SystemSettings);", code);
        Assert.DoesNotContain("=> OpenManualEntry(ManualEntryScope.All);", code);
    }

    [Fact]
    public void ManualDataEntry_BasePositionSettingsHasDedicatedPremiumDecisionTabAndSavePath()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("BasePositionSettingsTabItem", xaml);
        Assert.Contains("Header=\"底仓基准设置\"", xaml);
        Assert.Contains("保存底仓基准设置", code);
        Assert.Contains("private void SaveBasePositionSettings()", code);
        Assert.Equal(1, CountOccurrences(code, "_repository.SaveBasePositionSettings(_basePositionSettings);"));
    }

    [Fact]
    public void ManualDataEntry_SystemMaintenanceTabDoesNotUseEditableBusinessToolbar()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.Contains("SystemMaintenanceTabItem", xaml);
        Assert.Contains("Header=\"系统维护\"", xaml);
        Assert.Contains("BuildSystemMaintenanceTab", code);
        Assert.DoesNotContain("SystemMaintenanceTabRoot.Children.Add(CreateEditableTab", code);
        Assert.DoesNotContain("SystemMaintenanceTabRoot.Children.Add(CreateButton", code);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
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
