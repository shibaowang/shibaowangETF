using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class ChartHistoryDepthTests
{
    private static readonly DateTime LatestTradingDate = new(2026, 7, 10);
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void DepthEvaluator_ThreeHundredTwentyTradingDaysLeavesMonthlyHistoryInsufficient()
    {
        ChartHistoryDepthInfo depth = ChartHistoryDepthEvaluator.Evaluate(BusinessDaysEnding(320));

        Assert.Equal(320, depth.DailyCount);
        Assert.True(depth.MonthlyCount < ChartHistoryDepthEvaluator.MonthlyBasicTarget);
        Assert.False(depth.IsSufficientForMonthly);
    }

    [Fact]
    public void DepthEvaluator_ThreeThousandTradingDaysSupportsLongWeeklyAndMonthlyHistory()
    {
        ChartHistoryDepthInfo depth = ChartHistoryDepthEvaluator.Evaluate(BusinessDaysEnding(3000));

        Assert.Equal(3000, depth.DailyCount);
        Assert.True(depth.WeeklyCount >= ChartHistoryDepthEvaluator.WeeklyIdealTarget);
        Assert.True(depth.MonthlyCount >= ChartHistoryDepthEvaluator.MonthlyIdealTarget);
        Assert.True(depth.IsSufficientForWeekly);
        Assert.True(depth.IsSufficientForMonthly);
    }

    [Fact]
    public async Task MonthlyInsufficient_TriggersOneTencentDeepHistoryRequestAndAdoptsLongerResult()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new DepthChartMarketDataClient
        {
            TencentDepthResult = Result("sz159941", DailyPayload(BusinessDaysEnding(3000)))
        };
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);
        MarketQuoteRecord shortHistory = History(security, MarketSources.TencentHistory, BusinessDaysEnding(320));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { shortHistory }, CancellationToken.None);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), new[] { shortHistory }, CancellationToken.None);

        Assert.Equal(1, client.TencentDepthRequestCount);
        Assert.Equal(3000, client.LastTencentTargetPointCount);
        Assert.Equal(3000, cache.GetDailyKLines(security.StrategyCode)?.Points.Count);
        Assert.True(cache.GetSnapshot(security.StrategyCode, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume)?.HistoryDepth?.IsSufficientForMonthly);
    }

    [Fact]
    public async Task DeepHistoryFailure_KeepsOldCacheAndDoesNotRetrySameProcess()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new DepthChartMarketDataClient
        {
            TencentDepthFailure = new InvalidOperationException("controlled failure")
        };
        KLinePoint[] existing = BusinessDaysEnding(320);
        cache.SaveDailyKLines("159509", existing, new ChartDataStatus(true, "real cache", true), Now);
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159509", "纳指科技ETF景顺");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        ChartKLineCacheEntry? entry = cache.GetDailyKLines(security.StrategyCode);
        Assert.Equal(1, client.TencentDepthRequestCount);
        Assert.NotNull(entry);
        Assert.Equal(320, entry!.Points.Count);
        Assert.True(entry.Status.IsReady);
    }

    [Fact]
    public async Task SufficientMonthlyHistory_DoesNotRequestNetwork()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new DepthChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("513100", "纳指ETF国泰");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            new[] { History(security, MarketSources.TencentHistory, BusinessDaysEnding(3000)) },
            CancellationToken.None);

        Assert.Equal(0, client.TencentDepthRequestCount);
        Assert.Equal(0, client.EastMoneyHistoryRequestCount);
    }

    [Fact]
    public async Task IndexInsufficient_UsesEastMoneyDailyAndPersistsSourceExhaustedCheckpoint()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new DepthChartMarketDataClient
        {
            EastMoneyResult = Result("100.NDX100", DailyPayload(BusinessDaysEnding(800)), sourceExhausted: true)
        };
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? persistedMarketType = null;
        string? persistedSource = null;
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            historyCacheSaver: (_, marketType, _, _, source) =>
            {
                persistedMarketType = marketType;
                persistedSource = source;
            },
            nowProvider: () => Now,
            historyDepthCheckpointReader: key => settings.GetValueOrDefault(key),
            historyDepthCheckpointWriter: (key, value) => settings[key] = value);
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            new[] { History(security, MarketSources.EastMoneyHistory, BusinessDaysEnding(320)) },
            CancellationToken.None);

        Assert.Equal(1, client.EastMoneyHistoryRequestCount);
        Assert.Equal(0, client.TencentDepthRequestCount);
        Assert.Equal("INDEX", persistedMarketType);
        Assert.Equal(MarketSources.EastMoneyHistory, persistedSource);
        ChartHistoryDepthCheckpoint checkpoint = Assert.Single(settings.Values
            .Select(ChartHistoryDepthEvaluator.ParseCheckpoint)
            .OfType<ChartHistoryDepthCheckpoint>());
        Assert.True(checkpoint.SourceExhausted);
        Assert.Equal(800, checkpoint.DailyCount);
    }

    [Fact]
    public async Task RecentSourceExhaustedCheckpoint_PreventsRestartRetryWhenCurrentHistoryMatches()
    {
        KLinePoint[] existing = BusinessDaysEnding(320);
        ChartHistoryDepthInfo depth = ChartHistoryDepthEvaluator.Evaluate(existing);
        string checkpointValue = ChartHistoryDepthEvaluator.SerializeCheckpoint(new ChartHistoryDepthCheckpoint(
            MarketSources.TencentHistory,
            depth.DailyCount,
            depth.EarliestDate,
            depth.LatestDate,
            true,
            Now.AddDays(-1)));
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var client = new DepthChartMarketDataClient();
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now,
            historyDepthCheckpointReader: _ => checkpointValue);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159660", "纳指ETF汇添富");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            new[] { History(security, MarketSources.TencentHistory, existing) },
            CancellationToken.None);

        Assert.Equal(0, client.TencentDepthRequestCount);
        Assert.Contains(
            "全部可用历史",
            cache.GetSnapshot(security.StrategyCode, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume)?.MainStatus.Message ?? string.Empty);
    }

    [Fact]
    public void SourceExhaustedCheckpoint_DoesNotHideMissingOrRegressedCache()
    {
        ChartHistoryDepthInfo checkedDepth = ChartHistoryDepthEvaluator.Evaluate(BusinessDaysEnding(320));
        var checkpoint = new ChartHistoryDepthCheckpoint(
            MarketSources.TencentHistory,
            checkedDepth.DailyCount,
            checkedDepth.EarliestDate,
            checkedDepth.LatestDate,
            true,
            Now.AddDays(-1));

        ChartHistoryDepthInfo missing = ChartHistoryDepthEvaluator.Evaluate(Array.Empty<KLinePoint>());
        ChartHistoryDepthInfo regressed = ChartHistoryDepthEvaluator.Evaluate(BusinessDaysEnding(200));

        Assert.False(ChartHistoryDepthEvaluator.ShouldSkipExhaustedSource(missing, checkpoint, Now));
        Assert.False(ChartHistoryDepthEvaluator.ShouldSkipExhaustedSource(regressed, checkpoint, Now));
    }

    [Fact]
    public async Task MonthlyLikeDeepResult_DoesNotReplaceDailyLikeCache()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        KLinePoint[] existing = BusinessDaysEnding(320);
        cache.SaveDailyKLines("159513", existing, new ChartDataStatus(true, "real cache", true), Now);
        var client = new DepthChartMarketDataClient
        {
            TencentDepthResult = Result("sz159513", MonthlyPayload(100))
        };
        var coordinator = new ChartDataRefreshCoordinator(
            subscriptions,
            cache,
            client,
            nowProvider: () => Now);
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("159513", "纳指大成");
        subscriptions.Subscribe(security, SecurityChartPeriod.Monthly, SecurityChartSubPanel.Volume);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(320, cache.GetDailyKLines(security.StrategyCode)?.Points.Count);
        Assert.Equal(1, client.TencentDepthRequestCount);
    }

    [Fact]
    public void ReplacementGuard_RejectsShorterRollingOrInvalidSequencesAndAcceptsDeeperCurrentHistory()
    {
        KLinePoint[] existing = BusinessDaysEnding(800);
        KLinePoint[] shorter = BusinessDaysEnding(320);
        KLinePoint[] deeper = BusinessDaysEnding(1200);
        KLinePoint[] duplicate = BusinessDaysEnding(320).Append(Clone(BusinessDaysEnding(320)[^1])).ToArray();
        KLinePoint[] invalid = BusinessDaysEnding(320);
        invalid[10].High = invalid[10].Low - 1;

        Assert.False(ChartHistoryDepthEvaluator.DecideReplacement(existing, shorter).ShouldReplace);
        Assert.False(ChartHistoryDepthEvaluator.DecideReplacement(existing, duplicate).ShouldReplace);
        Assert.False(ChartHistoryDepthEvaluator.DecideReplacement(existing, invalid).ShouldReplace);
        Assert.True(ChartHistoryDepthEvaluator.DecideReplacement(existing, deeper).ShouldReplace);
    }

    [Fact]
    public void PersistentCacheGuard_RejectsShorterRollingDailyPayload()
    {
        MarketHistoryQualityInfo oldQuality = MarketHistoryQuality.Analyze(DailyPayload(BusinessDaysEnding(800)));
        MarketHistoryQualityInfo newQuality = MarketHistoryQuality.Analyze(DailyPayload(BusinessDaysEnding(320)));

        MarketHistoryOverwriteDecision decision = MarketHistoryQuality.DecideOverwrite(
            "159941",
            oldQuality,
            newQuality,
            isCoreIndex: false);

        Assert.False(decision.AllowOverwrite);
        Assert.Equal("SKIP_HISTORY_SHRINK", decision.Code);
    }

    [Fact]
    public void SnapshotSelection_PrefersLongerPersistedDailyHistoryOverNewerShortMemoryWindow()
    {
        ChartSecurityInfo security = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳斯达克科技指数");
        KLinePoint[] longHistory = BusinessDaysEnding(800);
        var memory = new ChartKLineCacheEntry(
            BusinessDaysEnding(320),
            new ChartDataStatus(true, "newer short memory", true),
            Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            security,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { History(security, MarketSources.EastMoneyHistory, longHistory) },
            null,
            memory);

        Assert.Equal(800, snapshot.HistoryDepth?.DailyCount);
        Assert.Equal(ChartHistoryDepthEvaluator.Evaluate(longHistory).MonthlyCount, snapshot.KLines.Count);
    }

    [Fact]
    public void SnapshotSelection_EtfPrefersTencentDailyLikeOverLegacyEastMoneyPayload()
    {
        ChartSecurityInfo security = ChartDataService.CreateSecurityInfo("513100", "纳指ETF国泰");
        MarketQuoteRecord legacyEastMoney = History(security, MarketSources.EastMoneyHistory, BusinessDaysEnding(1200));
        MarketQuoteRecord currentTencent = History(security, MarketSources.TencentHistory, BusinessDaysEnding(800));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            security,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { legacyEastMoney, currentTencent },
            null,
            null);

        Assert.Equal(800, snapshot.HistoryDepth?.DailyCount);
    }

    [Fact]
    public void TencentDeepHistory_UsesSerialThreeHundredTwentyPointPagesAndExistingRequestKind()
    {
        string clientSource = ReadRepositoryFile(Path.Combine("Infrastructure", "Market", "MarketDataClient.cs"));

        Assert.Contains("TencentDailyPageSize = 320", clientSource, StringComparison.Ordinal);
        Assert.Contains("WaitForTencentHistoryPageSlotAsync", clientSource, StringComparison.Ordinal);
        Assert.Contains("_scheduler.TryAcquireRaw", clientSource, StringComparison.Ordinal);
        Assert.Contains("overlapCount == 0", clientSource, StringComparison.Ordinal);
        Assert.Contains("!SameOhlc(existing, point)", clientSource, StringComparison.Ordinal);
        Assert.DoesNotContain(",week", clientSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(",month", clientSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Enum.GetNames<MarketRequestKind>(), name => name.Contains("Deep", StringComparison.OrdinalIgnoreCase));
    }

    private static MarketQuoteRecord History(
        ChartSecurityInfo security,
        string source,
        IReadOnlyList<KLinePoint> points)
        => new()
        {
            Symbol = security.StrategyCode,
            RawCode = security.EastMoneySecId,
            MarketType = security.InstrumentType == ChartInstrumentType.Index ? "INDEX" : "ETF",
            Source = source,
            RawPayload = DailyPayload(points),
            ReceivedAt = "2026-07-12 09:00:00"
        };

    private static EastMoneyHistoryFetchResult Result(
        string symbol,
        string payload,
        bool sourceExhausted = false)
        => new()
        {
            SecId = symbol,
            Fqt = 1,
            Klt = 101,
            RawPayload = payload,
            PointCount = EastMoneyHistoryParser.ParsePoints(payload).Count,
            IsSourceExhausted = sourceExhausted
        };

    private static KLinePoint[] BusinessDaysEnding(int count)
    {
        var dates = new List<DateTime>(count);
        DateTime date = LatestTradingDate;
        while (dates.Count < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(date);
            }

            date = date.AddDays(-1);
        }

        dates.Reverse();
        return dates.Select((item, index) => new KLinePoint
        {
            Date = item,
            Open = 100 + index * 0.01,
            High = 101 + index * 0.01,
            Low = 99 + index * 0.01,
            Close = 100.5 + index * 0.01,
            Volume = 1000 + index,
            Amount = 2000 + index
        }).ToArray();
    }

    private static string DailyPayload(IEnumerable<KLinePoint> points)
    {
        string[] lines = points.Select(point =>
            $"\"{point.Date:yyyy-MM-dd},{point.Open.ToString("F4", CultureInfo.InvariantCulture)},{point.Close.ToString("F4", CultureInfo.InvariantCulture)},{point.High.ToString("F4", CultureInfo.InvariantCulture)},{point.Low.ToString("F4", CultureInfo.InvariantCulture)},{(point.Volume ?? 0).ToString("F0", CultureInfo.InvariantCulture)},{(point.Amount ?? 0).ToString("F0", CultureInfo.InvariantCulture)}\"").ToArray();
        return "{\"data\":{\"klines\":[" + string.Join(",", lines) + "]}}";
    }

    private static string MonthlyPayload(int count)
    {
        KLinePoint[] points = Enumerable.Range(0, count)
            .Select(index => new KLinePoint
            {
                Date = new DateTime(2018, 4, 1).AddMonths(index),
                Open = 100 + index,
                High = 102 + index,
                Low = 99 + index,
                Close = 101 + index,
                Volume = 1000
            })
            .ToArray();
        return DailyPayload(points);
    }

    private static KLinePoint Clone(KLinePoint point)
        => new()
        {
            Date = point.Date,
            Open = point.Open,
            High = point.High,
            Low = point.Low,
            Close = point.Close,
            Volume = point.Volume,
            Amount = point.Amount
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

    private sealed class DepthChartMarketDataClient : IChartMarketDataClient
    {
        public EastMoneyHistoryFetchResult? TencentDepthResult { get; init; }
        public EastMoneyHistoryFetchResult? EastMoneyResult { get; init; }
        public Exception? TencentDepthFailure { get; init; }
        public int TencentDepthRequestCount { get; private set; }
        public int EastMoneyHistoryRequestCount { get; private set; }
        public int LastTencentTargetPointCount { get; private set; }

        public Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
            string secId,
            bool isEtf,
            bool preferDaily,
            CancellationToken cancellationToken)
        {
            EastMoneyHistoryRequestCount++;
            return Task.FromResult(EastMoneyResult ?? Result(secId, DailyPayload(BusinessDaysEnding(800)), true));
        }

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken)
            => Task.FromResult(Result(tencentCode, DailyPayload(BusinessDaysEnding(320))));

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryDepthAsync(
            string tencentCode,
            int targetPointCount,
            CancellationToken cancellationToken)
        {
            TencentDepthRequestCount++;
            LastTencentTargetPointCount = targetPointCount;
            return TencentDepthFailure is not null
                ? Task.FromException<EastMoneyHistoryFetchResult>(TencentDepthFailure)
                : Task.FromResult(TencentDepthResult ?? Result(tencentCode, DailyPayload(BusinessDaysEnding(3000))));
        }

        public void Dispose()
        {
        }
    }
}
