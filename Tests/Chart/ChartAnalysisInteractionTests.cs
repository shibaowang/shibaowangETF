using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class ChartAnalysisInteractionTests
{
    [Theory]
    [InlineData(SecurityChartPeriod.Daily, 120)]
    [InlineData(SecurityChartPeriod.Weekly, 104)]
    [InlineData(SecurityChartPeriod.Monthly, 60)]
    public void Viewport_ResetUsesPeriodDefaultAndLatestEdge(SecurityChartPeriod period, int expected)
    {
        ChartViewportState state = ChartViewportCalculator.Reset(period, 300);

        Assert.Equal(expected, state.VisibleCount);
        Assert.Equal(300 - expected, state.VisibleStartIndex);
        Assert.True(state.IsAtLatestEdge);
    }

    [Fact]
    public void Viewport_DataBelowDefaultShowsAll()
    {
        ChartViewportState state = ChartViewportCalculator.Reset(SecurityChartPeriod.Daily, 12);

        Assert.Equal(12, state.VisibleCount);
        Assert.Equal(0, state.VisibleStartIndex);
    }

    [Fact]
    public void Viewport_ZoomNeverGoesBelowTwentyOrOutsideData()
    {
        ChartViewportState state = ChartViewportCalculator.Reset(SecurityChartPeriod.Daily, 300);
        for (int i = 0; i < 30; i++)
        {
            state = ChartViewportCalculator.ZoomAt(state, 0.5, 120);
        }

        Assert.Equal(20, state.VisibleCount);
        Assert.InRange(state.VisibleStartIndex, 0, state.TotalCount - state.VisibleCount);
        Assert.Equal(state.VisibleCount, ChartViewportCalculator.ResolveVisibleRange(state).Count);
    }

    [Fact]
    public void Viewport_ZoomOutClampsToAllAvailableData()
    {
        ChartViewportState state = ChartViewportCalculator.Reset(240, 20);
        for (int i = 0; i < 40; i++)
        {
            state = ChartViewportCalculator.ZoomAt(state, 0.5, -120);
        }

        Assert.Equal(240, state.VisibleCount);
        Assert.Equal(0, state.VisibleStartIndex);
    }

    [Fact]
    public void Viewport_MouseAnchorZoomKeepsAnchorKLineNearSameRatio()
    {
        ChartViewportState initial = new(300, 100, 100, false);
        double ratio = 0.25;
        int oldAnchor = initial.VisibleStartIndex + (int)Math.Floor(ratio * initial.VisibleCount);

        ChartViewportState zoomed = ChartViewportCalculator.ZoomAt(initial, ratio, 120);
        int newAnchor = zoomed.VisibleStartIndex + (int)Math.Floor(ratio * zoomed.VisibleCount);

        Assert.InRange(Math.Abs(newAnchor - oldAnchor), 0, 1);
    }

    [Fact]
    public void Viewport_PanRightMovesTowardHistoryAndLeftMovesTowardLatest()
    {
        ChartViewportState initial = new(300, 150, 80, false);
        ChartViewportState history = ChartViewportCalculator.Pan(initial, 12);
        ChartViewportState latest = ChartViewportCalculator.Pan(history, -20);

        Assert.Equal(138, history.VisibleStartIndex);
        Assert.Equal(158, latest.VisibleStartIndex);
    }

    [Fact]
    public void Viewport_PanClampsAtBothEdges()
    {
        ChartViewportState initial = new(300, 100, 80, false);

        ChartViewportState earliest = ChartViewportCalculator.Pan(initial, 1000);
        ChartViewportState latest = ChartViewportCalculator.Pan(initial, -1000);

        Assert.Equal(0, earliest.VisibleStartIndex);
        Assert.Equal(220, latest.VisibleStartIndex);
        Assert.True(latest.IsAtLatestEdge);
    }

    [Fact]
    public void Viewport_ResetRestoresDefaultLatestRange()
    {
        ChartViewportState state = ChartViewportCalculator.Reset(SecurityChartPeriod.Daily, 300);
        state = ChartViewportCalculator.ZoomAt(state, 0.2, 120);
        state = ChartViewportCalculator.Pan(state, 50);

        ChartViewportState reset = ChartViewportCalculator.Reset(SecurityChartPeriod.Daily, 300);

        Assert.Equal(120, reset.VisibleCount);
        Assert.Equal(180, reset.VisibleStartIndex);
        Assert.True(reset.IsAtLatestEdge);
    }

    [Fact]
    public void ViewportStore_KeepsDailyWeeklyAndMonthlyIndependent()
    {
        var store = new ChartViewportStore();
        store.Set(SecurityChartPeriod.Daily, new ChartViewportState(300, 80, 40, false));
        store.Set(SecurityChartPeriod.Weekly, new ChartViewportState(180, 20, 60, false));
        store.Set(SecurityChartPeriod.Monthly, new ChartViewportState(100, 40, 50, false));

        Assert.True(store.TryGet(SecurityChartPeriod.Daily, out ChartViewportState daily));
        Assert.True(store.TryGet(SecurityChartPeriod.Weekly, out ChartViewportState weekly));
        Assert.True(store.TryGet(SecurityChartPeriod.Monthly, out ChartViewportState monthly));
        Assert.Equal((80, 40), (daily.VisibleStartIndex, daily.VisibleCount));
        Assert.Equal((20, 60), (weekly.VisibleStartIndex, weekly.VisibleCount));
        Assert.Equal((40, 50), (monthly.VisibleStartIndex, monthly.VisibleCount));
    }

    [Fact]
    public void Viewport_NewKLineFollowsOnlyWhenAlreadyAtLatestEdge()
    {
        ChartViewportState latest = new(100, 60, 40, true);
        ChartViewportState history = new(100, 30, 40, false);

        ChartViewportState latestUpdated = ChartViewportCalculator.Reconcile(latest, SecurityChartPeriod.Daily, 101);
        ChartViewportState historyUpdated = ChartViewportCalculator.Reconcile(history, SecurityChartPeriod.Daily, 101);

        Assert.Equal(61, latestUpdated.VisibleStartIndex);
        Assert.Equal(30, historyUpdated.VisibleStartIndex);
        Assert.True(latestUpdated.IsAtLatestEdge);
        Assert.False(historyUpdated.IsAtLatestEdge);
    }

    [Fact]
    public void Viewport_DataShrinkClampsWithoutNegativeOrOverflowIndex()
    {
        ChartViewportState previous = new(300, 220, 80, true);

        ChartViewportState reconciled = ChartViewportCalculator.Reconcile(previous, SecurityChartPeriod.Daily, 35);

        Assert.Equal(0, reconciled.VisibleStartIndex);
        Assert.Equal(35, reconciled.VisibleCount);
        Assert.Equal(35, reconciled.VisibleEndExclusive);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(60)]
    public void MovingAverage_CalculatesSmaFromCurrentPeriodCloses(int period)
    {
        KLinePoint[] lines = Enumerable.Range(1, period + 2).Select(index => K(index, index)).ToArray();

        MovingAverageSeries series = MovingAverageSeriesBuilder.Build(lines, period);

        Assert.Null(series.Values[period - 2]);
        Assert.Equal((period + 1) / 2.0, series.Values[period - 1]!.Value, 10);
        Assert.Equal((period + 3) / 2.0, series.Values[period]!.Value, 10);
    }

    [Fact]
    public void MovingAverage_InsufficientOrInvalidWindowDoesNotUseZeroFill()
    {
        KLinePoint[] lines = Enumerable.Range(1, 6).Select(index => K(index, index)).ToArray();
        lines[3].Close = 0;

        MovingAverageSeries series = MovingAverageSeriesBuilder.Build(lines, 5);

        Assert.All(series.Values, Assert.Null);
    }

    [Fact]
    public void MovingAverage_FullSeriesValueDoesNotChangeWhenViewportChanges()
    {
        KLinePoint[] lines = Enumerable.Range(1, 100).Select(index => K(index, index)).ToArray();
        MovingAverageSeries full = MovingAverageSeriesBuilder.Build(lines, 20);

        double? before = full.Values[79];
        double? afterZoom = full.Values.Skip(60).Take(20).Last();

        Assert.Equal(before, afterZoom);
    }

    [Fact]
    public void MovingAverage_DisplayOnlyQuoteBarParticipatesWithoutPersistenceSideEffects()
    {
        KLinePoint[] lines = Enumerable.Range(1, 4).Select(index => K(index, index)).Append(new KLinePoint
        {
            Date = new DateTime(2026, 1, 5),
            Open = 5,
            High = 5,
            Low = 5,
            Close = 5,
            IsDisplayOnly = true,
            PointSource = "QUOTE_INTRADAY_BAR"
        }).ToArray();

        MovingAverageSeries series = MovingAverageSeriesBuilder.Build(lines, 5);

        Assert.Equal(3, series.Values[^1]!.Value, 10);
        Assert.True(lines[^1].IsDisplayOnly);
    }

    [Theory]
    [InlineData("买入", ChartTradeMarkerType.B)]
    [InlineData("卖出", ChartTradeMarkerType.S)]
    public void TradeMarkers_MapsOnlyRealBuyAndSellActions(string action, ChartTradeMarkerType expected)
    {
        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(
            SecurityChartPeriod.Daily,
            new[] { Trade("2026-01-03 10:00:00", "159509", "159509", action) });

        Assert.Equal(expected, Assert.Single(markers).MarkerType);
    }

    [Theory]
    [InlineData("CASH")]
    [InlineData("入金")]
    [InlineData("出金")]
    [InlineData("分红")]
    [InlineData("送股")]
    [InlineData("拆分")]
    [InlineData("合并")]
    [InlineData("除权校准")]
    public void TradeMarkers_IgnoresNonBuySellActions(string action)
    {
        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(
            SecurityChartPeriod.Daily,
            new[] { Trade("2026-01-03 10:00:00", "159509", "159509", action) });

        Assert.Empty(markers);
    }

    [Fact]
    public void TradeMarkers_EtfUsesExactActualCodeAndDoesNotCrossSymbols()
    {
        TradeLogRecord[] logs =
        {
            Trade("2026-01-03 10:00:00", "159509", "159509", "买入"),
            Trade("2026-01-03 10:01:00", "159509", "159941", "卖出")
        };

        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(SecurityChartPeriod.Daily, logs);

        Assert.Equal(ChartTradeMarkerType.B, Assert.Single(markers).MarkerType);
    }

    [Fact]
    public void TradeMarkers_EtfAllowsLegacyExchangeStrategyFallbackButRejectsOtcSubstitute()
    {
        TradeLogRecord legacy = Trade("2026-01-03 10:00:00", "159509", null, "买入");
        TradeLogRecord otc = Trade("2026-01-03 11:00:00", "159509", null, "卖出");
        otc.Source = "场外替代";

        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(SecurityChartPeriod.Daily, new[] { legacy, otc });

        Assert.Equal(ChartTradeMarkerType.B, Assert.Single(markers).MarkerType);
    }

    [Fact]
    public void TradeMarkers_DailyRequiresExactKLineDateAndNeverMovesToNeighbor()
    {
        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(
            SecurityChartPeriod.Daily,
            new[] { Trade("2026-01-02 10:00:00", "159509", "159509", "买入") });

        Assert.Empty(markers);
    }

    [Fact]
    public void TradeMarkers_DeduplicatesSamePeriodTypeButKeepsBothBuyAndSell()
    {
        TradeLogRecord[] logs =
        {
            Trade("2026-01-03 10:00:00", "159509", "159509", "买入"),
            Trade("2026-01-03 10:01:00", "159509", "159509", "买入"),
            Trade("2026-01-03 10:02:00", "159509", "159509", "卖出"),
            Trade("2026-01-03 10:03:00", "159509", "159509", "卖出")
        };

        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(SecurityChartPeriod.Daily, logs);

        Assert.Equal(2, markers.Count);
        Assert.Contains(markers, marker => marker.MarkerType == ChartTradeMarkerType.B);
        Assert.Contains(markers, marker => marker.MarkerType == ChartTradeMarkerType.S);
    }

    [Fact]
    public void TradeMarkers_WeeklyUsesExistingMondayPeriodRule()
    {
        KLinePoint[] weekly = KLineAggregator.AggregateWeekly(new[]
        {
            K(new DateTime(2026, 1, 5), 1),
            K(new DateTime(2026, 1, 9), 2)
        }).ToArray();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "ETF");

        IReadOnlyList<ChartTradeMarker> markers = ChartTradeMarkerBuilder.Build(
            security,
            SecurityChartPeriod.Weekly,
            weekly,
            new[] { Trade("2026-01-07 10:00:00", "159509", "159509", "买入") },
            Array.Empty<StrategyConfigRecord>());

        Assert.Equal(0, Assert.Single(markers).KLineIndex);
        Assert.Equal(new DateTime(2026, 1, 5), markers[0].PeriodKey);
    }

    [Fact]
    public void TradeMarkers_MonthlyAggregatesByYearAndMonth()
    {
        KLinePoint[] monthly = KLineAggregator.AggregateMonthly(new[]
        {
            K(new DateTime(2026, 1, 5), 1),
            K(new DateTime(2026, 1, 30), 2)
        }).ToArray();
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "ETF");

        IReadOnlyList<ChartTradeMarker> markers = ChartTradeMarkerBuilder.Build(
            security,
            SecurityChartPeriod.Monthly,
            monthly,
            new[] { Trade("2026-01-20 10:00:00", "159509", "159509", "卖出") },
            Array.Empty<StrategyConfigRecord>());

        Assert.Equal(new DateTime(2026, 1, 1), Assert.Single(markers).PeriodKey);
    }

    [Fact]
    public void TradeMarkers_IndexUsesEnabledIndexSecIdRelationsAndDeduplicatesStrategies()
    {
        ChartSecurityInfo index = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "Index");
        StrategyConfigRecord[] strategies =
        {
            Strategy("159509", "100.NDX100", true),
            Strategy("159941", "100.NDX100", true),
            Strategy("159660", "251.NDXTMC", true),
            Strategy("159513", "100.NDX100", false)
        };
        TradeLogRecord[] logs =
        {
            Trade("2026-01-03 10:00:00", "159509", "159509", "买入"),
            Trade("2026-01-03 10:01:00", "159941", "159941", "买入"),
            Trade("2026-01-03 10:02:00", "159660", "159660", "卖出"),
            Trade("2026-01-03 10:03:00", "159513", "159513", "卖出")
        };

        IReadOnlyList<ChartTradeMarker> markers = ChartTradeMarkerBuilder.Build(
            index,
            SecurityChartPeriod.Daily,
            DailyLines(),
            logs,
            strategies);

        Assert.Equal(ChartTradeMarkerType.B, Assert.Single(markers).MarkerType);
    }

    [Fact]
    public void TradeMarkers_IntradayNeverProducesMarkers()
    {
        IReadOnlyList<ChartTradeMarker> markers = BuildEtfMarkers(
            SecurityChartPeriod.Intraday,
            new[] { Trade("2026-01-03 10:00:00", "159509", "159509", "买入") });

        Assert.Empty(markers);
    }

    [Fact]
    public void TradeMarkerBuilder_HasNoSecurityCodeHardcodingOrPersistenceAndNetworkCalls()
    {
        string source = ReadRepositoryFile(Path.Combine("Core", "Services", "ChartTradeMarkerBuilder.cs"));

        Assert.DoesNotContain("251.NDXTMC", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("100.NDX100", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Http", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("order_draft_state", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StrategyDecisionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_ExposesLocalAnalysisControlsAndMouseInteractions()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "SecurityChartWindow.xaml"));
        string source = ReadRepositoryFile(Path.Combine("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("x:Name=\"Ma5Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Ma10Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Ma20Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Ma60Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResetViewButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseWheel=\"MainChartCanvas_MouseWheel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonDown=\"MainChartCanvas_MouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseMove=\"MainChartCanvas_MouseMove\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonUp=\"MainChartCanvas_MouseLeftButtonUp\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeave=\"MainChartCanvas_MouseLeave\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LostMouseCapture=\"MainChartCanvas_LostMouseCapture\"", xaml, StringComparison.Ordinal);
        Assert.Contains("e.ClickCount >= 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_TradeMarkersRenderOnlyLettersWithoutTradeDetails()
    {
        string source = ReadRepositoryFile(Path.Combine("Views", "SecurityChartWindow.xaml.cs"));
        int start = source.IndexOf("private void DrawTradeMarkers", StringComparison.Ordinal);
        int end = source.IndexOf("private MacdPoint[] ResolveVisibleMacd", start, StringComparison.Ordinal);
        string method = source[start..end];

        Assert.Contains("Text = marker.MarkerType.ToString()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Arrow", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ellipse", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quantity", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Amount", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ToolTip", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChartAnalysisInteractionsUseInMemoryProvidersAndDoNotRequestOrPersist()
    {
        string manager = ReadRepositoryFile(Path.Combine("Views", "ChartWindowManager.cs"));
        string source = ReadRepositoryFile(Path.Combine("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("Func<IReadOnlyList<TradeLogRecord>>", manager, StringComparison.Ordinal);
        Assert.Contains("UpdateTradeContext", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLog", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadTradeLogs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTradeLog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDataClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartSnapshot_KeepsFullKLineAndMacdSeriesForViewport()
    {
        string service = ReadRepositoryFile(Path.Combine("Core", "Services", "ChartDataService.cs"));

        Assert.DoesNotContain("MaxKLineDisplayPoints", service, StringComparison.Ordinal);
        Assert.DoesNotContain("periodKLines.TakeLast", service, StringComparison.Ordinal);
        Assert.DoesNotContain("macd.TakeLast", service, StringComparison.Ordinal);
    }

    private static IReadOnlyList<ChartTradeMarker> BuildEtfMarkers(
        SecurityChartPeriod period,
        IReadOnlyList<TradeLogRecord> logs)
    {
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "ETF");
        return ChartTradeMarkerBuilder.Build(
            security,
            period,
            DailyLines(),
            logs,
            Array.Empty<StrategyConfigRecord>());
    }

    private static KLinePoint[] DailyLines()
        => new[]
        {
            K(new DateTime(2026, 1, 1), 1),
            K(new DateTime(2026, 1, 3), 2),
            K(new DateTime(2026, 1, 5), 3)
        };

    private static KLinePoint K(int day, double close)
        => K(new DateTime(2026, 1, 1).AddDays(day - 1), close);

    private static KLinePoint K(DateTime date, double close)
        => new()
        {
            Date = date,
            Open = close,
            High = close + 0.2,
            Low = Math.Max(0.01, close - 0.2),
            Close = close,
            Volume = 100
        };

    private static TradeLogRecord Trade(string time, string strategyCode, string? actualCode, string action)
        => new()
        {
            Time = time,
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Action = action,
            Source = "场内ETF"
        };

    private static StrategyConfigRecord Strategy(string code, string indexSecId, bool enabled)
        => new()
        {
            Code = code,
            Name = code,
            IndexSecId = indexSecId,
            Enabled = enabled
        };

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
