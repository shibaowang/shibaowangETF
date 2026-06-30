using CrossETF.Terminal.UiShell.Reference;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class RiskCenterWindowTests
{
    [Fact]
    public void RiskCenterNavigation_IsActionableWithoutManualEntryScope()
    {
        Assert.True(MainWindow.IsRiskCenterNavigation("风险中心"));
        Assert.True(MainWindow.IsActionableNavigation("风险中心"));
        Assert.Null(MainWindow.ResolveManualEntryScopeForNavigation("风险中心"));
    }

    [Fact]
    public void RiskCenterWindow_UsesAlertLogAndEmptyState()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.Contains("风险中心 / 预警日志", xaml);
        Assert.Contains("只读展示历史预警事件，不代表当前数据源实时状态", xaml);
        Assert.Contains("暂无预警日志", xaml);
        Assert.Contains("ReadAlertLogs(100)", code);
        Assert.Contains("IsReadOnly", xaml);
        Assert.Contains("刷新日志", xaml);
        Assert.Contains("清空日志", xaml);
        Assert.Contains("Header=\"微信发送\"", xaml);
        Assert.Contains("Header=\"语音播报\"", xaml);
        Assert.DoesNotContain("Header=\"微信状态\"", xaml);
        Assert.DoesNotContain("Header=\"语音状态\"", xaml);
    }

    [Fact]
    public void RiskCenterWindow_UsesDarkScrollBarsAndConfirmationWithoutDelivery()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.Contains("RiskScrollBarThumbStyle", xaml);
        Assert.Contains("RiskScrollBarTrackBrush", xaml);
        Assert.Contains("TargetType=\"ScrollBar\"", xaml);
        Assert.Contains("TargetType=\"ScrollViewer\"", xaml);
        Assert.Contains("确认清空全部预警日志吗？", code);
        Assert.Contains("ClearAlertLogs", code);
        Assert.DoesNotContain("AlertDeliveryService", code);
        Assert.DoesNotContain("PushPlusAlertSender", code);
        Assert.DoesNotContain("VoiceAlertPlayer", code);
    }

    [Fact]
    public void RiskCenterWindow_RefreshOnlyReloadsAlertLogRows()
    {
        string code = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.Contains("RefreshLogsButton_Click", code);
        Assert.Contains("RefreshAlertLogs(true)", code);
        Assert.Contains("预警日志已刷新。", code);
        Assert.Contains("刷新预警日志失败", code);
        Assert.Contains("ReadAlertLogs(100)", code);
        Assert.DoesNotContain("ClearAlertLogs();\r\n            SetStatus(\"预警日志已刷新", code);
        Assert.DoesNotContain("SaveAlertLog", code);
        Assert.DoesNotContain("SaveAlertDeliveryState", code);
    }

    [Fact]
    public void RiskCenterRows_MapAlertLogFields()
    {
        IReadOnlyList<RiskAlertLogRow> rows = RiskCenterWindow.BuildRows(new[]
        {
            new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:30:00",
                AlertType = AlertTypes.StrategyDecision,
                Severity = AlertSeverity.Severe,
                StrategyCode = "159941",
                ActualCode = "159941",
                Title = "【作战指令】159941 全清换现金(留底)",
                WechatStatus = "成功",
                VoiceStatus = "失败",
                VoiceError = "voice failed",
                Source = "strategy_decision_state"
            }
        });

        RiskAlertLogRow row = Assert.Single(rows);
        Assert.Equal("159941/159941", row.Target);
        Assert.Equal("成功", row.WechatStatus);
        Assert.Equal("失败", row.VoiceStatus);
        Assert.Equal("voice failed", row.Error);
        Assert.Equal("strategy_decision_state", row.Source);
    }

    [Fact]
    public void RiskCenterRows_DisplayNotApplicableChannelStatus()
    {
        IReadOnlyList<RiskAlertLogRow> rows = RiskCenterWindow.BuildRows(new[]
        {
            new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:35:00",
                AlertType = AlertTypes.Test,
                Severity = AlertSeverity.Normal,
                Title = "【测试预警】PushPlus 微信预警测试",
                WechatStatus = "成功",
                VoiceStatus = "不适用",
                Source = "system_settings"
            }
        });

        RiskAlertLogRow row = Assert.Single(rows);
        Assert.Equal("成功", row.WechatStatus);
        Assert.Equal("不适用", row.VoiceStatus);
    }

    [Fact]
    public void RiskCenterRows_ShowMarketEventDetailWhenDeliverySucceeded()
    {
        IReadOnlyList<RiskAlertLogRow> rows = RiskCenterWindow.BuildRows(new[]
        {
            new AlertLogRecord
            {
                CreatedAt = "2026-06-29 14:46:35",
                AlertType = AlertTypes.MarketRuntime,
                Severity = AlertSeverity.Market,
                Title = "【行情异常】EASTMONEY_HISTORY",
                Content = "触发时间：2026-06-29 14:46:35<br>模块：EASTMONEY_HISTORY<br>级别：ERROR<br>消息：历史高点刷新失败<br>详情：251.NDXTMC: scheduler rate_limited; next=2026-06-29T15:07:49+08:00<br>来源：runtime_log",
                WechatStatus = "成功",
                VoiceStatus = "未启用",
                Source = "EASTMONEY_HISTORY"
            }
        });

        RiskAlertLogRow row = Assert.Single(rows);
        Assert.Equal("成功", row.WechatStatus);
        Assert.Equal("未启用", row.VoiceStatus);
        Assert.Contains("scheduler rate_limited", row.Error);
    }

    [Fact]
    public void RiskCenterRows_WechatSuccessDoesNotHideMarketRuntimeMessage()
    {
        IReadOnlyList<RiskAlertLogRow> rows = RiskCenterWindow.BuildRows(new[]
        {
            new AlertLogRecord
            {
                CreatedAt = "2026-06-29 15:47:55",
                AlertType = AlertTypes.MarketRuntime,
                Severity = AlertSeverity.Market,
                Title = "【行情异常】EASTMONEY_HISTORY",
                Content = "触发时间：2026-06-29 15:47:55<br>模块：EASTMONEY_HISTORY<br>级别：ERROR<br>消息：历史高点刷新失败<br>来源：runtime_log",
                WechatStatus = "成功",
                VoiceStatus = "未启用",
                Source = "EASTMONEY_HISTORY"
            }
        });

        RiskAlertLogRow row = Assert.Single(rows);
        Assert.Equal("消息：历史高点刷新失败", row.Error);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "CrossETF.Terminal.UiShell.Reference.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }
}
