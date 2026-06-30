namespace CrossETF.Terminal.UiShell.Reference.Tests.Regression;

public sealed class LockedModulesDocumentationTests
{
    [Fact]
    public void LockedModulesDocument_ExistsAndListsCoreLocks()
    {
        string document = File.ReadAllText(FindWorkspaceFile("docs", "LOCKED_MODULES.md"));

        Assert.Contains("TradeLog", document, StringComparison.Ordinal);
        Assert.Contains("OrderDraft", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StrategyDecisionService", document, StringComparison.Ordinal);
        Assert.Contains("GlobalMarketRequestScheduler", document, StringComparison.Ordinal);
        Assert.Contains("TENCENT_DAILY_QFQ", document, StringComparison.Ordinal);
        Assert.Contains("251.NDXTMC", document, StringComparison.Ordinal);
        Assert.Contains("100.NDX100", document, StringComparison.Ordinal);
        Assert.Contains("K线盘中显示用临时K线", document, StringComparison.Ordinal);
        Assert.Contains("顶部行情专业显示", document, StringComparison.Ordinal);
        Assert.Contains("分时首屏缓存", document, StringComparison.Ordinal);
        Assert.Contains("后台分时防封更新", document, StringComparison.Ordinal);
        Assert.Contains("CROSSETF_SMOKE_MODE=1", document, StringComparison.Ordinal);
        Assert.Contains("TencentProbe", document, StringComparison.Ordinal);
    }

    [Fact]
    public void LockedModulesDocument_DefinesUnlockProtocolAndForbiddenSideEffects()
    {
        string document = File.ReadAllText(FindWorkspaceFile("docs", "LOCKED_MODULES.md"));

        Assert.Contains("后续任务解锁流程", document, StringComparison.Ordinal);
        Assert.Contains("等用户确认后才能继续", document, StringComparison.Ordinal);
        Assert.Contains("TradeLog", document, StringComparison.Ordinal);
        Assert.Contains("order_draft_state", document, StringComparison.Ordinal);
        Assert.Contains("alert_log", document, StringComparison.Ordinal);
        Assert.Contains("runtime_log", document, StringComparison.Ordinal);
        Assert.Contains("market_history_cache", document, StringComparison.Ordinal);
        Assert.Contains("是否会改变数据源请求频率", document, StringComparison.Ordinal);
        Assert.Contains("是否会绕过 `GlobalMarketRequestScheduler`", document, StringComparison.Ordinal);
        Assert.Contains("禁止自动成交写入 TradeLog", document, StringComparison.Ordinal);
        Assert.Contains("禁止高频请求", document, StringComparison.Ordinal);
    }

    [Fact]
    public void LockedModulesDocument_DefinesLiveKLineDisplayOnlyContract()
    {
        string document = File.ReadAllText(FindWorkspaceFile("docs", "LOCKED_MODULES.md"));
        string chartService = File.ReadAllText(FindWorkspaceFile("Core", "Services", "ChartDataService.cs"));

        Assert.Contains("DailyLike 历史K + 可选 quote 临时K线", document, StringComparison.Ordinal);
        Assert.Contains("IsDisplayOnly=true", document, StringComparison.Ordinal);
        Assert.Contains("PointSource=QUOTE_INTRADAY_BAR", document, StringComparison.Ordinal);
        Assert.Contains("quote 缺少 OHLC 时不得用当前价造K", document, StringComparison.Ordinal);
        Assert.Contains("周K/月K只本地聚合，不联网", document, StringComparison.Ordinal);
        Assert.Contains("159509", document, StringComparison.Ordinal);
        Assert.Contains("QUOTE_INTRADAY_BAR is display-only", chartService, StringComparison.Ordinal);
    }

    [Fact]
    public void LockedModulesDocument_DefinesIndexQuoteAndDrawdownRealtimeLocks()
    {
        string document = File.ReadAllText(FindWorkspaceFile("docs", "LOCKED_MODULES.md"));

        Assert.Contains("TASK-MARKET-INDEX-QUOTE-LANE-023", document, StringComparison.Ordinal);
        Assert.Contains("TASK-INDEX-DRAWDOWN-QUOTE-REFRESH-024", document, StringComparison.Ordinal);
        Assert.Contains("TASK-INDEX-DRAWDOWN-LATEST-POINT-025", document, StringComparison.Ordinal);
        Assert.Contains("IndexQuote / ulist.np", document, StringComparison.Ordinal);
        Assert.Contains("IndexIntraday / trends2", document, StringComparison.Ordinal);
        Assert.Contains("IndexDailyHistory / push2his", document, StringComparison.Ordinal);
        Assert.Contains("latestPoint.Date == lastHistoryDate", document, StringComparison.Ordinal);
        Assert.Contains("market_history_cache", document, StringComparison.Ordinal);
        Assert.Contains("GlobalMarketRequestSchedulerTests", document, StringComparison.Ordinal);
        Assert.Contains("IndexDrawdownQuoteRefreshHelperTests", document, StringComparison.Ordinal);
        Assert.Contains("IndexDrawdownChartSeriesBuilderTests", document, StringComparison.Ordinal);
    }

    [Fact]
    public void LockedModulesDocument_DefinesIndexIntradayFullSessionLocks()
    {
        string document = File.ReadAllText(FindWorkspaceFile("docs", "LOCKED_MODULES.md"));
        string chartService = File.ReadAllText(FindWorkspaceFile("Core", "Services", "ChartDataService.cs"));
        string completenessService = File.ReadAllText(FindWorkspaceFile("Core", "Services", "IndexIntradayCacheCompletenessService.cs"));
        string timeAxis = File.ReadAllText(FindWorkspaceFile("Core", "Services", "IntradayTradingTimeAxis.cs"));
        string volumeNormalizer = File.ReadAllText(FindWorkspaceFile("Core", "Services", "IntradayVolumeNormalizer.cs"));
        string chartWindow = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("TASK-INDEX-INTRADAY-CATCHUP-027", document, StringComparison.Ordinal);
        Assert.Contains("TASK-INDEX-INTRADAY-CLOSE-QUOTE-ALIGN-028", document, StringComparison.Ordinal);
        Assert.Contains("TASK-INDEX-INTRADAY-US-SESSION-DATE-029", document, StringComparison.Ordinal);
        Assert.Contains("TASK-INDEX-INTRADAY-MACD-VOLUME-030", document, StringComparison.Ordinal);
        Assert.Contains("Index intraday full-session display locks", document, StringComparison.Ordinal);
        Assert.Contains("symbol + latestCompletedUsTradeDate", document, StringComparison.Ordinal);
        Assert.Contains("QUOTE_CLOSE_DISPLAY", document, StringComparison.Ordinal);
        Assert.Contains("21:30-23:59", document, StringComparison.Ordinal);
        Assert.Contains("00:00-04:00", document, StringComparison.Ordinal);
        Assert.Contains("TakeLast(260)", document, StringComparison.Ordinal);
        Assert.Contains("成交量数据不可用", document, StringComparison.Ordinal);
        Assert.Contains("`100.NDX100` volume must not be used as `251.NDXTMC` volume", document, StringComparison.Ordinal);
        Assert.Contains("chart_intraday_cache", document, StringComparison.Ordinal);
        Assert.Contains("SecurityChartServiceTests", document, StringComparison.Ordinal);
        Assert.Contains("ChartDataSourceRoutingTests", document, StringComparison.Ordinal);
        Assert.Contains("LockedModulesDocumentationTests", document, StringComparison.Ordinal);

        Assert.Contains("LOCKED: QUOTE_CLOSE_DISPLAY is display-only close alignment", chartService, StringComparison.Ordinal);
        Assert.Contains("LOCKED: Index intraday MACD keeps the full US session", chartService, StringComparison.Ordinal);
        Assert.Contains("LOCKED: Accepted index intraday full-session behavior", completenessService, StringComparison.Ordinal);
        Assert.Contains("LOCKED: Accepted index charts use US Eastern", timeAxis, StringComparison.Ordinal);
        Assert.Contains("LOCKED: Volume bars must use real source volume only", volumeNormalizer, StringComparison.Ordinal);
        Assert.Contains("LOCKED: Index quote tails stay independent", chartWindow, StringComparison.Ordinal);
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
