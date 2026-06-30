using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Strategy;

public class StrategyDecisionServiceTests
{
    private readonly StrategyDecisionService _service = new();

    [Fact]
    public void BaseGapOutputsStrategicBaseTarget()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 15000, cash: 100000).Single();

        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.Equal(5000, decision.TargetAmount);
    }

    [Fact]
    public void BaseSatisfiedDoesNotOutputStrategicBase()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 21000, cash: 100000).Single();

        Assert.NotEqual("战略底仓", decision.ActionInstruction);
    }

    [Fact]
    public void ReturnThresholdOutputsProfitTaking()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 30000,
            marketValue: 45000,
            cash: 100000,
            sellRatio: 0.40).Single();

        Assert.Equal("止盈减仓(留底)", decision.ActionInstruction);
        Assert.Equal("收益达标", decision.StrategyStatus);
    }

    [Fact]
    public void PremiumThresholdOutputsPremiumTaking()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 30000,
            price: 1.10,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.08,
            extraPrice: 0.20).Single();

        Assert.Equal("溢价达标减仓(留底)", decision.ActionInstruction);
        Assert.Equal("溢价止盈", decision.StrategyStatus);
    }

    [Fact]
    public void ExtraPremiumThresholdOutputsExtremePremium()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 30000,
            price: 1.20,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.08,
            extraPrice: 0.15).Single();

        Assert.Equal("全清换现金(留底)", decision.ActionInstruction);
        Assert.Equal("极端溢价", decision.StrategyStatus);
    }

    [Fact]
    public void AccountBaseCompleteAllowsExtremePremiumSellExcess()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 128585.45,
            cost: 5809,
            accountTotalPositionCost: 27550.78,
            price: 1.105,
            iopv: 1.0,
            cash: 102300.91,
            extraPrice: 0.10).Single();

        Assert.NotEqual("底仓保护", decision.StrategyStatus);
        Assert.Equal("全清换现金(留底)", decision.ActionInstruction);
        Assert.Equal("极端溢价", decision.StrategyStatus);
        Assert.Equal(1833.69, Math.Round(decision.TargetAmount!.Value, 2));
    }

    [Fact]
    public void AccountBaseIncompletePrioritizesStrategicBaseBeforeExtremePremiumSell()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 100000,
            cost: 5809,
            accountTotalPositionCost: 27550,
            price: 1.12,
            iopv: 1.0,
            cash: 100000,
            extraPrice: 0.10,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.Equal(2450, decision.TargetAmount);
    }

    [Fact]
    public void BaseIncompleteNoHoldingExtremePremiumOutputsStrategicBase()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 100000,
            cost: 0,
            accountTotalPositionCost: 1000,
            price: 1.12,
            iopv: 1.0,
            cash: 100000,
            extraPrice: 0.10,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.NotEqual("极端溢价", decision.ActionInstruction);
        Assert.NotEqual("禁止建仓", decision.StrategyStatus);
    }

    [Fact]
    public void BaseIncompleteOtcOnlyExtremePremiumOutputsStrategicBase()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "888006",
            principal: 100000,
            cost: 5000,
            accountTotalPositionCost: 10000,
            marketValue: 5600,
            price: 1.20,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.10,
            extraPrice: 0.15,
            addPremiumLimit: 0.02,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 5600,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.Equal("场外替代", decision.PreferredSource);
        Assert.True(decision.IsActionable);
        Assert.NotEqual("√ 持股待涨", decision.ActionInstruction);
        Assert.NotEqual("场外替代", decision.StrategyStatus);

        OrderDraftCalculationResult draftResult = new OrderDraftService().Calculate(new OrderDraftCalculationInput
        {
            StrategyDecisions = new[] { decision }
        });
        Assert.DoesNotContain(draftResult.Legs, leg => leg.Side == "卖出" || leg.Source == "场外替代");
    }

    [Fact]
    public void NoHoldingExtremePremiumBlocksOpeningWithoutTierTrigger()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 0,
            accountTotalPositionCost: 30000,
            price: 1.113,
            iopv: 1.0,
            cash: 100000,
            extraPrice: 0.10,
            indexPrice: 96.34,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("极端溢价", decision.ActionInstruction);
        Assert.Equal("禁止建仓", decision.StrategyStatus);
    }

    [Fact]
    public void ExtremePremiumTakesPriorityOverEmptyObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 0,
            accountTotalPositionCost: 30000,
            price: 1.12,
            iopv: 1.0,
            cash: 100000,
            extraPrice: 0.10,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.NotEqual("等待建仓", decision.ActionInstruction);
        Assert.NotEqual("空仓观察", decision.StrategyStatus);
        Assert.Equal("极端溢价", decision.ActionInstruction);
        Assert.Equal("禁止建仓", decision.StrategyStatus);
    }

    [Fact]
    public void ExistingHoldingWithoutActionOutputsHoldingObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("正常趋势", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
    }

    [Fact]
    public void EmptyPositionWithoutActionOutputsEmptyObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 0,
            accountTotalPositionCost: 30000,
            price: 1.02,
            iopv: 1.0,
            cash: 100000,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("等待建仓", decision.ActionInstruction);
        Assert.Equal("空仓观察", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
    }

    [Fact]
    public void Current159513HoldingSceneOutputsHoldingObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159513",
            cost: 14550.29,
            accountTotalPositionCost: 27550.78,
            marketValue: 15393.17,
            price: 1.851,
            iopv: 1.6841,
            cash: 102300.91,
            sellRatio: 0.40,
            takeProfitPrice: 0.10,
            extraPrice: 0.10,
            addPremiumLimit: 0.02,
            indexPrice: 96.34,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true).Single();

        Assert.NotEqual("等待建仓", decision.ActionInstruction);
        Assert.NotEqual("空仓观察", decision.StrategyStatus);
        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("正常趋势", decision.StrategyStatus);
    }

    [Fact]
    public void Current159660HoldingSceneOutputsHoldingObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159660",
            cost: 6991.61,
            accountTotalPositionCost: 27550.78,
            marketValue: 7790.74,
            price: 2.41,
            iopv: 2.2008,
            cash: 102300.91,
            sellRatio: 0.40,
            takeProfitPrice: 0.10,
            extraPrice: 0.10,
            addPremiumLimit: 0.02,
            indexPrice: 96.34,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 7790.74).Single();

        Assert.NotEqual("等待建仓", decision.ActionInstruction);
        Assert.NotEqual("空仓观察", decision.StrategyStatus);
        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("正常趋势", decision.StrategyStatus);
    }

    [Fact]
    public void OtcPremiumTakeProfitDoesNotPreemptCompletedTier()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159509",
            principal: 128588.54,
            cost: 199.88,
            accountTotalPositionCost: 27550.78,
            marketValue: 239.13,
            price: 2.74,
            iopv: 2.2292,
            cash: 102300.91,
            sellRatio: 0.40,
            takeProfitPrice: 0.18,
            extraPrice: 0.23,
            addPremiumLimit: 0.08,
            indexSecId: "251.NDXTMC",
            indexPrice: 93.86,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 239.13,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 100.0, tier: "战略底仓", strategyCode: "159509"),
                Trade(2, "买入", amount: 99.88, tier: "战略底仓", strategyCode: "159509")
            }).Single();

        Assert.Equal("一档建仓完成", decision.ActionInstruction);
        Assert.Equal("场外替代", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);

        OrderDraftCalculationResult draftResult = new OrderDraftService().Calculate(new OrderDraftCalculationInput
        {
            StrategyDecisions = new[] { decision }
        });
        Assert.False(draftResult.Drafts.Single().IsExecutable);
        Assert.Empty(draftResult.Legs);
    }

    [Fact]
    public void OtcOnlyHoldingWithOrdinaryPremiumDoesNotTriggerPremiumSell()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "888001",
            cost: 5000,
            accountTotalPositionCost: 30000,
            marketValue: 5600,
            price: 1.20,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.10,
            extraPrice: 0.50,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 5600).Single();

        Assert.NotEqual("溢价达标减仓(留底)", decision.ActionInstruction);
        Assert.NotEqual("溢价止盈", decision.StrategyStatus);
        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("正常趋势", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);
    }

    [Fact]
    public void OtcOnlyHoldingWithExtremePremiumDoesNotTriggerOtcSell()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "888002",
            cost: 5000,
            accountTotalPositionCost: 30000,
            marketValue: 5600,
            price: 1.20,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.10,
            extraPrice: 0.15,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 5600).Single();

        Assert.NotEqual("全清换现金(留底)", decision.ActionInstruction);
        Assert.NotEqual("极端溢价", decision.StrategyStatus);
        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("场外替代", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);
    }

    [Fact]
    public void EmptyPositionWithOrdinaryPremiumDoesNotTriggerSell()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "888003",
            cost: 0,
            accountTotalPositionCost: 30000,
            price: 1.20,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.10,
            extraPrice: 0.50,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.NotEqual("溢价达标减仓(留底)", decision.ActionInstruction);
        Assert.NotEqual("溢价止盈", decision.StrategyStatus);
        Assert.Equal("等待建仓", decision.ActionInstruction);
        Assert.Equal("空仓观察", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
    }

    [Fact]
    public void MixedHoldingPremiumSellCapsTargetToExchangeEtfCostOnly()
    {
        var input = new StrategyDecisionCalculationInput
        {
            Strategies = new[]
            {
                new StrategyConfigRecord
                {
                    Code = "888004",
                    Name = "混合持仓测试",
                    IndexSecId = "100.NDX100",
                    TakeProfitPrice = 0.10,
                    ExtraPrice = 0.50,
                    Enabled = true
                }
            },
            AccountReplayState = new AccountReplayStateRecord
            {
                ReplayStatus = "正常",
                CashBalance = 100000,
                Principal = 100000,
                TotalPositionCost = 30000
            },
            PositionReplayStates = new[]
            {
                new PositionReplayStateRecord
                {
                    StrategyCode = "888004",
                    ActualCode = "888004",
                    Source = "场内ETF",
                    Quantity = 1000,
                    CostAmount = 1000,
                    AverageCost = 1,
                    MarketPrice = 1.20,
                    MarketValue = 1200
                }
            },
            OtcPositionReplayStates = new[]
            {
                new OtcPositionReplayStateRecord
                {
                    StrategyCode = "888004",
                    ActualCode = "016055",
                    Quantity = 9000,
                    CostAmount = 9000,
                    AverageCost = 1,
                    Nav = 1,
                    MarketValue = 9000
                }
            },
            MarketQuotes = new[]
            {
                Quote("888004", "ETF", 1.20, 1.0),
                Quote("100.NDX100", "INDEX", 98, null),
                Quote("251.NDXTMC", "INDEX", 100, null),
                Quote("016055", "OTC", 1, null)
            },
            MarketHistory = new[]
            {
                History("100.NDX100", 100),
                History("251.NDXTMC", 100)
            },
            BasePositionSettings = BasePositionSettings.Default()
        };

        StrategyDecisionStateRecord decision = _service.Calculate(input).Decisions.Single();

        Assert.Equal("溢价达标减仓(留底)", decision.ActionInstruction);
        Assert.Equal("溢价止盈", decision.StrategyStatus);
        Assert.Equal(1000, decision.TargetAmount);

        OrderDraftCalculationResult draftResult = new OrderDraftService().Calculate(new OrderDraftCalculationInput
        {
            StrategyDecisions = new[] { decision },
            AccountReplayState = input.AccountReplayState,
            PositionReplayStates = input.PositionReplayStates,
            OtcPositionReplayStates = input.OtcPositionReplayStates,
            OtcChannels = new[] { new OtcChannelRecord { StrategyCode = "888004", OtcCode = "016055", ClassType = "C类", Enabled = true } },
            MarketQuotes = input.MarketQuotes
        });

        OrderDraftStateRecord draft = draftResult.Drafts.Single();
        Assert.Equal("场内ETF", draft.Source);
        Assert.Equal("卖出", draft.Side);
        Assert.True(draft.IsExecutable);
        Assert.All(draftResult.Legs, leg => Assert.Equal("场内ETF", leg.Source));
        Assert.DoesNotContain(draftResult.Legs, leg => leg.Source == "场外替代");
    }

    [Fact]
    public void EmptyExtremePremiumStillBlocksOpening()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 0,
            accountTotalPositionCost: 30000,
            price: 1.12,
            iopv: 1.0,
            cash: 100000,
            extraPrice: 0.10,
            indexPrice: 98,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("极端溢价", decision.ActionInstruction);
        Assert.Equal("禁止建仓", decision.StrategyStatus);
    }

    [Fact]
    public void Current159941ExtremePremiumSellIsUnchanged()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159941",
            principal: 128585.45,
            cost: 5808.995294117648,
            accountTotalPositionCost: 27550.77529411765,
            marketValue: 6540.3,
            price: 1.677,
            iopv: 1.5107,
            cash: 102300.91,
            sellRatio: 0.40,
            takeProfitPrice: 0.10,
            extraPrice: 0.10,
            addPremiumLimit: 0.02,
            indexPrice: 96.34,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true).Single();

        Assert.Equal("全清换现金(留底)", decision.ActionInstruction);
        Assert.Equal("极端溢价", decision.StrategyStatus);
        Assert.Equal(1833.69, Math.Round(decision.TargetAmount!.Value, 2));
    }

    [Fact]
    public void OtcReturnRateDoesNotDoubleCountReplayAndOtcDetails()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 100000,
            cost: 199.88,
            accountTotalPositionCost: 30000,
            marketValue: 239.13,
            cash: 100000,
            positionSource: "场外替代",
            includeOtcReplayState: true,
            otcMarketValue: 239.13).Single();

        double expected = (239.13 - 199.88) / 199.88;
        Assert.Equal(expected, decision.ReturnRate!.Value, 6);
        Assert.InRange(decision.ReturnRate.Value, 0.196, 0.197);
        Assert.NotInRange(decision.ReturnRate.Value, 1.39, 1.40);
    }

    [Fact]
    public void ReturnSellSignalFallsThroughToBaseGapWhenAccountBaseIncomplete()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 100000,
            cost: 5809,
            accountTotalPositionCost: 27550,
            marketValue: 12000,
            cash: 100000,
            sellRatio: 0.10,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.Equal(2450, decision.TargetAmount);
    }

    [Fact]
    public void Current159509OrdinaryPremiumDoesNotBlockBaseRefill()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159509",
            principal: 128588.54,
            cost: 199.88,
            accountTotalPositionCost: 27550.77529411765,
            marketValue: 240.448,
            price: 2.776,
            iopv: 2.3175,
            cash: 102304,
            sellRatio: 0.40,
            takeProfitPrice: 0.18,
            extraPrice: 0.23,
            addPremiumLimit: 0.02,
            indexSecId: "251.NDXTMC",
            indexPrice: 97.6495100432985,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: false,
            includeOtcReplayState: true,
            positionSource: "场外替代",
            otcMarketValue: 240.448,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.NotEqual("--", decision.ActionInstruction);
        Assert.NotEqual("底仓保护", decision.StrategyStatus);
        Assert.Equal("战略底仓", decision.ActionInstruction);
        Assert.Equal("逢低吸筹", decision.StrategyStatus);
        Assert.True(decision.IsActionable);
        Assert.Equal(11025.79, Math.Round(decision.TargetAmount!.Value, 2));

        OrderDraftCalculationResult draftResult = new OrderDraftService().Calculate(new OrderDraftCalculationInput
        {
            StrategyDecisions = new[] { decision },
            AccountReplayState = new AccountReplayStateRecord
            {
                ReplayStatus = "正常",
                CashBalance = 102304,
                Principal = 128588.54,
                TotalPositionCost = 27550.77529411765
            },
            MarketQuotes = new[] { Quote("159509", "ETF", 2.776, 2.3175) }
        });
        OrderDraftStateRecord draft = draftResult.Drafts.Single();
        Assert.Equal("买入", draft.Side);
        Assert.Equal("场内ETF", draft.Source);
        Assert.DoesNotContain(draftResult.Legs, leg => leg.Side == "卖出" || leg.Source == "场外替代");
    }

    [Fact]
    public void OtcEnabledAndPremiumAboveLimitPrefersOtc()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            price: 1.06,
            iopv: 1.0,
            cash: 100000,
            addPremiumLimit: 0.03,
            otcEnabled: true).Single();

        Assert.Equal("场外替代", decision.PreferredSource);
    }

    [Fact]
    public void PremiumWithinLimitPrefersExchange()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            price: 1.02,
            iopv: 1.0,
            cash: 100000,
            addPremiumLimit: 0.03,
            otcEnabled: true).Single();

        Assert.Equal("场内直投", decision.PreferredSource);
    }

    [Fact]
    public void HistoryNotReadyDoesNotOutputTiers()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 21000, cash: 100000).Single();

        Assert.Equal("--", decision.ActionInstruction);
        Assert.Equal("T1-T6前置未就绪", decision.StrategyStatus);
        Assert.Equal("未就绪", decision.PrerequisiteStatus);
    }

    [Fact]
    public void FivePercentIndexDrawdownOutputsTierOne()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("狙击一档", decision.ActionInstruction);
        Assert.Equal("狙击一档", decision.TargetTier);
    }

    [Fact]
    public void ThirtyPercentIndexDrawdownOutputsTierSix()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 70,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal("狙击六档", decision.ActionInstruction);
        Assert.Equal("狙击六档", decision.TargetTier);
    }

    [Fact]
    public void ZeroOrNegativeWeightsUseDefaultSixtyThreeParts()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            t1: 0,
            t2: -1,
            t3: 0,
            t4: 0,
            t5: 0,
            t6: 0).Single();

        Assert.Equal(63, decision.TierTotalParts);
    }

    [Fact]
    public void RealSniperPoolUsesCashMinusBaseGapWhenBaseComplete()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 21000, tier: "战略底仓")
            }).Single();

        Assert.Equal(100000, decision.RealSniperPool);
    }

    [Fact]
    public void MemoCycleEndDoesNotOverrideRealSniperPool()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 21000, tier: "战略底仓"),
                Trade(2, "CASH", amount: 0, memo: "周期结束", cashBalance: 50000)
            }).Single();

        Assert.Equal(100000, decision.RealSniperPool);
    }

    [Fact]
    public void RealSniperPoolSubtractsBaseGapWhenBaseIncomplete()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 15000, cash: 80000).Single();

        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(75000, decision.RealSniperPool);
    }

    [Fact]
    public void RealSniperPoolIsZeroWhenCashCannotCoverBaseGap()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 15000, cash: 3000).Single();

        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(0, decision.RealSniperPool);
    }

    [Fact]
    public void CurrentRealDataCaseUsesCashBalanceAsMainSniperPool()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 128578.22,
            cost: 27550.78,
            cash: 102293.68).Single();

        Assert.Equal(25715.644, decision.BaseTargetAmount!.Value, 6);
        Assert.Equal(27550.78, decision.BaseCurrentCost);
        Assert.Equal(0, decision.BaseGapAmount);
        Assert.Equal(102293.68, decision.RealSniperPool);
    }

    [Fact]
    public void ThirtyPercentBaseSubtractsNewGapFromMainSniperPool()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 128578.22,
            cost: 27550.78,
            cash: 102293.68,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal(38573.466, decision.BaseTargetAmount!.Value, 6);
        Assert.Equal(11022.686, decision.BaseGapAmount!.Value, 6);
        Assert.Equal(91270.99, Math.Round(decision.RealSniperPool!.Value, 2));
    }

    [Fact]
    public void TierBudgetUsesVbaStrategicBaseBuysWhenNoCycleEnd()
    {
        StrategyDecisionStateRecord decision = Calculate(
            principal: 128578.22,
            cost: 27550.78,
            cash: 102293.68,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 34859.78, tier: "战略底仓")
            }).Single();

        Assert.Equal(102293.68, decision.RealSniperPool);
        Assert.Equal(93718.44 / 63, decision.TierCumulativeTarget!.Value, 6);
    }

    [Fact]
    public void TierBudgetUsesBaseTargetWhenNoStrategicBaseBuys()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal(100000, decision.RealSniperPool);
        Assert.Equal(80000.0 / 63, decision.TierCumulativeTarget!.Value, 6);
    }

    [Fact]
    public void TierBudgetUsesTierCycleEndCash()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 21000, tier: "战略底仓"),
                Trade(2, "CASH", amount: 0, tier: "周期结束", cashBalance: 50000)
            }).Single();

        Assert.Equal(100000, decision.RealSniperPool);
        Assert.Equal(50000.0 / 63, decision.TierCumulativeTarget!.Value, 6);
    }

    [Fact]
    public void ActionAndMemoCycleEndDoNotAffectTierBudget()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 21000, tier: "战略底仓"),
                Trade(2, "周期结束", amount: 0, memo: "周期结束", cashBalance: 50000)
            }).Single();

        Assert.Equal(100000, decision.RealSniperPool);
        Assert.Equal(79000.0 / 63, decision.TierCumulativeTarget!.Value, 6);
    }

    [Fact]
    public void ExecutedTierAmountStartsAfterTierCycleEnd()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 21000,
            cash: 100000,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 21000, tier: "战略底仓"),
                Trade(2, "买入", amount: 1000, tier: "狙击一档"),
                Trade(3, "CASH", amount: 0, tier: "周期结束", cashBalance: 50000),
                Trade(4, "买入", amount: 500, tier: "狙击一档")
            }).Single();

        Assert.Equal(500, decision.TierExecutedAmount);
        Assert.Equal(50000.0 / 63 - 500, decision.TierRemainAmount!.Value, 6);
    }

    [Fact]
    public void TargetAmountIsCappedByRealSniperPool()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 20000,
            cash: 3000,
            indexPrice: 70,
            indexHigh: 100,
            includeHistory: true).Single();

        Assert.Equal(3000, decision.RealSniperPool);
        Assert.Equal(80000, decision.TierCumulativeTarget);
        Assert.Equal(3000, decision.TargetAmount);
        Assert.Equal("现金上限", decision.StrategyStatus);
    }

    [Fact]
    public void CumulativeTierRemainSubtractsExecutedGridAmount()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 37000,
            cash: 100000,
            indexPrice: 90,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 37000, tier: "战略底仓"),
                Trade(2, "买入", amount: 1000, tier: "狙击一档")
            }).Single();

        Assert.Equal(3000, decision.TierCumulativeTarget);
        Assert.Equal(1000, decision.TierExecutedAmount);
        Assert.Equal(2000, decision.TierRemainAmount);
    }

    [Fact]
    public void CumulativeTierRemainSubtractsGlobalExecutedGridAmountAcrossSymbols()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159513",
            cost: 37000,
            cash: 100000,
            indexPrice: 90,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 37000, tier: "战略底仓", strategyCode: "159513"),
                Trade(2, "买入", amount: 1000, tier: "狙击一档", strategyCode: "159941")
            }).Single();

        Assert.Equal(3000, decision.TierCumulativeTarget);
        Assert.Equal(1000, decision.TierExecutedAmount);
        Assert.Equal(2000, decision.TierRemainAmount);
    }

    [Fact]
    public void GlobalExecutedGridAmountCompletesTierForEverySymbol()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159513",
            cost: 37000,
            accountTotalPositionCost: 37000,
            price: 1.03,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.20,
            extraPrice: 0.30,
            addPremiumLimit: 0.02,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 37000, tier: "战略底仓", strategyCode: "159513"),
                Trade(2, "买入", amount: 1000, tier: "狙击一档", strategyCode: "159941")
            }).Single();

        Assert.Equal(1000, decision.TierCumulativeTarget);
        Assert.Equal(1000, decision.TierExecutedAmount);
        Assert.Equal(0, decision.TierRemainAmount);
        Assert.Equal("一档建仓完成", decision.ActionInstruction);
        Assert.Equal("场外替代", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);
    }

    [Fact]
    public void CompletedTierWithoutEnabledOtcUsesHoldingObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "513300",
            cost: 0,
            accountTotalPositionCost: 37000,
            price: 1.03,
            iopv: 1.0,
            cash: 100000,
            takeProfitPrice: 0.20,
            extraPrice: 0.30,
            addPremiumLimit: 0.02,
            indexPrice: 95,
            indexHigh: 100,
            includeHistory: true,
            otcEnabled: false,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 37000, tier: "战略底仓", strategyCode: "159513"),
                Trade(2, "买入", amount: 1000, tier: "狙击一档", strategyCode: "159941")
            }).Single();

        Assert.Equal("场内直投", decision.PreferredSource);
        Assert.Equal("一档建仓完成", decision.ActionInstruction);
        Assert.Equal("持仓观察", decision.StrategyStatus);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);
    }

    [Fact]
    public void Current159513TierSceneMatchesVbaCompletedOtcState()
    {
        StrategyDecisionStateRecord decision = Calculate(
            strategyCode: "159513",
            principal: 128585.45,
            cost: 14550.29,
            accountTotalPositionCost: 27550.78,
            marketValue: 15266.27,
            price: 1.823,
            iopv: 1.6599,
            cash: 102329.55,
            sellRatio: 0.40,
            takeProfitPrice: 0.10,
            extraPrice: 0.11,
            addPremiumLimit: 0.02,
            indexPrice: 29220.06,
            indexHigh: 30762.20,
            includeHistory: true,
            otcEnabled: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 34859.78, tier: "战略底仓", strategyCode: "159513"),
                Trade(2, "买入", amount: 1571.00, tier: "狙击一档", strategyCode: "159941")
            }).Single();

        Assert.Equal("场外替代", decision.PreferredSource);
        Assert.Equal("一档建仓完成", decision.ActionInstruction);
        Assert.Equal("场外替代", decision.StrategyStatus);
        Assert.True(decision.TierExecutedAmount >= decision.TierCumulativeTarget);
        Assert.Equal(0, decision.TierRemainAmount);
        Assert.False(decision.IsActionable);
        Assert.Null(decision.TargetAmount);
    }

    [Fact]
    public void CashShortageCapsTargetAmount()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 37000,
            cash: 1000,
            indexPrice: 90,
            indexHigh: 100,
            includeHistory: true,
            tradeLogs: new[]
            {
                Trade(1, "买入", amount: 37000, tier: "战略底仓")
            }).Single();

        Assert.Equal("现金上限", decision.StrategyStatus);
        Assert.Equal(1000, decision.TargetAmount);
    }

    [Fact]
    public void SellSignalWithoutSellableExcessFallsThroughToHoldingObservation()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 20000,
            marketValue: 50000,
            cash: 100000,
            sellRatio: 0.10,
            includeHistory: true).Single();

        Assert.NotEqual("止盈减仓(留底)", decision.ActionInstruction);
        Assert.Equal("√ 持股待涨", decision.ActionInstruction);
        Assert.Equal("正常趋势", decision.StrategyStatus);
    }

    [Fact]
    public void StrategyAndOrderServicesDoNotHardCodeSymbolSpecificPremiumRules()
    {
        string strategyService = ReadRepositoryFile(Path.Combine("Core", "Services", "StrategyDecisionService.cs"));
        string orderService = ReadRepositoryFile(Path.Combine("Core", "Services", "OrderDraftService.cs"));

        Assert.DoesNotContain("159509", strategyService);
        Assert.DoesNotContain("159509", orderService);
        Assert.DoesNotContain("TEST_OTC_ONLY", strategyService);
        Assert.DoesNotContain("TEST_OTC_ONLY", orderService);
        Assert.DoesNotContain("strategyCode ==", strategyService);
        Assert.DoesNotContain("strategyCode ==", orderService);
        Assert.DoesNotContain("symbol ==", strategyService);
        Assert.DoesNotContain("symbol ==", orderService);
    }

    private StrategyDecisionStateRecord[] Calculate(
        double cost,
        double cash,
        string strategyCode = "159941",
        string strategyName = "纳指科技ETF",
        string indexSecId = "100.NDX100",
        double principal = 100000,
        double price = 1.0,
        double iopv = 1.0,
        double? marketValue = null,
        double? accountTotalPositionCost = null,
        double? sellRatio = null,
        double? takeProfitPrice = null,
        double? extraPrice = null,
        double? addPremiumLimit = null,
        double? indexPrice = null,
        double? indexHigh = null,
        bool includeHistory = false,
        bool otcEnabled = false,
        bool includeOtcReplayState = false,
        string positionSource = "场内ETF",
        double? otcMarketValue = null,
        double? t1 = null,
        double? t2 = null,
        double? t3 = null,
        double? t4 = null,
        double? t5 = null,
        double? t6 = null,
        BasePositionSettings? settings = null,
        IReadOnlyList<TradeLogRecord>? tradeLogs = null)
    {
        var strategy = new StrategyConfigRecord
        {
            Code = strategyCode,
            Name = strategyName,
            IndexSecId = indexSecId,
            ExtraPrice = extraPrice,
            TakeProfitPrice = takeProfitPrice,
            SellRatio = sellRatio,
            AddPremiumLimit = addPremiumLimit,
            T1Weight = t1,
            T2Weight = t2,
            T3Weight = t3,
            T4Weight = t4,
            T5Weight = t5,
            T6Weight = t6,
            Enabled = true
        };

        var quotes = new List<MarketQuoteRecord>
        {
            Quote(strategyCode, "ETF", price, iopv),
            Quote(indexSecId, "INDEX", indexPrice ?? 100, null),
            Quote("251.NDXTMC", "INDEX", 100, null)
        };
        if (otcEnabled)
        {
            quotes.Add(Quote("016055", "OTC", 1.0, null));
        }

        var history = new List<MarketQuoteRecord>();
        if (includeHistory)
        {
            history.Add(History(indexSecId, indexHigh ?? 100));
            history.Add(History("251.NDXTMC", 100));
            history.Add(History("100.NDX100", 100));
        }

        var input = new StrategyDecisionCalculationInput
        {
            Strategies = new[] { strategy },
            AccountReplayState = new AccountReplayStateRecord
            {
                ReplayStatus = "正常",
                CashBalance = cash,
                Principal = principal,
                TotalPositionCost = accountTotalPositionCost ?? cost
            },
            PositionReplayStates = new[]
            {
                new PositionReplayStateRecord
                {
                    StrategyCode = strategyCode,
                    ActualCode = strategyCode,
                    Source = positionSource,
                    Quantity = cost > 0 ? cost / price : 0,
                    CostAmount = cost,
                    AverageCost = price,
                    MarketPrice = price,
                    MarketValue = marketValue
                }
            },
            OtcPositionReplayStates = includeOtcReplayState
                ? new[]
                {
                    new OtcPositionReplayStateRecord
                    {
                        StrategyCode = strategyCode,
                        ActualCode = "016055",
                        Quantity = cost > 0 ? cost / price : 0,
                        CostAmount = cost,
                        AverageCost = price,
                        Nav = price,
                        MarketValue = otcMarketValue,
                        ReturnRate = otcMarketValue.HasValue && cost > 0 ? (otcMarketValue.Value - cost) / cost : null
                    }
                }
                : Array.Empty<OtcPositionReplayStateRecord>(),
            OtcChannels = otcEnabled
                ? new[] { new OtcChannelRecord { StrategyCode = strategyCode, OtcCode = "016055", ClassType = "A类", Enabled = true } }
                : Array.Empty<OtcChannelRecord>(),
            TradeLogs = tradeLogs ?? Array.Empty<TradeLogRecord>(),
            MarketQuotes = quotes,
            MarketHistory = history,
            BasePositionSettings = settings ?? BasePositionSettings.Default()
        };

        return _service.Calculate(input).Decisions.ToArray();
    }

    private static MarketQuoteRecord Quote(string symbol, string marketType, double price, double? iopv)
    {
        return new MarketQuoteRecord
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = "TEST",
            Price = price,
            Iopv = iopv,
            ReceivedAt = "2026-06-14 12:00:00"
        };
    }

    private static MarketQuoteRecord History(string symbol, double high)
    {
        return new MarketQuoteRecord
        {
            Symbol = symbol,
            MarketType = "INDEX",
            Source = "TEST",
            HighValue = high,
            ReceivedAt = "2026-06-14 12:00:00"
        };
    }

    private static TradeLogRecord Trade(
        long id,
        string action,
        double amount,
        string? tier = null,
        string? memo = null,
        double cashBalance = 0,
        string strategyCode = "159941")
    {
        return new TradeLogRecord
        {
            Id = id,
            Time = $"2026-06-14 12:0{id}:00",
            StrategyCode = strategyCode,
            ActualCode = strategyCode,
            Action = action,
            Amount = amount,
            Tier = tier,
            Memo = memo,
            CashBalance = cashBalance
        };
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
