using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class IndicatorDrawdownSnapshotBuilderTests
{
    [Fact]
    public void BuildInstruments_AlwaysIncludesOnlyTwoLockedIndexesBeforeEtfs()
    {
        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(Array.Empty<StrategyConfigRecord>());

        Assert.Collection(
            instruments,
            item => Assert.Equal("251.NDXTMC", item.Code),
            item => Assert.Equal("100.NDX100", item.Code));
        Assert.All(instruments, item => Assert.Equal("指数", item.Category));
    }

    [Fact]
    public void BuildInstruments_EnabledEtfsAreNormalizedDeduplicatedAndStrategyCodesSorted()
    {
        var strategies = new[]
        {
            Strategy(2, "sz159941", "第二策略", true),
            Strategy(1, "159941", "第一策略", true),
            Strategy(3, "513100", "纳指ETF国泰", true)
        };

        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(strategies);

        IndicatorDrawdownInstrument row = Assert.Single(instruments, item => item.Code == "159941");
        Assert.Equal("159941, sz159941", row.StrategyCodes);
        Assert.Equal(4, instruments.Count);
    }

    [Fact]
    public void BuildInstruments_DisabledStrategyIsExcluded()
    {
        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(new[] { Strategy(1, "159509", "停用", false) });

        Assert.DoesNotContain(instruments, item => item.Code == "159509");
    }

    [Fact]
    public void BuildInstruments_InvalidNonSixDigitStrategyCodeIsExcluded()
    {
        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(new[] { Strategy(1, "invalid", "无效", true) });

        Assert.Equal(2, instruments.Count);
    }

    [Fact]
    public void BuildInstruments_MissingStrategyNameUsesLockedPlaceholder()
    {
        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(
            new[] { Strategy(1, "159941", "   ", true) });

        Assert.Equal("--", Assert.Single(instruments, item => item.Code == "159941").Name);
    }

    [Fact]
    public void BuildInstruments_DoesNotIncludeConfiguredTrackingIndexesOrOtcSymbols()
    {
        StrategyConfigRecord strategy = Strategy(1, "159509", "纳指科技ETF景顺", true);
        strategy.IndexSecId = "100.OTHER";

        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(new[] { strategy });

        Assert.DoesNotContain(instruments, item => item.Code == "100.OTHER");
        Assert.DoesNotContain(instruments, item => item.MarketType == "OTC");
    }

    [Theory]
    [InlineData("全部", 3)]
    [InlineData("指数", 2)]
    [InlineData("场内 ETF", 1)]
    public void FilterRows_FiltersLockedTypes(string filter, int expected)
    {
        IndicatorDrawdownRow[] rows = Rows();

        Assert.Equal(expected, IndicatorDrawdownSnapshotBuilder.FilterRows(rows, filter, null).Count);
    }

    [Theory]
    [InlineData("159941")]
    [InlineData("广发")]
    [InlineData("策略甲")]
    [InlineData("TENCENT_DAILY_QFQ")]
    [InlineData("TENCENT_QT")]
    [InlineData("正常")]
    public void FilterRows_SearchesAllRequiredFields(string search)
    {
        IReadOnlyList<IndicatorDrawdownRow> result = IndicatorDrawdownSnapshotBuilder.FilterRows(Rows(), "全部", search);

        Assert.Single(result);
        Assert.Equal("159941", result[0].Code);
    }

    [Fact]
    public void ReplaceRows_RemovesNoLongerEnabledInstrumentAndKeepsUnchangedRows()
    {
        var builder = new IndicatorDrawdownSnapshotBuilder();
        var current = new IndicatorDrawdownSnapshot { Rows = Rows(), GeneratedAt = DateTimeOffset.Now, HistoryCheckedAt = DateTimeOffset.Now };
        HashSet<string> valid = new[] { "INDEX|251.NDXTMC", "INDEX|100.NDX100" }.ToHashSet(StringComparer.OrdinalIgnoreCase);

        IndicatorDrawdownSnapshot result = builder.ReplaceRows(current, Array.Empty<IndicatorDrawdownRow>(), DateTimeOffset.Now, DateTimeOffset.Now, valid);

        Assert.Equal(2, result.Rows.Count);
        Assert.DoesNotContain(result.Rows, row => row.Code == "159941");
    }

    [Fact]
    public void RefreshRealtime_RemovesInactiveRowsImmediatelyWithoutInventingAddedHistoryRows()
    {
        var builder = new IndicatorDrawdownSnapshotBuilder();
        var current = new IndicatorDrawdownSnapshot
        {
            Rows = Rows(),
            GeneratedAt = DateTimeOffset.Now,
            HistoryCheckedAt = DateTimeOffset.Now
        };
        IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(
            new[] { Strategy(1, "513100", "新增ETF", true) });
        var realtime = new IndicatorDrawdownRealtimeReadModel
        {
            Instruments = instruments,
            ReadAt = DateTimeOffset.Now
        };

        IndicatorDrawdownSnapshot result = builder.RefreshRealtime(current, realtime, DateTimeOffset.Now);

        Assert.DoesNotContain(result.Rows, row => row.Code == "159941");
        Assert.DoesNotContain(result.Rows, row => row.Code == "513100");
        Assert.Equal(new[] { "251.NDXTMC", "100.NDX100" }, result.Rows.Select(row => row.Code));
    }

    [Fact]
    public void ReplaceRows_RebuildsAllSummaryCountsDeepestValuesAndHistorySignatures()
    {
        var builder = new IndicatorDrawdownSnapshotBuilder();
        IndicatorDrawdownRow[] rows =
        {
            new() { Key = "A", Code = "A", DataStatus = "正常", CurrentDrawdown = -0.1, MaximumDrawdown = -0.2, HistorySignature = "sig-a" },
            new() { Key = "B", Code = "B", DataStatus = "数据不足", CurrentDrawdown = -0.3, MaximumDrawdown = -0.4, HistorySignature = "sig-b" },
            new() { Key = "C", Code = "C", DataStatus = "历史滞后" },
            new() { Key = "D", Code = "D", DataStatus = "无历史" },
            new() { Key = "E", Code = "E", DataStatus = "数据损坏" },
            new() { Key = "F", Code = "F", DataStatus = "实时行情缺失" },
            new() { Key = "G", Code = "G", DataStatus = "数据源异常" },
            new() { Key = "H", Code = "H", DataStatus = "熔断冷却" },
            new() { Key = "I", Code = "I", DataStatus = "限频" }
        };

        IndicatorDrawdownSnapshot snapshot = builder.ReplaceRows(
            new IndicatorDrawdownSnapshot(), rows, DateTimeOffset.Now, DateTimeOffset.Now);

        Assert.Equal(9, snapshot.TotalCount);
        Assert.Equal(1, snapshot.NormalCount);
        Assert.Equal(1, snapshot.InsufficientCount);
        Assert.Equal(1, snapshot.StaleCount);
        Assert.Equal(2, snapshot.MissingOrCorruptCount);
        Assert.Equal(1, snapshot.CorruptCount);
        Assert.Equal(1, snapshot.NoHistoryCount);
        Assert.Equal(1, snapshot.SourceErrorCount);
        Assert.Equal(1, snapshot.CooldownCount);
        Assert.Equal(1, snapshot.RateLimitCount);
        Assert.Equal(1, snapshot.MissingRealtimeCount);
        Assert.Equal(6, snapshot.AbnormalOrMissingCount);
        Assert.Equal(snapshot.TotalCount, snapshot.NormalCount + snapshot.InsufficientCount + snapshot.StaleCount + snapshot.AbnormalOrMissingCount);
        Assert.Equal("无历史 1｜损坏 1｜源异常 1｜熔断 1｜限频 1｜实时缺失 1", snapshot.AbnormalOrMissingToolTip);
        Assert.Equal("B", snapshot.DeepestCurrentCode);
        Assert.Equal(-0.3, snapshot.DeepestCurrentDrawdown);
        Assert.Equal("B", snapshot.DeepestMaximumCode);
        Assert.Equal(-0.4, snapshot.DeepestMaximumDrawdown);
        Assert.Equal("sig-a", snapshot.HistorySignatures["A"]);
        Assert.Equal(snapshot.Rows, snapshot.FilteredRows);
        Assert.Equal(new[] { "B", "A" }, snapshot.Rows.Take(2).Select(row => row.Code));
        Assert.All(snapshot.Rows.Skip(2), row => Assert.Null(row.CurrentDrawdown));
    }

    [Theory]
    [InlineData("数据损坏", true)]
    [InlineData("无历史", true)]
    [InlineData("数据源异常", true)]
    [InlineData("熔断冷却", true)]
    [InlineData("限频", true)]
    [InlineData("实时行情缺失", true)]
    [InlineData("正常", false)]
    [InlineData("数据不足", false)]
    [InlineData("历史滞后", false)]
    public void ReplaceRows_AbnormalOrMissingUsesOnlyTheSixFinalMutuallyExclusiveStatuses(string status, bool expectedAbnormal)
    {
        var builder = new IndicatorDrawdownSnapshotBuilder();

        IndicatorDrawdownSnapshot snapshot = builder.ReplaceRows(
            new IndicatorDrawdownSnapshot(),
            new[] { new IndicatorDrawdownRow { Key = "A", Code = "A", DataStatus = status } },
            DateTimeOffset.Now,
            DateTimeOffset.Now);

        Assert.Equal(expectedAbnormal ? 1 : 0, snapshot.AbnormalOrMissingCount);
        Assert.Equal(snapshot.TotalCount, snapshot.NormalCount + snapshot.InsufficientCount + snapshot.StaleCount + snapshot.AbnormalOrMissingCount);
    }

    [Fact]
    public void ReplaceRows_ScreenshotEquivalentSummaryReconcilesTenRowsWithTwoRateLimitedIndexes()
    {
        var builder = new IndicatorDrawdownSnapshotBuilder();
        IndicatorDrawdownRow[] rows = Enumerable.Range(1, 7)
            .Select(index => new IndicatorDrawdownRow { Key = $"ETF-{index}", Code = $"ETF-{index}", DataStatus = "正常" })
            .Append(new IndicatorDrawdownRow { Key = "STALE", Code = "STALE", DataStatus = "历史滞后" })
            .Append(new IndicatorDrawdownRow { Key = "INDEX-1", Code = "251.NDXTMC", DataStatus = "限频" })
            .Append(new IndicatorDrawdownRow { Key = "INDEX-2", Code = "100.NDX100", DataStatus = "限频" })
            .ToArray();

        IndicatorDrawdownSnapshot snapshot = builder.ReplaceRows(
            new IndicatorDrawdownSnapshot(), rows, DateTimeOffset.Now, DateTimeOffset.Now);

        Assert.Equal(10, snapshot.TotalCount);
        Assert.Equal(7, snapshot.NormalCount);
        Assert.Equal(0, snapshot.InsufficientCount);
        Assert.Equal(1, snapshot.StaleCount);
        Assert.Equal(2, snapshot.AbnormalOrMissingCount);
        Assert.Equal(2, snapshot.RateLimitCount);
        Assert.Equal(10, snapshot.NormalCount + snapshot.InsufficientCount + snapshot.StaleCount + snapshot.AbnormalOrMissingCount);
        Assert.Equal("无历史 0｜损坏 0｜源异常 0｜熔断 0｜限频 2｜实时缺失 0", snapshot.AbnormalOrMissingToolTip);
    }

    private static IndicatorDrawdownRow[] Rows()
        => new[]
        {
            new IndicatorDrawdownRow { Key = "INDEX|251.NDXTMC", Category = "指数", Code = "251.NDXTMC", Name = "纳斯达克科技指数" },
            new IndicatorDrawdownRow { Key = "INDEX|100.NDX100", Category = "指数", Code = "100.NDX100", Name = "纳斯达克100指数" },
            new IndicatorDrawdownRow
            {
                Key = "ETF|159941", Category = "场内 ETF", Code = "159941", Name = "纳指ETF广发",
                StrategyCodes = "策略甲", HistorySource = "TENCENT_DAILY_QFQ", QuoteSource = "TENCENT_QT", DataStatus = "正常"
            }
        };

    private static StrategyConfigRecord Strategy(long id, string code, string name, bool enabled)
        => new() { Id = id, Code = code, Name = name, Enabled = enabled };
}
