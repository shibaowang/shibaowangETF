using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Strategy;

public sealed class T1T6ChartCenterSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void TierDefinitions_AreSixLockedTiersInOrder()
    {
        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(Strategy(), null);

        Assert.Equal(6, tiers.Count);
        Assert.Equal(new[] { "T1", "T2", "T3", "T4", "T5", "T6" }, tiers.Select(item => item.TierCode));
        Assert.Equal(new[] { "狙击一档", "狙击二档", "狙击三档", "狙击四档", "狙击五档", "狙击六档" }, tiers.Select(item => item.TierName));
        Assert.Equal(new[] { -0.05, -0.10, -0.15, -0.20, -0.25, -0.30 }, tiers.Select(item => item.TriggerDrawdown));
    }

    [Fact]
    public void TierDefinitions_DefaultWeightsCumulativeWeightsAndRatiosAreCorrect()
    {
        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(Strategy(), null);

        Assert.Equal(new double[] { 1, 2, 4, 8, 16, 32 }, tiers.Select(item => item.ConfiguredWeight));
        Assert.Equal(new double[] { 1, 3, 7, 15, 31, 63 }, tiers.Select(item => item.CumulativeWeight));
        Assert.Equal(new[] { 1d / 63, 3d / 63, 7d / 63, 15d / 63, 31d / 63, 1d }, tiers.Select(item => item.CumulativeWeightRatio));
        Assert.Equal(1d, tiers[^1].CumulativeWeightRatio, 10);
    }

    [Fact]
    public void TierDefinitions_PositiveConfiguredWeightsTakePrecedence()
    {
        StrategyConfigRecord strategy = Strategy();
        strategy.T1Weight = 3;
        strategy.T2Weight = 5;
        strategy.T3Weight = 7;
        strategy.T4Weight = 9;
        strategy.T5Weight = 11;
        strategy.T6Weight = 13;

        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(strategy, null);

        Assert.Equal(new double[] { 3, 5, 7, 9, 11, 13 }, tiers.Select(item => item.ConfiguredWeight));
        Assert.Equal(new double[] { 3, 8, 15, 24, 35, 48 }, tiers.Select(item => item.CumulativeWeight));
    }

    [Fact]
    public void TierDefinitions_InvalidWeightsFallBackByTier()
    {
        StrategyConfigRecord strategy = Strategy();
        strategy.T1Weight = null;
        strategy.T2Weight = 0;
        strategy.T3Weight = -4;
        strategy.T4Weight = double.NaN;
        strategy.T5Weight = double.PositiveInfinity;
        strategy.T6Weight = double.NegativeInfinity;

        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(strategy, null);

        Assert.Equal(new double[] { 1, 2, 4, 8, 16, 32 }, tiers.Select(item => item.ConfiguredWeight));
    }

    [Theory]
    [InlineData(-0.04, 0)]
    [InlineData(-0.05, 1)]
    [InlineData(-0.12, 2)]
    [InlineData(-0.30, 6)]
    [InlineData(-0.45, 6)]
    public void TierDefinitions_ConditionMetUsesPersistedIndexDrawdownOnly(double drawdown, int expectedMet)
    {
        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(
            Strategy(),
            Decision(indexDrawdown: drawdown));

        Assert.Equal(expectedMet, tiers.Count(item => item.IsConditionMet));
        Assert.All(tiers.Where(item => item.IsConditionMet), item => Assert.Equal("回撤条件满足", item.ConditionStatusText));
        Assert.DoesNotContain(tiers, item => item.ConditionStatusText.Contains("成交", StringComparison.Ordinal));
    }

    [Fact]
    public void TierDefinitions_MissingDecisionUsesMissingDataWithoutFakeCondition()
    {
        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(Strategy(), null);

        Assert.All(tiers, item =>
        {
            Assert.False(item.IsConditionMet);
            Assert.Equal("缺少决策数据", item.ConditionStatusText);
        });
    }

    [Fact]
    public void TierDefinitions_CurrentSuggestedTierHighlightsOnlyExactPersistedTier()
    {
        IReadOnlyList<T1T6TierDisplayDefinition> tiers = T1T6ChartCenterSnapshotBuilder.BuildTierDefinitions(
            Strategy(),
            Decision(indexDrawdown: -0.30, targetTier: "狙击二档"));

        T1T6TierDisplayDefinition highlighted = Assert.Single(tiers, item => item.IsCurrentSuggestedTier);
        Assert.Equal("T2", highlighted.TierCode);
        Assert.Equal(6, tiers.Count(item => item.IsConditionMet));
    }

    [Fact]
    public void Build_NonStandardPersistedTierIsShownSafelyAndNotRemappedFromDrawdown()
    {
        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { Strategy() },
            new[] { Decision(indexDrawdown: -0.30, targetTier: "观察档") },
            new[] { Quote() },
            new[] { Status() });

        Assert.Equal("观察档", snapshot.SelectedRow!.CurrentSuggestedTierText);
        Assert.DoesNotContain(snapshot.SelectedRow.Tiers, item => item.IsCurrentSuggestedTier);
    }

    [Fact]
    public void Build_OnlyEnabledStrategiesAreShownAndNoHoldingIsRequired()
    {
        StrategyConfigRecord enabled = Strategy(1, "159941", true);
        StrategyConfigRecord disabled = Strategy(2, "513110", false);

        T1T6ChartCenterSnapshot snapshot = Build(new[] { disabled, enabled }, Array.Empty<StrategyDecisionStateRecord>());

        T1T6StrategyRow row = Assert.Single(snapshot.Rows);
        Assert.Equal("159941", row.StrategyCode);
    }

    [Fact]
    public void Build_SortsStrategiesByExactStrategyCodeThenId()
    {
        T1T6ChartCenterSnapshot snapshot = Build(new[]
        {
            Strategy(3, "sz159941"),
            Strategy(2, "513100"),
            Strategy(1, "159941")
        });

        Assert.Equal(new[] { "159941", "513100", "sz159941" }, snapshot.Rows.Select(row => row.StrategyCode));
    }

    [Fact]
    public void Build_SameEtfStrategiesRemainSeparateShareQuoteAndKeepExactDecisions()
    {
        StrategyConfigRecord first = Strategy(1, "159941");
        StrategyConfigRecord second = Strategy(2, "sz159941");
        MarketQuoteRecord quote = Quote(symbol: "159941", displayName: "纳指ETF广发", price: 1.678);

        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { second, first },
            new[]
            {
                Decision(1, "159941", targetTier: "狙击一档", action: "动作甲"),
                Decision(2, "sz159941", targetTier: "狙击三档", action: "动作乙")
            },
            new[] { quote },
            new[] { Status() });

        Assert.Equal(2, snapshot.Rows.Count);
        Assert.All(snapshot.Rows, row => Assert.Equal(1.678, row.LatestPrice));
        Assert.Equal("动作甲", snapshot.Rows.Single(row => row.StrategyCode == "159941").CurrentAction);
        Assert.Equal("动作乙", snapshot.Rows.Single(row => row.StrategyCode == "sz159941").CurrentAction);
        Assert.Equal(2, snapshot.DuplicateEtfStrategyCount);
        Assert.All(snapshot.Rows, row => Assert.True(row.IsDuplicateEtfCode));
        Assert.Contains("159941：159941 / sz159941", snapshot.DuplicateEtfToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotFallBackDecisionByNormalizedEtfCode()
    {
        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { Strategy(2, "sz159941") },
            new[] { Decision(1, "159941", action: "不应串用") },
            new[] { Quote() },
            new[] { Status() });

        Assert.False(snapshot.SelectedRow!.HasDecision);
        Assert.Equal("无决策", snapshot.SelectedRow.CurrentAction);
    }

    [Fact]
    public void Build_MissingDecisionIsCountedAndUsesSafeDisplay()
    {
        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { Strategy() },
            Array.Empty<StrategyDecisionStateRecord>(),
            new[] { Quote() },
            new[] { Status() });

        Assert.Equal(1, snapshot.MissingDecisionCount);
        Assert.Equal("无决策", snapshot.SelectedRow!.CurrentSuggestedTierText);
        Assert.Equal("无决策", snapshot.SelectedRow.DataStatusText);
    }

    [Fact]
    public void Build_NormalizesEtfCodeAndDisablesChartForInvalidCodeWithoutDroppingRow()
    {
        T1T6ChartCenterSnapshot normalized = Build(new[] { Strategy(1, "sz159941") });
        T1T6ChartCenterSnapshot invalid = Build(new[] { Strategy(1, "invalid") });

        Assert.Equal("159941", normalized.SelectedRow!.EtfCode);
        Assert.True(normalized.SelectedRow.CanOpenChart);
        Assert.Equal("invalid", invalid.SelectedRow!.EtfCode);
        Assert.False(invalid.SelectedRow.CanOpenChart);
        Assert.Equal("ETF代码无效", invalid.SelectedRow.DataStatusText);
    }

    [Fact]
    public void Build_QuoteNameHasPriorityThenStrategyNameThenPlaceholder()
    {
        T1T6StrategyRow quoteName = Build(
            new[] { Strategy(name: "配置名称") },
            quotes: new[] { Quote(displayName: "行情名称") }).SelectedRow!;
        T1T6StrategyRow strategyName = Build(
            new[] { Strategy(name: "配置名称") },
            quotes: new[] { Quote(displayName: " ") }).SelectedRow!;
        T1T6StrategyRow placeholder = Build(
            new[] { Strategy(name: " ") },
            quotes: new[] { Quote(displayName: null) }).SelectedRow!;

        Assert.Equal("行情名称", quoteName.EtfName);
        Assert.Equal("配置名称", strategyName.EtfName);
        Assert.Equal("--", placeholder.EtfName);
    }

    [Fact]
    public void Build_HealthyQuoteSummaryRequiresPriceFreshnessAndOkSource()
    {
        T1T6ChartCenterSnapshot healthy = Build(
            new[] { Strategy() },
            new[] { Decision() },
            new[] { Quote() },
            new[] { Status("OK") });
        T1T6ChartCenterSnapshot rateLimited = Build(
            new[] { Strategy() },
            new[] { Decision() },
            new[] { Quote() },
            new[] { Status("RATE_LIMIT") });

        Assert.Equal(1, healthy.HealthyQuoteCount);
        Assert.Equal(0, rateLimited.HealthyQuoteCount);
        Assert.Equal("限频", rateLimited.SelectedRow!.DataStatusText);
    }

    [Theory]
    [InlineData("ERROR", "数据源异常")]
    [InlineData("RATE_LIMIT", "限频")]
    [InlineData("COOLDOWN", "熔断冷却")]
    public void Build_SourceStatusUsesLockedPriority(string sourceStatus, string expected)
    {
        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { Strategy() },
            new[] { Decision() },
            new[] { Quote() },
            new[] { Status(sourceStatus) });

        Assert.Equal(expected, snapshot.SelectedRow!.DataStatusText);
    }

    [Fact]
    public void Build_MissingQuoteUsesPlaceholdersNotFakeZero()
    {
        T1T6ChartCenterSnapshot snapshot = Build(new[] { Strategy() }, new[] { Decision() });

        Assert.Null(snapshot.SelectedRow!.LatestPrice);
        Assert.Equal("--", snapshot.SelectedRow.LatestPriceText);
        Assert.Equal("实时行情缺失", snapshot.SelectedRow.DataStatusText);
    }

    [Fact]
    public void Build_DefaultSelectionPreservesStrategyCodeAndUsesNextRowWhenRemoved()
    {
        StrategyConfigRecord first = Strategy(1, "159501");
        StrategyConfigRecord selected = Strategy(2, "159509");
        StrategyConfigRecord next = Strategy(3, "159513");
        var builder = new T1T6ChartCenterSnapshotBuilder();
        var initialModel = new T1T6ChartCenterReadModel { EnabledStrategies = new[] { first, selected, next }, ReadAt = Now };

        T1T6ChartCenterSnapshot initial = builder.Build(initialModel, Now, "159509", 1);
        T1T6ChartCenterSnapshot removed = builder.Build(
            new T1T6ChartCenterReadModel { EnabledStrategies = new[] { first, next }, ReadAt = Now },
            Now,
            initial.SelectedStrategyCode,
            1);

        Assert.Equal("159509", initial.SelectedStrategyCode);
        Assert.Equal("159513", removed.SelectedStrategyCode);
    }

    [Fact]
    public void Build_EmptyStrategiesIsSafe()
    {
        T1T6ChartCenterSnapshot snapshot = Build(Array.Empty<StrategyConfigRecord>());

        Assert.Empty(snapshot.Rows);
        Assert.Null(snapshot.SelectedRow);
        Assert.Equal(0, snapshot.EnabledStrategyCount);
    }

    [Fact]
    public void Build_ReadErrorIsExposedWithoutInventingRows()
    {
        var builder = new T1T6ChartCenterSnapshotBuilder();
        T1T6ChartCenterSnapshot snapshot = builder.Build(
            new T1T6ChartCenterReadModel { ReadAt = Now, ReadError = "locked database" },
            Now);

        Assert.Equal("本地读取失败", snapshot.ReadStatusText);
        Assert.Equal("locked database", snapshot.ReadError);
        Assert.Empty(snapshot.Rows);
    }

    [Fact]
    public void ChartRequest_UsesNormalizedSymbolDisplayNameAndStrategySource()
    {
        T1T6StrategyRow row = Build(
            new[] { Strategy(1, "sz159941") },
            quotes: new[] { Quote(displayName: "纳指ETF广发") }).SelectedRow!;

        T1T6ChartOpenRequest request = Assert.IsType<T1T6ChartOpenRequest>(T1T6ChartCenterSnapshotBuilder.BuildChartOpenRequest(row));

        Assert.Equal("ETF", request.MarketType);
        Assert.Equal("159941", request.Symbol);
        Assert.Equal("纳指ETF广发", request.DisplayName);
        Assert.Equal("sz159941", request.StrategyCode);
    }

    [Fact]
    public void ChartRequest_SameEtfDifferentStrategiesUsesSameSecuritySymbol()
    {
        T1T6ChartCenterSnapshot snapshot = Build(
            new[] { Strategy(1, "159941"), Strategy(2, "sz159941") },
            quotes: new[] { Quote() });

        T1T6ChartOpenRequest[] requests = snapshot.Rows
            .Select(row => T1T6ChartCenterSnapshotBuilder.BuildChartOpenRequest(row)!)
            .ToArray();

        Assert.Equal(new[] { "159941", "159941" }, requests.Select(item => item.Symbol));
        Assert.Equal(2, requests.Select(item => item.StrategyCode).Distinct().Count());
    }

    [Fact]
    public void ChartRequest_InvalidCodeReturnsNull()
    {
        T1T6StrategyRow row = Build(new[] { Strategy(1, "bad") }).SelectedRow!;

        Assert.Null(T1T6ChartCenterSnapshotBuilder.BuildChartOpenRequest(row));
    }

    [Fact]
    public void DisplayModelsContainNoAccountHoldingDraftAmountQuantityOrExecutionFields()
    {
        string[] propertyNames = typeof(T1T6StrategyRow).GetProperties().Select(property => property.Name).ToArray();
        string[] forbidden =
        {
            "AvailableCash", "RealSniperPool", "HoldingQuantity", "HoldingCost", "HoldingPnl",
            "TargetPrice", "TargetAmount", "TargetQuantity", "ExecutedQuantity", "ExecutionStatus", "OrderDraft"
        };

        Assert.All(forbidden, name => Assert.DoesNotContain(name, propertyNames));
    }

    private static T1T6ChartCenterSnapshot Build(
        IReadOnlyList<StrategyConfigRecord> strategies,
        IReadOnlyList<StrategyDecisionStateRecord>? decisions = null,
        IReadOnlyList<MarketQuoteRecord>? quotes = null,
        IReadOnlyList<MarketSourceStatusRecord>? statuses = null)
        => new T1T6ChartCenterSnapshotBuilder().Build(
            new T1T6ChartCenterReadModel
            {
                EnabledStrategies = strategies,
                LatestDecisions = decisions ?? Array.Empty<StrategyDecisionStateRecord>(),
                RelatedQuotes = quotes ?? Array.Empty<MarketQuoteRecord>(),
                RelatedSourceStatuses = statuses ?? Array.Empty<MarketSourceStatusRecord>(),
                ReadAt = Now
            },
            Now);

    private static StrategyConfigRecord Strategy(
        long id = 1,
        string code = "159941",
        bool enabled = true,
        string name = "策略甲")
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            IndexSecId = "100.NDX100",
            Enabled = enabled,
            CreatedAt = "2026-07-01 09:00:00",
            UpdatedAt = "2026-07-16 09:00:00"
        };

    private static StrategyDecisionStateRecord Decision(
        long id = 1,
        string strategyCode = "159941",
        double indexDrawdown = -0.12,
        string? targetTier = "狙击二档",
        string action = "持股观察")
        => new()
        {
            Id = id,
            CalculatedAt = "2026-07-16 09:59:30",
            StrategyCode = strategyCode,
            Name = "策略甲",
            ActionInstruction = action,
            StrategyStatus = "正常趋势",
            PreferredSource = MarketSources.Tencent,
            TargetTier = targetTier,
            Premium = 0.015,
            IndexDrawdown = indexDrawdown,
            PrerequisiteStatus = "已就绪"
        };

    private static MarketQuoteRecord Quote(
        string symbol = "159941",
        string? displayName = "纳指ETF广发",
        double price = 1.678)
        => new()
        {
            Id = 1,
            Symbol = symbol,
            DisplayName = displayName,
            MarketType = "ETF",
            Source = MarketSources.Tencent,
            Price = price,
            LastClose = 1.65,
            QuoteTime = "2026-07-16 09:59:30",
            ReceivedAt = "2026-07-16 09:59:31"
        };

    private static MarketSourceStatusRecord Status(string status = "OK")
        => new()
        {
            Id = 1,
            Source = MarketSources.Tencent,
            Status = status,
            LastSuccessAt = "2026-07-16 09:59:31",
            FailureCount = 0,
            UpdatedAt = "2026-07-16 09:59:31"
        };
}
