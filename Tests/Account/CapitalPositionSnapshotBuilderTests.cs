using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Account;

public sealed class CapitalPositionSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Build_UsesPersistedAccountTotalsWithoutRecalculatingThem()
    {
        AccountReplayStateRecord account = Account();
        account.TotalAssets = 987_654.32;
        account.KnownMarketValue = 876_543.21;
        account.TotalUnrealizedPnl = 4321.12;
        account.TotalRealizedPnl = -321.45;
        CapitalPositionReadModel model = Model(
            account,
            positions: new[] { Etf("S1", "159941", 100, 2, 200) },
            otcPositions: new[] { Otc("S1", "017091", 100, 1.5, 150) });

        CapitalPositionSnapshot snapshot = Build(model);

        Assert.Equal(987_654.32, snapshot.Summary.TotalAssets);
        Assert.Equal(876_543.21, snapshot.Summary.KnownMarketValue);
        Assert.Equal(4321.12, snapshot.Summary.TotalUnrealizedPnl);
        Assert.Equal(-321.45, snapshot.Summary.TotalRealizedPnl);
        Assert.NotEqual(snapshot.Summary.EtfMarketValue + snapshot.Summary.OtcMarketValue, snapshot.Summary.KnownMarketValue);
    }

    [Fact]
    public void Build_FundsUsageReadsPositionRatioAndNeverUsesBasePositionRatio()
    {
        AccountReplayStateRecord account = Account();
        account.PositionRatio = 0.4567;
        account.BasePositionRatio = 0.20;

        CapitalPositionSnapshot snapshot = Build(Model(account));

        Assert.Equal(0.4567, snapshot.Summary.PositionRatio);
        Assert.Equal("45.67%", snapshot.Summary.PositionRatioText);
        Assert.NotEqual(account.BasePositionRatio, snapshot.Summary.PositionRatio);
    }

    [Fact]
    public void Build_UsesLatestDecisionValuesAndKeepsCompletionTextAboveOneHundredPercent()
    {
        var decision = new StrategyDecisionStateRecord
        {
            RealSniperPool = 12_345.67,
            BaseCompletionRate = 1.2635
        };

        CapitalPositionSnapshot snapshot = Build(Model(Account(), decision: decision));

        Assert.Equal(12_345.67, snapshot.Summary.RealSniperPool);
        Assert.Equal("12,345.67", snapshot.Summary.RealSniperPoolText);
        Assert.Equal(1.2635, snapshot.Summary.BaseCompletionRate);
        Assert.Equal("126.35%", snapshot.Summary.BaseCompletionRateText);
        Assert.Equal(1, snapshot.Summary.BaseCompletionProgress);
    }

    [Fact]
    public void Build_MissingDecisionDoesNotFallBackToCashOrBasePositionRatio()
    {
        AccountReplayStateRecord account = Account();
        account.CashBalance = 99_999;
        account.BasePositionRatio = 0.77;

        CapitalPositionSnapshot snapshot = Build(Model(account));

        Assert.Null(snapshot.Summary.RealSniperPool);
        Assert.Null(snapshot.Summary.BaseCompletionRate);
        Assert.Equal("--", snapshot.Summary.RealSniperPoolText);
        Assert.Equal("--", snapshot.Summary.BaseCompletionRateText);
    }

    [Theory]
    [InlineData(false, "正常")]
    [InlineData(true, "估值不完整")]
    public void Build_IncompleteValuationKeepsKnownAmountsButHidesEveryRatio(bool completeFlag, string replayStatus)
    {
        AccountReplayStateRecord account = Account();
        account.MarketValueComplete = completeFlag;
        account.ReplayStatus = replayStatus;
        account.TotalAssets = 1000;
        account.CashRatio = 0.4;
        account.PositionRatio = 0.6;

        CapitalPositionSnapshot snapshot = Build(Model(
            account,
            positions: new[] { Etf("S1", "159941", 100, 2, 200) },
            otcPositions: new[] { Otc("S1", "017091", 100, 1, 100) }));

        Assert.False(snapshot.IsValuationComplete);
        Assert.Equal(1000, snapshot.Summary.TotalAssets);
        Assert.Equal("1,000.00", snapshot.Summary.TotalAssetsText);
        Assert.Equal("--", snapshot.Summary.PositionRatioText);
        Assert.Equal("--", snapshot.Summary.CashRatioText);
        Assert.Equal("--", snapshot.Summary.EtfRatioText);
        Assert.Equal("--", snapshot.Summary.OtcRatioText);
        Assert.All(snapshot.StrategyRows, row => Assert.Equal("--", row.AssetRatioText));
    }

    [Fact]
    public void Build_NegativeCashRemainsVisibleAndProgressOnlyClampsTheGraphic()
    {
        AccountReplayStateRecord account = Account();
        account.CashBalance = -123.45;
        account.CashRatio = -0.10;

        CapitalPositionSnapshot snapshot = Build(Model(account));

        Assert.Equal(-123.45, snapshot.Summary.CashBalance);
        Assert.Equal("-123.45", snapshot.Summary.CashBalanceText);
        Assert.Equal(-0.10, snapshot.Summary.CashRatio);
        Assert.Equal("-10.00%", snapshot.Summary.CashRatioText);
        Assert.Equal(0, snapshot.Summary.CashRatioProgress);
        Assert.Equal("#F5A623", snapshot.Summary.CashBalanceColor);
    }

    [Fact]
    public void Build_ZeroAssetsNeverDividesAndShowsMissingRatios()
    {
        AccountReplayStateRecord account = Account();
        account.TotalAssets = 0;

        CapitalPositionSnapshot snapshot = Build(Model(account, positions: new[] { Etf("S1", "159941", 100, 2, 200) }));

        Assert.Null(snapshot.Summary.PositionRatio);
        Assert.Equal("--", snapshot.Summary.PositionRatioText);
        Assert.Equal("--", Assert.Single(snapshot.EtfRows).AssetRatioText);
    }

    [Fact]
    public void Build_MissingAccountReturnsSafeEmptySnapshotAndIgnoresOrphanRows()
    {
        CapitalPositionSnapshot snapshot = Build(Model(
            null,
            positions: new[] { Etf("S1", "159941", 100, 2, 200) },
            otcPositions: new[] { Otc("S1", "017091", 100, 1, 100) }));

        Assert.False(snapshot.HasAccount);
        Assert.Equal("暂无账户回放结果", snapshot.AccountStatusText);
        Assert.Empty(snapshot.EtfRows);
        Assert.Empty(snapshot.OtcRows);
        Assert.Equal("--", snapshot.Summary.TotalAssetsText);
    }

    [Fact]
    public void Build_PreservesFullReplayErrorAndProvidesCompactSummary()
    {
        AccountReplayStateRecord account = Account();
        account.ReplayError = "第一行错误\n第二行完整详情";

        CapitalPositionSnapshot snapshot = Build(Model(account));

        Assert.Equal(account.ReplayError, snapshot.ReplayError);
        Assert.Equal("第一行错误", snapshot.ReplayErrorSummary);
    }

    [Fact]
    public void Build_OtcCopyInPositionReplayIsExcludedFromEtfAndNeverDoubleCounted()
    {
        PositionReplayStateRecord exchange = Etf("S1", "159941", 100, 2, 200);
        PositionReplayStateRecord otcCopy = Etf("S1", "017091", 100, 1, 100);
        otcCopy.Source = "场外替代";
        OtcPositionReplayStateRecord otc = Otc("S1", "017091", 100, 1, 100);
        AccountReplayStateRecord account = Account();
        account.KnownMarketValue = 300;

        CapitalPositionSnapshot snapshot = Build(Model(account, new[] { exchange, otcCopy }, new[] { otc }));

        Assert.Equal(200, snapshot.Summary.EtfMarketValue);
        Assert.Equal(100, snapshot.Summary.OtcMarketValue);
        Assert.Equal(300, snapshot.Summary.KnownMarketValue);
        Assert.Single(snapshot.EtfRows);
        Assert.Single(snapshot.OtcRows);
        Assert.Equal(300, Assert.Single(snapshot.StrategyRows).TotalMarketValue);
    }

    [Fact]
    public void Build_MultipleOtcRowsRemainIndependentAcrossFundsAndStrategies()
    {
        OtcPositionReplayStateRecord first = Otc("S1", "017091", 100, 1.2, 120);
        OtcPositionReplayStateRecord second = Otc("S1", "017093", 200, 1.3, 260);
        OtcPositionReplayStateRecord third = Otc("S2", "017091", 300, 1.4, 420);

        CapitalPositionSnapshot snapshot = Build(Model(Account(), otcPositions: new[] { first, second, third }));

        Assert.Equal(3, snapshot.OtcRows.Count);
        Assert.Equal(2, snapshot.StrategyRows.Count);
        Assert.Equal(380, snapshot.StrategyRows.Single(row => row.StrategyCode == "S1").OtcMarketValue);
        Assert.Equal(420, snapshot.StrategyRows.Single(row => row.StrategyCode == "S2").OtcMarketValue);
    }

    [Fact]
    public void Build_SameEtfAcrossStrategiesRemainsTwoRowsWithIndependentValues()
    {
        PositionReplayStateRecord first = Etf("S1", "159941", 100, 1.5, 150);
        PositionReplayStateRecord second = Etf("S2", "159941", 200, 1.6, 320);

        CapitalPositionSnapshot snapshot = Build(Model(Account(), positions: new[] { first, second }));

        Assert.Equal(2, snapshot.EtfRows.Count);
        Assert.Contains(snapshot.EtfRows, row => row.StrategyCode == "S1" && row.MarketValue == 150);
        Assert.Contains(snapshot.EtfRows, row => row.StrategyCode == "S2" && row.MarketValue == 320);
    }

    [Fact]
    public void Build_EtfNameUsesStrategyCodeAndMissingConfigurationStaysMissing()
    {
        var strategies = new[] { new StrategyConfigRecord { Code = "S1", Name = "策略一", Enabled = true } };

        CapitalPositionSnapshot snapshot = Build(Model(
            Account(),
            positions: new[] { Etf("S1", "159941", 1, 2, 2), Etf("UNKNOWN", "159509", 1, 3, 3) },
            strategies: strategies));

        Assert.Equal("策略一", snapshot.EtfRows.Single(row => row.StrategyCode == "S1").EtfName);
        Assert.Equal("--", snapshot.EtfRows.Single(row => row.StrategyCode == "UNKNOWN").EtfName);
    }

    [Fact]
    public void Build_EtfAccountingValuesComeDirectlyFromReplay()
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 123.4567, 1.234567, 152.41);
        position.AverageCost = 1.111111;
        position.UnrealizedPnl = 15.55;
        position.ReturnRate = 0.1133;
        MarketQuoteRecord quote = Quote("159941", "ETF", MarketSources.Tencent, 1.234567, 5);
        quote.LastClose = 0.5;

        CapitalPositionEtfRow row = Assert.Single(Build(Model(Account(), new[] { position }, quotes: new[] { quote })).EtfRows);

        Assert.Equal(position.Quantity, row.Quantity);
        Assert.Equal(position.AverageCost, row.AverageCost);
        Assert.Equal(position.MarketPrice, row.MarketPrice);
        Assert.Equal(position.MarketValue, row.MarketValue);
        Assert.Equal(position.UnrealizedPnl, row.UnrealizedPnl);
        Assert.Equal(position.ReturnRate, row.ReturnRate);
    }

    [Fact]
    public void Build_OtcValuesAndChannelPriorityUseExactStrategyAndFundPair()
    {
        OtcPositionReplayStateRecord position = Otc("S2", "017091", 123.4567, 1.234567, 152.41);
        position.CostAmount = 140.25;
        position.AverageCost = 1.136;
        position.UnrealizedPnl = 12.16;
        position.ReturnRate = 0.0867;
        var channels = new[]
        {
            new OtcChannelRecord { StrategyCode = "S1", OtcCode = "017091", Priority = 1, Enabled = true },
            new OtcChannelRecord { StrategyCode = "S2", OtcCode = "017091", Priority = 7, Enabled = true }
        };
        MarketQuoteRecord quote = Quote("017091", "OTC", MarketSources.SinaFund, 1.234567, 5);
        quote.DisplayName = "真实基金名称";

        CapitalPositionOtcRow row = Assert.Single(Build(Model(
            Account(), otcPositions: new[] { position }, channels: channels, quotes: new[] { quote })).OtcRows);

        Assert.Equal(position.Quantity, row.Quantity);
        Assert.Equal(position.CostAmount, row.CostAmount);
        Assert.Equal(position.AverageCost, row.AverageCost);
        Assert.Equal(position.Nav, row.Nav);
        Assert.Equal(7, row.ChannelPriority);
        Assert.Equal("真实基金名称", row.FundName);
    }

    [Fact]
    public void Build_MissingOtcQuoteKeepsReplayNavAndUsesMissingMetadata()
    {
        OtcPositionReplayStateRecord position = Otc("S1", "017091", 100, 1.5, 150);

        CapitalPositionOtcRow row = Assert.Single(Build(Model(Account(), otcPositions: new[] { position })).OtcRows);

        Assert.Equal(1.5, row.Nav);
        Assert.Equal("1.5", row.NavText);
        Assert.Equal("--", row.FundName);
        Assert.Equal("--", row.QuoteSourceText);
        Assert.Equal("未关联", row.CacheStatus);
    }

    [Fact]
    public void Build_QuoteSelectionPrefersLockedSourceAmongPriceMatchingCandidates()
    {
        PositionReplayStateRecord etf = Etf("S1", "159941", 100, 1.65, 165);
        OtcPositionReplayStateRecord otc = Otc("S1", "017091", 100, 1.2, 120);
        MarketQuoteRecord etfFallback = Quote("159941", "ETF", "OTHER_REAL", 1.65, 1);
        MarketQuoteRecord etfPreferred = Quote("159941", "ETF", MarketSources.Tencent, 1.65, 20);
        MarketQuoteRecord otcFallback = Quote("017091", "OTC", "OTHER_REAL", 1.2, 1);
        MarketQuoteRecord otcPreferred = Quote("017091", "OTC", MarketSources.SinaFund, 1.2, 20);

        CapitalPositionSnapshot snapshot = Build(Model(
            Account(),
            new[] { etf },
            new[] { otc },
            quotes: new[] { etfFallback, etfPreferred, otcFallback, otcPreferred }));

        Assert.Equal(MarketSources.Tencent, Assert.Single(snapshot.EtfRows).QuoteSource);
        Assert.Equal(MarketSources.SinaFund, Assert.Single(snapshot.OtcRows).QuoteSource);
    }

    [Fact]
    public void Build_PriceMismatchNeverAttachesNewerMetadataOrRevaluesAccountingAmounts()
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 100, 1.65, 165);
        position.UnrealizedPnl = 20;
        MarketQuoteRecord newer = Quote("159941", "ETF", MarketSources.Tencent, 1.70, 1);

        CapitalPositionEtfRow row = Assert.Single(Build(Model(Account(), new[] { position }, quotes: new[] { newer })).EtfRows);

        Assert.Equal(1.65, row.MarketPrice);
        Assert.Equal(165, row.MarketValue);
        Assert.Equal(20, row.UnrealizedPnl);
        Assert.Equal("--", row.QuoteSourceText);
        Assert.Equal("未关联", row.CacheStatus);
    }

    [Fact]
    public void Build_RejectsMockFakeAndSimulatedQuoteSources()
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 100, 1.65, 165);
        MarketQuoteRecord mock = Quote("159941", "ETF", "MOCK_SOURCE", 1.65, 1);
        MarketQuoteRecord fake = Quote("159941", "ETF", "FAKE_SOURCE", 1.65, 1);
        MarketQuoteRecord simulated = Quote("159941", "ETF", "SIMULATED_SOURCE", 1.65, 1);

        CapitalPositionEtfRow row = Assert.Single(Build(Model(
            Account(), new[] { position }, quotes: new[] { mock, fake, simulated })).EtfRows);

        Assert.Equal("未关联", row.CacheStatus);
        Assert.Equal("--", row.QuoteSourceText);
    }

    [Theory]
    [InlineData("TENCENT_QT", 30, "正常")]
    [InlineData("TENCENT_QT", 31, "延迟")]
    [InlineData("TENCENT_QT", 121, "过期")]
    [InlineData("SINA_FUND", 300, "正常")]
    [InlineData("SINA_FUND", 301, "延迟")]
    [InlineData("SINA_FUND", 901, "过期")]
    [InlineData("OTHER_REAL", 60, "正常")]
    [InlineData("OTHER_REAL", 61, "延迟")]
    [InlineData("OTHER_REAL", 301, "过期")]
    public void Build_FreshnessMatchesV870ReceivedAtRules(string source, int ageSeconds, string expected)
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 100, 1.65, 165);
        MarketQuoteRecord quote = Quote("159941", "ETF", source, 1.65, ageSeconds);

        CapitalPositionEtfRow row = Assert.Single(Build(Model(Account(), new[] { position }, quotes: new[] { quote })).EtfRows);

        Assert.Equal(expected, row.CacheStatus);
        Assert.Equal(expected, MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, Now));
    }

    [Fact]
    public void Build_InvalidReceivedAtKeepsMatchedSourceButMarksTimeInvalid()
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 100, 1.65, 165);
        MarketQuoteRecord quote = Quote("159941", "ETF", MarketSources.Tencent, 1.65, 5);
        quote.ReceivedAt = string.Empty;

        CapitalPositionEtfRow row = Assert.Single(Build(Model(Account(), new[] { position }, quotes: new[] { quote })).EtfRows);

        Assert.Equal(MarketSources.Tencent, row.QuoteSource);
        Assert.Equal("--", row.ReceivedAtText);
        Assert.Equal("时间无效", row.CacheStatus);
    }

    [Fact]
    public void Build_FormatsMoneyQuantityPriceRatioAndMissingWithoutChangingValues()
    {
        PositionReplayStateRecord position = Etf("S1", "159941", 1234.56789, 1.88355000000000147, 2324.45);
        position.UnrealizedPnl = null;
        position.ReturnRate = 0.123456;

        CapitalPositionEtfRow row = Assert.Single(Build(Model(Account(), new[] { position })).EtfRows);

        Assert.Equal(1234.56789, row.Quantity);
        Assert.Equal("1,234.5679", row.QuantityText);
        Assert.Equal("1.88355", row.MarketPriceText);
        Assert.Equal("2,324.45", row.MarketValueText);
        Assert.Equal("12.35%", row.ReturnRateText);
        Assert.Equal("--", row.UnrealizedPnlText);
    }

    private static CapitalPositionSnapshot Build(CapitalPositionReadModel model)
        => new CapitalPositionSnapshotBuilder().Build(model, Now);

    private static CapitalPositionReadModel Model(
        AccountReplayStateRecord? account,
        IReadOnlyList<PositionReplayStateRecord>? positions = null,
        IReadOnlyList<OtcPositionReplayStateRecord>? otcPositions = null,
        IReadOnlyList<StrategyConfigRecord>? strategies = null,
        IReadOnlyList<OtcChannelRecord>? channels = null,
        IReadOnlyList<MarketQuoteRecord>? quotes = null,
        StrategyDecisionStateRecord? decision = null)
        => new()
        {
            Account = account,
            Positions = positions ?? Array.Empty<PositionReplayStateRecord>(),
            OtcPositions = otcPositions ?? Array.Empty<OtcPositionReplayStateRecord>(),
            Strategies = strategies ?? Array.Empty<StrategyConfigRecord>(),
            OtcChannels = channels ?? Array.Empty<OtcChannelRecord>(),
            Quotes = quotes ?? Array.Empty<MarketQuoteRecord>(),
            LatestDecision = decision,
            ReadAt = Now
        };

    private static AccountReplayStateRecord Account()
        => new()
        {
            Id = 1,
            CalculatedAt = "2026-07-15 09:59:58",
            ReplayStatus = "正常",
            CashBalance = 400,
            Principal = 1000,
            KnownMarketValue = 600,
            TotalAssets = 1000,
            TotalRealizedPnl = 50,
            TotalUnrealizedPnl = 75,
            CashRatio = 0.4,
            PositionRatio = 0.6,
            BasePositionRatio = 0.2,
            MarketValueComplete = true
        };

    private static PositionReplayStateRecord Etf(
        string strategyCode,
        string actualCode,
        double quantity,
        double price,
        double marketValue)
        => new()
        {
            Id = Math.Abs(HashCode.Combine(strategyCode, actualCode, quantity)),
            CalculatedAt = "2026-07-15 09:59:58",
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = "场内ETF",
            Quantity = quantity,
            CostAmount = marketValue - 10,
            AverageCost = price - 0.1,
            MarketPrice = price,
            MarketValue = marketValue,
            UnrealizedPnl = 10,
            ReturnRate = 0.05,
            QuoteStatus = "真实行情"
        };

    private static OtcPositionReplayStateRecord Otc(
        string strategyCode,
        string actualCode,
        double quantity,
        double nav,
        double marketValue)
        => new()
        {
            Id = Math.Abs(HashCode.Combine(strategyCode, actualCode, quantity)),
            CalculatedAt = "2026-07-15 09:59:58",
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Quantity = quantity,
            CostAmount = marketValue - 10,
            AverageCost = nav - 0.1,
            Nav = nav,
            MarketValue = marketValue,
            UnrealizedPnl = 10,
            ReturnRate = 0.05,
            QuoteStatus = "真实净值"
        };

    private static MarketQuoteRecord Quote(
        string symbol,
        string marketType,
        string source,
        double price,
        int ageSeconds)
        => new()
        {
            Id = ageSeconds,
            Symbol = symbol,
            DisplayName = symbol + " 名称",
            MarketType = marketType,
            Source = source,
            Price = price,
            QuoteTime = Now.AddSeconds(-ageSeconds).ToString("yyyy-MM-dd HH:mm:ss"),
            ReceivedAt = Now.AddSeconds(-ageSeconds).ToString("yyyy-MM-dd HH:mm:ss")
        };
}
