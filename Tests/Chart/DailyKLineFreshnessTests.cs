using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class DailyKLineFreshnessTests
{
    private static readonly DateTimeOffset BeijingMorning = new(2026, 7, 21, 9, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ExpectedCompletedTradingDate_UsesMarketLocalCloseInsteadOfCallerCalendarDate()
    {
        Assert.Equal(
            new DateOnly(2026, 7, 20),
            DailyKLineFreshnessService.GetExpectedCompletedTradingDate(ChartInstrumentType.Etf, BeijingMorning));
        Assert.Equal(
            new DateOnly(2026, 7, 20),
            DailyKLineFreshnessService.GetExpectedCompletedTradingDate(
                ChartInstrumentType.Etf,
                new DateTimeOffset(2026, 7, 20, 16, 0, 0, TimeSpan.FromHours(8))));
        Assert.Equal(
            new DateOnly(2026, 7, 20),
            DailyKLineFreshnessService.GetExpectedCompletedTradingDate(
                ChartInstrumentType.Index,
                new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void LatestRealDailyDate_ExcludesEveryQuoteGeneratedDisplayBar()
    {
        KLinePoint[] points =
        {
            Point(new DateTime(2026, 7, 10), 100),
            Point(new DateTime(2026, 7, 20), 101, displayOnly: true, source: "QUOTE_INTRADAY_BAR"),
            Point(new DateTime(2026, 7, 21), 102, source: "QUOTE_OTHER_DISPLAY")
        };

        Assert.Equal(new DateOnly(2026, 7, 10), DailyKLineFreshnessService.GetLatestRealDailyDate(points));
        Assert.True(DailyKLineFreshnessService.NeedsTailCatchUp(points, new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void MergeRealDaily_PreservesDeepHistory_OverwritesSameDate_AndDoesNotInventWeekends()
    {
        KLinePoint[] existing = BusinessDaysEnding(new DateTime(2026, 7, 10), 3000);
        KLinePoint[] incoming = BusinessDaysEnding(new DateTime(2026, 7, 20), 320);
        KLinePoint overwritten = incoming.Single(point => point.Date == new DateTime(2026, 7, 10));
        overwritten.Open = 776;
        overwritten.High = 778;
        overwritten.Low = 775;
        overwritten.Close = 777;
        KLinePoint displayOnly = Point(new DateTime(2026, 7, 21), 888, displayOnly: true, source: "QUOTE_INTRADAY_BAR");

        IReadOnlyList<KLinePoint> merged = DailyKLineFreshnessService.MergeRealDaily(
            existing.Append(displayOnly),
            incoming);

        Assert.Equal(3006, merged.Count);
        Assert.Equal(existing[0].Date, merged[0].Date);
        Assert.Equal(777, merged.Single(point => point.Date == new DateTime(2026, 7, 10)).Close);
        Assert.Contains(merged, point => point.Date == new DateTime(2026, 7, 13));
        Assert.Contains(merged, point => point.Date == new DateTime(2026, 7, 20));
        Assert.DoesNotContain(merged, point => point.Date is { Year: 2026, Month: 7, Day: 11 or 12 or 21 });
        Assert.Equal(merged.Count, merged.Select(point => point.Date.Date).Distinct().Count());
    }

    [Fact]
    public void MergedFormalPayload_IsDailyLikeAndIdentifiesItsFormalMergedOrigin()
    {
        KLinePoint[] points = BusinessDaysEnding(new DateTime(2026, 7, 20), 320);

        string payload = DailyKLineFreshnessService.BuildMergedFormalPayload(
            points,
            MarketSources.TencentHistory,
            new DateOnly(2026, 7, 20),
            BeijingMorning);

        Assert.Contains("MERGED_REAL_DAILY_LIKE", payload, StringComparison.Ordinal);
        Assert.Contains(MarketSources.TencentHistory, payload, StringComparison.Ordinal);
        Assert.True(MarketHistoryQuality.IsDailyLike(payload));
        Assert.Equal(320, EastMoneyHistoryParser.ParsePoints(payload).Count);
    }

    [Fact]
    public void QuoteDisplayBar_ReproducesNextDayDisappearanceWhenFormalTailWasNeverPersisted()
    {
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");
        MarketQuoteRecord history = History(security, BusinessDaysEnding(new DateTime(2026, 7, 10), 3000), MarketSources.TencentHistory);

        SecurityChartSnapshot closeSnapshot = ChartDataService.BuildSnapshot(
            security,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { Quote("159941", new DateTime(2026, 7, 20, 15, 0, 0), 101) },
            new[] { history },
            null,
            null);
        SecurityChartSnapshot nextDaySnapshot = ChartDataService.BuildSnapshot(
            security,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { Quote("159941", new DateTime(2026, 7, 21, 9, 30, 0), 102) },
            new[] { history },
            null,
            null);

        Assert.Contains(closeSnapshot.KLines, point => point.Date == new DateTime(2026, 7, 20) && point.IsDisplayOnly);
        Assert.DoesNotContain(nextDaySnapshot.KLines, point => point.Date == new DateTime(2026, 7, 20));
        Assert.Contains(nextDaySnapshot.KLines, point => point.Date == new DateTime(2026, 7, 21) && point.IsDisplayOnly);
    }

    [Fact]
    public async Task StaleEtfDailyTail_UsesTencent_MergesPersists_AndSurvivesReload()
    {
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "ETF");
        MarketQuoteRecord history = History(security, BusinessDaysEnding(new DateTime(2026, 7, 10), 3000), MarketSources.TencentHistory);
        var client = new TailChartMarketDataClient
        {
            TencentResult = Result("sz159941", BusinessDaysEnding(new DateTime(2026, 7, 20), 320))
        };
        string? persistedPayload = null;
        string? persistedSource = null;
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            historyCacheSaver: (_, _, _, payload, source) =>
            {
                persistedPayload = payload;
                persistedSource = source;
            },
            nowProvider: () => BeijingMorning);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { history }, CancellationToken.None);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { history }, CancellationToken.None);

        Assert.Equal(1, client.TencentDailyRequests);
        Assert.Equal(0, client.EastMoneyDailyRequests);
        Assert.Equal(MarketSources.TencentHistory, persistedSource);
        Assert.NotNull(persistedPayload);
        IReadOnlyList<MarketHistoryPoint> persisted = EastMoneyHistoryParser.ParsePoints(persistedPayload!);
        Assert.Equal(3006, persisted.Count);
        Assert.Contains(persisted, point => point.Date == new DateTime(2026, 7, 13));
        Assert.Contains(persisted, point => point.Date == new DateTime(2026, 7, 20));
        Assert.DoesNotContain(persisted, point => point.Date is { Year: 2026, Month: 7, Day: 11 or 12 or 21 });

        MarketQuoteRecord persistedHistory = new()
        {
            Symbol = security.StrategyCode,
            MarketType = "ETF",
            Source = MarketSources.TencentHistory,
            RawPayload = persistedPayload,
            ReceivedAt = "2026-07-21 09:30:00"
        };
        SecurityChartSnapshot reload = ChartDataService.BuildSnapshot(
            security,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { persistedHistory },
            null,
            null);

        Assert.Contains(reload.KLines, point => point.Date == new DateTime(2026, 7, 20) && !point.IsDisplayOnly);
    }

    [Fact]
    public async Task StaleIndexDailyTail_UsesEastMoneyAndPersistsMergedFormalHistory()
    {
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "Index");
        MarketQuoteRecord history = History(security, BusinessDaysEnding(new DateTime(2026, 7, 10), 800), MarketSources.EastMoneyHistory);
        var client = new TailChartMarketDataClient
        {
            EastMoneyResult = Result(security.EastMoneySecId, BusinessDaysEnding(new DateTime(2026, 7, 20), 320))
        };
        string? persistedPayload = null;
        var subscriptions = new ChartSubscriptionService();
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            new ChartCache(),
            client,
            historyCacheSaver: (_, _, _, payload, _) => persistedPayload = payload,
            nowProvider: () => new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.FromHours(8)));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { history }, CancellationToken.None);

        Assert.Equal(1, client.EastMoneyDailyRequests);
        Assert.Equal(0, client.TencentDailyRequests);
        Assert.NotNull(persistedPayload);
        Assert.Contains(EastMoneyHistoryParser.ParsePoints(persistedPayload!), point => point.Date == new DateTime(2026, 7, 20));
    }

    [Fact]
    public async Task TailRefreshFailure_PreservesExistingDeepHistoryAndDoesNotPersist()
    {
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "ETF");
        KLinePoint[] existing = BusinessDaysEnding(new DateTime(2026, 7, 10), 3000);
        var cache = new ChartCache();
        cache.SaveDailyKLines(security.StrategyCode, existing, new ChartDataStatus(true, "cache", true), BeijingMorning);
        var client = new TailChartMarketDataClient { TencentFailure = new InvalidOperationException("controlled") };
        var subscriptions = new ChartSubscriptionService();
        subscriptions.Subscribe(security, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        int saveCount = 0;
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            historyCacheSaver: (_, _, _, _, _) => saveCount++,
            nowProvider: () => BeijingMorning);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, client.TencentDailyRequests);
        Assert.Equal(0, saveCount);
        Assert.Equal(existing.Select(point => point.Date), cache.GetDailyKLines(security.StrategyCode)!.Points.Select(point => point.Date));
    }

    [Fact]
    public async Task BackgroundTailCatchUp_RequestsAtMostOneSymbolPerRoundThroughScheduler()
    {
        DateTimeOffset now = new(2026, 7, 21, 8, 0, 0, TimeSpan.FromHours(8));
        ChartSecurityInfo first = ChartDataService.CreateSecurityInfo("159509", "First");
        ChartSecurityInfo second = ChartDataService.CreateSecurityInfo("159941", "Second");
        var client = new TailChartMarketDataClient
        {
            TencentResult = Result("ETF", BusinessDaysEnding(new DateTime(2026, 7, 20), 320))
        };
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        scheduler.BeginTick(now);
        var coordinator = new ChartDataRefreshCoordinator(
            new ChartSubscriptionService(),
            new ChartCache(),
            client,
            scheduler: scheduler,
            nowProvider: () => now);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            new[]
            {
                History(first, BusinessDaysEnding(new DateTime(2026, 7, 10), 3000), MarketSources.TencentHistory),
                History(second, BusinessDaysEnding(new DateTime(2026, 7, 10), 3000), MarketSources.TencentHistory)
            },
            new[] { first, second },
            CancellationToken.None);

        Assert.Equal(1, client.TencentDailyRequests);
        Assert.Equal(1, scheduler.NonQuoteRequestsThisTick);
    }

    private static MarketQuoteRecord History(
        ChartSecurityInfo security,
        IEnumerable<KLinePoint> points,
        string source)
        => new()
        {
            Symbol = security.StrategyCode,
            RawCode = security.EastMoneySecId,
            MarketType = security.InstrumentType == ChartInstrumentType.Index ? "INDEX" : "ETF",
            Source = source,
            RawPayload = Payload(points),
            ReceivedAt = "2026-07-10 16:00:00"
        };

    private static MarketQuoteRecord Quote(string symbol, DateTime quoteTime, double price)
        => new()
        {
            Symbol = symbol,
            MarketType = "ETF",
            Source = MarketSources.Tencent,
            Price = price,
            LastClose = price - 1,
            OpenValue = price - 0.5,
            HighValue = price + 0.5,
            LowValue = price - 1,
            QuoteTime = quoteTime.ToString("yyyy-MM-dd HH:mm:ss"),
            ReceivedAt = quoteTime.ToString("yyyy-MM-dd HH:mm:ss")
        };

    private static KLinePoint[] BusinessDaysEnding(DateTime end, int count)
    {
        var dates = new List<DateTime>();
        DateTime cursor = end.Date;
        while (dates.Count < count)
        {
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(cursor);
            }

            cursor = cursor.AddDays(-1);
        }

        return dates
            .OrderBy(date => date)
            .Select((date, index) => Point(date, 100 + index * 0.01))
            .ToArray();
    }

    private static KLinePoint Point(DateTime date, double close, bool displayOnly = false, string? source = null)
        => new()
        {
            Date = date.Date,
            Open = close - 0.2,
            High = close + 0.5,
            Low = close - 0.5,
            Close = close,
            Volume = 1000,
            Amount = 2000,
            IsDisplayOnly = displayOnly,
            PointSource = source
        };

    private static string Payload(IEnumerable<KLinePoint> points)
        => TencentHistoryParser.ToEastMoneyCompatiblePayload(points.Select(point => new MarketHistoryPoint
        {
            Date = point.Date,
            Open = point.Open,
            Close = point.Close,
            High = point.High,
            Low = point.Low,
            Volume = point.Volume,
            Amount = point.Amount
        }));

    private static EastMoneyHistoryFetchResult Result(string secId, IEnumerable<KLinePoint> points)
    {
        KLinePoint[] materialized = points.ToArray();
        return new EastMoneyHistoryFetchResult
        {
            SecId = secId,
            Fqt = 1,
            Klt = 101,
            RawPayload = Payload(materialized),
            High = materialized.Max(point => point.High),
            PointCount = materialized.Length
        };
    }

    private sealed class TailChartMarketDataClient : IChartMarketDataClient
    {
        public EastMoneyHistoryFetchResult? TencentResult { get; init; }
        public EastMoneyHistoryFetchResult? EastMoneyResult { get; init; }
        public Exception? TencentFailure { get; init; }
        public int TencentDailyRequests { get; private set; }
        public int EastMoneyDailyRequests { get; private set; }

        public Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Intraday is outside this test boundary.");

        public Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Intraday is outside this test boundary.");

        public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
            string secId,
            bool isEtf,
            bool preferDaily,
            CancellationToken cancellationToken)
        {
            EastMoneyDailyRequests++;
            return Task.FromResult(EastMoneyResult ?? Result(secId, BusinessDaysEnding(new DateTime(2026, 7, 20), 320)));
        }

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(
            string tencentCode,
            CancellationToken cancellationToken)
        {
            TencentDailyRequests++;
            if (TencentFailure is not null)
            {
                throw TencentFailure;
            }

            return Task.FromResult(TencentResult ?? Result(tencentCode, BusinessDaysEnding(new DateTime(2026, 7, 20), 320)));
        }

        public void Dispose()
        {
        }
    }
}
