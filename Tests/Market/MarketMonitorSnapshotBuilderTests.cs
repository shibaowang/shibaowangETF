using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class MarketMonitorSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(8));

    public static IEnumerable<object[]> FixedTopItems()
        => MarketSymbolNormalizer.DefaultTopBarItems()
            .Select(item => new object[] { item.Symbol, item.DisplayName, item.MarketType });

    public static IEnumerable<object[]> FreshnessCases()
    {
        yield return Case(MarketSources.Tencent, 0, "正常");
        yield return Case(MarketSources.Tencent, 30, "正常");
        yield return Case(MarketSources.Tencent, 31, "延迟");
        yield return Case(MarketSources.Tencent, 120, "延迟");
        yield return Case(MarketSources.Tencent, 121, "过期");
        yield return Case(MarketSources.EastMoney, 60, "正常");
        yield return Case(MarketSources.EastMoney, 61, "延迟");
        yield return Case(MarketSources.EastMoney, 180, "延迟");
        yield return Case(MarketSources.EastMoney, 181, "过期");
        yield return Case(MarketSources.SinaFund, 300, "正常");
        yield return Case(MarketSources.SinaFund, 301, "延迟");
        yield return Case(MarketSources.SinaFund, 900, "延迟");
        yield return Case(MarketSources.SinaFund, 901, "过期");
        yield return Case("OTHER_REAL_SOURCE", 60, "正常");
        yield return Case("OTHER_REAL_SOURCE", 61, "延迟");
        yield return Case("OTHER_REAL_SOURCE", 300, "延迟");
        yield return Case("OTHER_REAL_SOURCE", 301, "过期");
    }

    public static IEnumerable<object[]> AgeCases()
    {
        yield return new object[] { 8, "8秒" };
        yield return new object[] { 60, "1分" };
        yield return new object[] { 85, "1分25秒" };
        yield return new object[] { 3600, "1小时" };
        yield return new object[] { 7980, "2小时13分" };
    }

    public static IEnumerable<object[]> SourceStatusCases()
    {
        yield return new object?[] { "OK", "正常", true }!;
        yield return new object?[] { "RATE_LIMIT", "限频", false }!;
        yield return new object?[] { "COOLDOWN", "熔断冷却", false }!;
        yield return new object?[] { "ERROR", "异常", false }!;
        yield return new object?[] { null, "未记录", false }!;
        yield return new object?[] { "CUSTOM", "CUSTOM", false }!;
    }

    public static IEnumerable<object[]> SourceNameCases()
    {
        yield return new object[] { MarketSources.Tencent, "腾讯场内ETF" };
        yield return new object[] { MarketSources.EastMoney, "东方财富指数/汇率" };
        yield return new object[] { MarketSources.SinaFund, "新浪场外净值" };
        yield return new object[] { MarketSources.EastMoneyHistory, "东方财富历史K线" };
        yield return new object[] { "CUSTOM_SOURCE", "CUSTOM_SOURCE" };
    }

    [Theory]
    [MemberData(nameof(FixedTopItems))]
    public void Build_FixedTopBarItemsAreReused(string symbol, string name, string marketType)
    {
        MarketMonitorSnapshot snapshot = Build();

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == symbol);
        Assert.Equal(name, row.Name);
        Assert.Equal(marketType, row.MarketType);
        Assert.Equal("无数据", row.FreshnessStatus);
    }

    [Fact]
    public void Build_EnabledStrategyAddsEtfAndTrackingIndex()
    {
        StrategyConfigRecord strategy = Strategy("159941", "纳指ETF广发", "100.NDX100", true);

        MarketMonitorSnapshot snapshot = Build(strategies: new[] { strategy });

        Assert.Contains(snapshot.QuoteRows, row => row.Code == "159941" && row.Name == strategy.Name);
        Assert.Contains(snapshot.QuoteRows, row => row.Code == "100.NDX100" && row.StrategyCodes == "159941");
        Assert.Equal(1, snapshot.QuoteRows.Count(row => row.Code == "100.NDX100"));
    }

    [Fact]
    public void Build_DisabledStrategyDoesNotAddConfiguredEtfOrIndex()
    {
        MarketMonitorSnapshot snapshot = Build(strategies: new[] { Strategy("600001", "停用策略", "100.CUSTOM", false) });

        Assert.DoesNotContain(snapshot.QuoteRows, row => row.Code is "600001" or "100.CUSTOM");
    }

    [Fact]
    public void Build_ExchangePositionRemainsMonitoredWithoutEnabledStrategy()
    {
        var position = new PositionStateRecord
        {
            StrategyCode = "legacy",
            ActualCode = "513110",
            Source = "场内ETF",
            Quantity = 100
        };

        MarketMonitorSnapshot snapshot = Build(positions: new[] { position });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == "513110");
        Assert.Equal("场内ETF", row.Category);
        Assert.Equal("legacy", row.StrategyCodes);
    }

    [Fact]
    public void Build_NonExchangePositionIsNotAddedAsRequiredInstrument()
    {
        var position = new PositionStateRecord
        {
            StrategyCode = "159941",
            ActualCode = "017091",
            Source = "场外基金",
            Quantity = 100
        };

        MarketMonitorSnapshot snapshot = Build(positions: new[] { position });

        Assert.DoesNotContain(snapshot.QuoteRows, row => row.Code == "017091");
    }

    [Fact]
    public void Build_EnabledOtcChannelIsIncludedWithStrategySearchMetadata()
    {
        StrategyConfigRecord strategy = Strategy("159509", "纳指科技ETF景顺", null, true);
        OtcChannelRecord channel = Otc("159509", "017091", "A类", true);

        MarketMonitorSnapshot snapshot = Build(new[] { strategy }, otcChannels: new[] { channel });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == "017091");
        Assert.Equal("场外基金", row.Category);
        Assert.Contains("159509", row.StrategyCodes, StringComparison.Ordinal);
        Assert.Contains("A类", row.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DisabledOtcChannelDoesNotCreateMissingQuoteRow()
    {
        MarketMonitorSnapshot snapshot = Build(otcChannels: new[] { Otc("159509", "017091", "A类", false) });

        Assert.DoesNotContain(snapshot.QuoteRows, row => row.Code == "017091");
    }

    [Fact]
    public void Build_ExtraRealCacheRecordIsRetained()
    {
        MarketQuoteRecord extra = Quote("100.EXTRA", "INDEX", "OTHER_REAL_SOURCE", 123.45, 5);

        MarketMonitorSnapshot snapshot = Build(quotes: new[] { extra });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == "100.EXTRA");
        Assert.Equal(123.45, row.Price);
        Assert.Equal("OTHER_REAL_SOURCE", row.Source);
    }

    [Fact]
    public void Build_MarketTypeAndNormalizedSymbolAreTheDedupeKey()
    {
        var strategies = new[]
        {
            Strategy("sz159941", "策略一", null, true),
            Strategy("159941", "策略二", null, true)
        };
        MarketQuoteRecord quote = Quote("159941", "ETF", MarketSources.Tencent, 1.6, 5);

        MarketMonitorSnapshot snapshot = Build(strategies, quotes: new[] { quote });

        Assert.Equal(1, snapshot.QuoteRows.Count(row => row.Code == "159941" && row.MarketType == "ETF"));
    }

    [Theory]
    [InlineData("ETF", "TENCENT_QT")]
    [InlineData("INDEX", "EASTMONEY_PUSH2")]
    [InlineData("FX", "EASTMONEY_PUSH2")]
    [InlineData("OTC", "SINA_FUND")]
    public void Build_SelectsPreferredRealtimeSource(string marketType, string preferredSource)
    {
        string code = marketType is "ETF" or "OTC" ? "600006" : "100.SOURCE";
        MarketQuoteRecord preferred = Quote(code, marketType, preferredSource, 10, 100);
        preferred.OpenValue = 9;
        MarketQuoteRecord newerFallback = Quote(code, marketType, "OTHER_REAL_SOURCE", 20, 1);
        newerFallback.OpenValue = 19;

        MarketMonitorSnapshot snapshot = Build(quotes: new[] { newerFallback, preferred });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == code);
        Assert.Equal(preferredSource, row.Source);
        Assert.Equal(10, row.Price);
        Assert.Equal(9, row.OpenValue);
    }

    [Fact]
    public void Build_UsesLatestOtherCacheWhenPreferredSourceIsAbsent()
    {
        MarketQuoteRecord oldQuote = Quote("600007", "ETF", "SOURCE_A", 1, 60);
        MarketQuoteRecord latestQuote = Quote("600007", "ETF", "SOURCE_B", 2, 10);

        MarketMonitorSnapshot snapshot = Build(quotes: new[] { oldQuote, latestQuote });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == "600007");
        Assert.Equal("SOURCE_B", row.Source);
        Assert.Equal(2, row.Price);
    }

    [Fact]
    public void Build_DoesNotMergeFieldsAcrossSources()
    {
        MarketQuoteRecord preferred = Quote("600008", "ETF", MarketSources.Tencent, 1.8, 60);
        preferred.LastClose = null;
        MarketQuoteRecord fallback = Quote("600008", "ETF", "OTHER_SOURCE", 1.9, 5);
        fallback.LastClose = 1.7;

        MarketMonitorQuoteRow row = Assert.Single(Build(quotes: new[] { preferred, fallback }).QuoteRows, row => row.Code == "600008");

        Assert.Equal(1.8, row.Price);
        Assert.Null(row.LastClose);
        Assert.Equal("--", row.LastCloseText);
    }

    [Fact]
    public void Build_MissingRequiredQuoteRemainsVisibleAsNoData()
    {
        MarketMonitorSnapshot snapshot = Build(strategies: new[] { Strategy("159660", "纳指ETF汇添富", null, true) });

        MarketMonitorQuoteRow row = Assert.Single(snapshot.QuoteRows, row => row.Code == "159660");
        Assert.Null(row.Price);
        Assert.Equal("--", row.PriceText);
        Assert.Equal("无数据", row.FreshnessStatus);
        Assert.Equal("--", row.CacheAge);
        Assert.Equal("腾讯场内ETF", row.SourceName);
    }

    [Theory]
    [MemberData(nameof(FreshnessCases))]
    public void ResolveFreshnessStatus_UsesSourceSpecificReceivedAtThresholds(
        string source,
        int ageSeconds,
        string expected)
    {
        MarketQuoteRecord quote = Quote("600009", "ETF", source, 1, ageSeconds);

        Assert.Equal(expected, MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, Now));
    }

    [Fact]
    public void ResolveFreshnessStatus_NullPriceIsNoData()
    {
        MarketQuoteRecord quote = Quote("600010", "ETF", MarketSources.Tencent, null, 5);

        Assert.Equal("无数据", MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, Now));
    }

    [Fact]
    public void ResolveFreshnessStatus_MissingQuoteIsNoData()
        => Assert.Equal("无数据", MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(null, Now));

    [Fact]
    public void ResolveFreshnessStatus_InvalidReceivedAtIsInvalidTime()
    {
        MarketQuoteRecord quote = Quote("600011", "ETF", MarketSources.Tencent, 1, 5);
        quote.ReceivedAt = "not-a-time";

        Assert.Equal("时间无效", MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, Now));
        Assert.Equal("--", MarketMonitorSnapshotBuilder.FormatCacheAge(quote, Now));
    }

    [Theory]
    [InlineData("2026-07-15T02:00:00Z")]
    [InlineData("2026-07-14T22:00:00-04:00")]
    public void ResolveFreshnessStatus_RespectsExplicitUtcAndNegativeOffsets(string receivedAt)
    {
        MarketQuoteRecord quote = Quote("600011", "ETF", MarketSources.Tencent, 1, 5);
        quote.ReceivedAt = receivedAt;

        Assert.Equal("正常", MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, Now));
        Assert.Equal("0秒", MarketMonitorSnapshotBuilder.FormatCacheAge(quote, Now));
    }

    [Theory]
    [MemberData(nameof(AgeCases))]
    public void FormatCacheAge_ProducesReadableChineseDuration(int ageSeconds, string expected)
    {
        MarketQuoteRecord quote = Quote("600012", "ETF", MarketSources.Tencent, 1, ageSeconds);

        Assert.Equal(expected, MarketMonitorSnapshotBuilder.FormatCacheAge(quote, Now));
    }

    [Theory]
    [MemberData(nameof(SourceStatusCases))]
    public void Build_SourceStatusMappingsRemainExplicit(string? rawStatus, string expected, bool normal)
    {
        MarketSourceStatusRecord[] statuses = rawStatus is null
            ? Array.Empty<MarketSourceStatusRecord>()
            : new[] { SourceStatus(MarketSources.Tencent, rawStatus) };

        MarketMonitorSourceRow row = Assert.Single(
            Build(sourceStatuses: statuses).SourceRows,
            row => row.Source == MarketSources.Tencent);

        Assert.Equal(expected, row.Status);
        Assert.Equal(normal, row.IsNormal);
    }

    [Theory]
    [MemberData(nameof(SourceNameCases))]
    public void ResolveSourceName_UsesFriendlyCoreNamesAndPreservesUnknown(string source, string expected)
        => Assert.Equal(expected, MarketMonitorSnapshotBuilder.ResolveSourceName(source));

    [Fact]
    public void Build_AlwaysContainsFourCoreSourceRows()
    {
        MarketMonitorSnapshot snapshot = Build();

        Assert.Contains(snapshot.SourceRows, row => row.Source == MarketSources.Tencent);
        Assert.Contains(snapshot.SourceRows, row => row.Source == MarketSources.EastMoney);
        Assert.Contains(snapshot.SourceRows, row => row.Source == MarketSources.SinaFund);
        Assert.Contains(snapshot.SourceRows, row => row.Source == MarketSources.EastMoneyHistory);
        Assert.All(snapshot.SourceRows, row => Assert.Equal("未记录", row.Status));
    }

    [Fact]
    public void Build_SourceStatusPreservesFailureCooldownAndFullError()
    {
        MarketSourceStatusRecord status = SourceStatus(MarketSources.Tencent, "ERROR");
        status.FailureCount = 7;
        status.CooldownUntil = "2026-07-15 10:05:00";
        status.LastError = "full error detail";

        MarketMonitorSourceRow row = Assert.Single(
            Build(sourceStatuses: new[] { status }).SourceRows,
            row => row.Source == MarketSources.Tencent);

        Assert.Equal(7, row.FailureCount);
        Assert.Equal("7", row.FailureCountText);
        Assert.Equal(status.CooldownUntil, row.CooldownUntil);
        Assert.Equal(status.LastError, row.LastError);
    }

    [Fact]
    public void Build_SummaryCountsComeOnlyFromCurrentSnapshot()
    {
        MarketQuoteRecord normal = Quote("600020", "ETF", MarketSources.Tencent, 1, 5);
        MarketQuoteRecord delayed = Quote("600021", "ETF", MarketSources.Tencent, 1, 50);
        MarketQuoteRecord expired = Quote("600022", "ETF", MarketSources.Tencent, 1, 150);
        MarketQuoteRecord missing = Quote("600023", "ETF", MarketSources.Tencent, null, 5);
        MarketQuoteRecord invalid = Quote("600024", "ETF", MarketSources.Tencent, 1, 5);
        invalid.ReceivedAt = "invalid";

        MarketMonitorSnapshot snapshot = Build(
            quotes: new[] { normal, delayed, expired, missing, invalid },
            sourceStatuses: new[] { SourceStatus(MarketSources.Tencent, "OK") });

        Assert.Equal(1, snapshot.NormalCount);
        Assert.Equal(1, snapshot.DelayedCount);
        Assert.Equal(1, snapshot.ExpiredCount);
        Assert.Equal(7, snapshot.NoDataCount);
        Assert.Equal(1, snapshot.InvalidTimeCount);
        Assert.Equal(9, snapshot.ExpiredOrMissingCount);
        Assert.Equal(1, snapshot.NormalSourceCount);
        Assert.Equal(3, snapshot.AbnormalSourceCount);
    }

    [Fact]
    public void Build_FormatsNullWithoutFakeZeroAndKeepsMarketPrecision()
    {
        MarketQuoteRecord index = Quote("100.FORMAT", "INDEX", MarketSources.EastMoney, 1234.5678, 5);
        index.ChangePercent = 0.01234;
        index.ChangeValue = 1.2345;
        index.Volume = 12345;
        index.Amount = null;

        MarketMonitorQuoteRow row = Assert.Single(Build(quotes: new[] { index }).QuoteRows, row => row.Code == "100.FORMAT");

        Assert.Equal("1,234.57", row.PriceText);
        Assert.Equal("+1.23%", row.ChangePercentText);
        Assert.Equal("1.23万", row.VolumeText);
        Assert.Equal("12,345", row.VolumeFullText);
        Assert.Equal("--", row.AmountText);
        Assert.Equal("上涨", row.TrendStatus);
    }

    [Theory]
    [InlineData("全部", "", 4)]
    [InlineData("指数/汇率", "", 1)]
    [InlineData("场内ETF", "", 1)]
    [InlineData("场外基金", "", 1)]
    [InlineData("全部", " 159941 ", 1)]
    [InlineData("全部", "广发", 1)]
    [InlineData("全部", "strategy-a", 1)]
    [InlineData("全部", "tencent", 1)]
    [InlineData("全部", "腾讯场内ETF", 1)]
    public void FilterRows_FiltersCurrentMemorySnapshotOnly(string filter, string search, int expectedCount)
    {
        MarketMonitorQuoteRow[] rows =
        {
            Row("指数/汇率", "纳斯达克100", "100.NDX100", "", MarketSources.EastMoney, "东方财富指数/汇率"),
            Row("场内ETF", "纳指ETF广发", "159941", "STRATEGY-A", MarketSources.Tencent, "腾讯场内ETF"),
            Row("场外基金", "纳指基金A", "017091", "159509", MarketSources.SinaFund, "新浪场外净值"),
            Row("其它", "其它行情", "X", "", "OTHER", "OTHER")
        };

        IReadOnlyList<MarketMonitorQuoteRow> result = MarketMonitorSnapshotBuilder.FilterRows(rows, filter, search);

        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void Build_DoesNotMutateInputsAndProducesStableOrder()
    {
        var strategies = new List<StrategyConfigRecord>
        {
            Strategy("600032", "B", null, true),
            Strategy("600031", "A", null, true)
        };
        var quotes = new List<MarketQuoteRecord>
        {
            Quote("600032", "ETF", MarketSources.Tencent, 2, 5),
            Quote("600031", "ETF", MarketSources.Tencent, 1, 5)
        };
        string[] originalStrategyOrder = strategies.Select(strategy => strategy.Code).ToArray();
        string[] originalQuoteOrder = quotes.Select(quote => quote.Symbol).ToArray();

        MarketMonitorSnapshot first = Build(strategies, quotes: quotes);
        MarketMonitorSnapshot second = Build(strategies, quotes: quotes);

        Assert.Equal(originalStrategyOrder, strategies.Select(strategy => strategy.Code));
        Assert.Equal(originalQuoteOrder, quotes.Select(quote => quote.Symbol));
        Assert.Equal(first.QuoteRows.Select(row => row.Code), second.QuoteRows.Select(row => row.Code));
        Assert.True(first.QuoteRows.FindIndex(row => row.Code == "600031") < first.QuoteRows.FindIndex(row => row.Code == "600032"));
    }

    private static object[] Case(string source, int ageSeconds, string expected)
        => new object[] { source, ageSeconds, expected };

    private static MarketMonitorSnapshot Build(
        IReadOnlyList<StrategyConfigRecord>? strategies = null,
        IReadOnlyList<PositionStateRecord>? positions = null,
        IReadOnlyList<OtcChannelRecord>? otcChannels = null,
        IReadOnlyList<MarketQuoteRecord>? quotes = null,
        IReadOnlyList<MarketSourceStatusRecord>? sourceStatuses = null)
        => new MarketMonitorSnapshotBuilder().Build(
            strategies ?? Array.Empty<StrategyConfigRecord>(),
            positions ?? Array.Empty<PositionStateRecord>(),
            otcChannels ?? Array.Empty<OtcChannelRecord>(),
            quotes ?? Array.Empty<MarketQuoteRecord>(),
            sourceStatuses ?? Array.Empty<MarketSourceStatusRecord>(),
            Now);

    private static StrategyConfigRecord Strategy(string code, string name, string? indexSecId, bool enabled)
        => new()
        {
            Code = code,
            Name = name,
            IndexSecId = indexSecId,
            Enabled = enabled
        };

    private static OtcChannelRecord Otc(string strategyCode, string otcCode, string classType, bool enabled)
        => new()
        {
            StrategyCode = strategyCode,
            OtcCode = otcCode,
            ClassType = classType,
            Enabled = enabled
        };

    private static MarketQuoteRecord Quote(
        string symbol,
        string marketType,
        string source,
        double? price,
        int ageSeconds)
        => new()
        {
            Symbol = symbol,
            DisplayName = symbol + " name",
            MarketType = marketType,
            Source = source,
            Price = price,
            QuoteTime = Now.AddSeconds(-ageSeconds).ToString("yyyy-MM-dd HH:mm:ss"),
            ReceivedAt = Now.AddSeconds(-ageSeconds).ToString("yyyy-MM-dd HH:mm:ss")
        };

    private static MarketSourceStatusRecord SourceStatus(string source, string status)
        => new()
        {
            Source = source,
            Status = status,
            LastSuccessAt = "2026-07-15 09:59:00",
            UpdatedAt = "2026-07-15 09:59:30"
        };

    private static MarketMonitorQuoteRow Row(
        string filterGroup,
        string name,
        string code,
        string strategyCodes,
        string source,
        string sourceName)
        => new()
        {
            FilterGroup = filterGroup,
            Name = name,
            Code = code,
            StrategyCodes = strategyCodes,
            Source = source,
            SourceName = sourceName
        };
}

internal static class MarketMonitorTestExtensions
{
    public static int FindIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        int index = 0;
        foreach (T item in source)
        {
            if (predicate(item))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
