using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using CrossETF.Terminal.UiShell.Reference;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Order;

public class OrderDraftServiceTests
{
    private readonly OrderDraftService _service = new();

    [Fact]
    public void ExchangeBuyFloorsToBoardLot()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场内直投", 1234.56, price: 1.2),
            account: Account(cash: 10000),
            quotes: new[] { EtfQuote("159941", 1.2) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal(1000, draft.Quantity);
        Assert.Equal(1200, draft.Amount);
    }

    [Fact]
    public void ExchangeBuyRejectsBelowBoardLot()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场内直投", 90, price: 1),
            account: Account(cash: 90),
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Equal("不可执行", draft.DraftStatus);
    }

    [Fact]
    public void ExchangeBuyCapsByRealSniperPool()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "狙击一档", "买入", "场内直投", 10000, price: 2, realSniperPool: 2500),
            account: Account(cash: 10000),
            quotes: new[] { EtfQuote("159941", 2) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.Equal(1200, draft.Quantity);
        Assert.Equal(2400, draft.Amount);
    }

    [Fact]
    public void ExchangeSellCapsByHoldingQuantity()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场内直投", 10000, price: 1, baseCurrentCost: 1000, baseTargetAmount: 0),
            positions: new[] { Position("159941", "159941", 500, 500, 1) },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场内ETF", draft.Source);
        Assert.Equal(500, draft.Quantity);
        Assert.Equal(500, draft.Amount);
        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("场内ETF", leg.Source);
    }

    [Fact]
    public void ExchangeSellProtectsBaseAndFloorsToBoardLot()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场内直投", 1000, price: 1, baseCurrentCost: 1000, baseTargetAmount: 650),
            positions: new[] { Position("159941", "159941", 1000, 1000, 1) },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.Equal(300, draft.Quantity);
        Assert.Equal(300, draft.Amount);
    }

    [Fact]
    public void ExchangeSellUsesEtfQuotePriceInsteadOfSuggestedPrice()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场内直投", 1833.69, price: 8.2442, baseCurrentCost: 10000, baseTargetAmount: 0),
            positions: new[] { Position("159941", "159941", 2000, 2000, 1) },
            quotes: new[] { EtfQuote("159941", 1.677) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场内ETF", draft.Source);
        Assert.Equal(1.677, draft.Price);
        Assert.Equal(1000, draft.Quantity);
        Assert.Equal(1677, draft.Amount);
        Assert.NotEqual(8.2442, draft.Price);
        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal(1.677, leg.Price);
    }

    [Fact]
    public void ExchangeBuyUsesEtfQuotePriceInsteadOfSuggestedPrice()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场内直投", 10000, price: 8.2442),
            account: Account(cash: 10000),
            quotes: new[] { EtfQuote("159941", 2.10) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal(2.10, draft.Price);
        Assert.Equal(4700, draft.Quantity);
        Assert.Equal(9870, draft.Amount);
        Assert.NotEqual(8.2442, draft.Price);
    }

    [Fact]
    public void AnyExchangeEtfUsesEtfQuotePriceInsteadOfSuggestedPrice()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("513100", "战略底仓", "买入", "场内直投", 10000, price: 9.99),
            account: Account(cash: 10000),
            quotes: new[] { EtfQuote("513100", 2.258) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场内ETF", draft.Source);
        Assert.Equal(2.258, draft.Price);
        Assert.Equal(4400, draft.Quantity);
        Assert.Equal(9935.2, draft.Amount, 3);
        Assert.NotEqual(9.99, draft.Price);
    }

    [Fact]
    public void OtcBuyUsesOtcNavInsteadOfEtfQuotePrice()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场外替代", 5000, price: 1.677),
            account: Account(cash: 10000),
            channels: new[] { Channel("159941", "017091", "A类", priority: 1, minBuy: 100) },
            quotes: new[] { EtfQuote("159941", 1.677), OtcQuote("017091", 8.2442) });

        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("场外替代", leg.Source);
        Assert.Null(leg.Price);
        Assert.Equal(8.2442, leg.Nav);
        Assert.NotEqual(1.677, leg.Nav);
    }

    [Fact]
    public void OtcSellUsesOtcNavInsteadOfEtfQuotePrice()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 8244.2, price: 1.677, baseCurrentCost: 20000, baseTargetAmount: 0),
            channels: new[] { Channel("159941", "017092", "C类", priority: 2) },
            otcPositions: new[] { OtcPosition("159941", "017092", 2000, 2000, nav: null) },
            quotes: new[] { EtfQuote("159941", 1.677), OtcQuote("017092", 8.2442) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场外替代", draft.Source);
        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Null(leg.Price);
        Assert.Equal(8.2442, leg.Nav);
        Assert.Equal(1000, leg.Quantity);
        Assert.Equal(8244.2, leg.Amount);
        Assert.NotEqual(1.677, leg.Nav);
    }

    [Fact]
    public void OtcBuySplitsByPriorityAndDailyLimit()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场外替代", 700),
            account: Account(cash: 1000),
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1, dailyLimit: 500, minBuy: 100),
                Channel("159941", "017092", "C类", priority: 2, dailyLimit: 500, minBuy: 100)
            });

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal("017091", result.Legs[0].ActualCode);
        Assert.Equal(500, result.Legs[0].Amount);
        Assert.Equal("017092", result.Legs[1].ActualCode);
        Assert.Equal(200, result.Legs[1].Amount);
    }

    [Fact]
    public void OtcBuySubtractsTodayUsedAmount()
    {
        DateTime today = new(2026, 6, 15);
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场外替代", 500),
            account: Account(cash: 1000),
            channels: new[] { Channel("159941", "017091", "A类", priority: 1, dailyLimit: 500, minBuy: 100) },
            tradeLogs: new[] { Trade("2026-06-15 10:00:00", "159941", "017091", "买入", 300) },
            today: today);

        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal(200, leg.Amount);
    }

    [Fact]
    public void OtcBuyRespectsMinimumBuy()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场外替代", 80),
            account: Account(cash: 1000),
            channels: new[] { Channel("159941", "017091", "A类", priority: 1, dailyLimit: 500, minBuy: 100) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Empty(result.Legs);
    }

    [Fact]
    public void OtcBuyMarksPartialWhenLimitInsufficient()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "战略底仓", "买入", "场外替代", 1000),
            account: Account(cash: 1000),
            channels: new[] { Channel("159941", "017091", "A类", priority: 1, dailyLimit: 300, minBuy: 100) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("部分可委托", draft.DraftStatus);
        Assert.Equal(300, draft.Amount);
    }

    [Fact]
    public void StrategicBaseOtcBuyShowsRealExecutableAmountWhenDailyLimitsAreSmall()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159659", "战略底仓", "逢低吸筹", "场外替代", 36746.44),
            account: Account(cash: 102309.90),
            channels: new[]
            {
                Channel("159659", "019547", "A类", priority: 1, dailyLimit: 100, minBuy: 1),
                Channel("159659", "019548", "C类", priority: 2, dailyLimit: 100, minBuy: 1)
            });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("部分可委托", draft.DraftStatus);
        Assert.Equal(200, draft.Amount);
        Assert.Equal(2, result.Legs.Count);
        Assert.Equal(100, result.Legs[0].Amount);
        Assert.Equal(100, result.Legs[1].Amount);
    }

    [Fact]
    public void OtcSellUsesCClassBeforeAClass()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 1500, baseCurrentCost: 3000, baseTargetAmount: 0),
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1),
                OtcPosition("159941", "017092", 1000, 1000, nav: 1)
            });

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal("017092", result.Legs[0].ActualCode);
        Assert.Equal("C类", result.Legs[0].ChannelClass);
        Assert.Equal(1000, result.Legs[0].Amount);
        Assert.Equal("017091", result.Legs[1].ActualCode);
        Assert.Equal(500, result.Legs[1].Amount);
    }

    [Fact]
    public void OtcSellUsesOnlyCClassWhenTargetFitsCClass()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 500, baseCurrentCost: 3000, baseTargetAmount: 0),
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1),
                OtcPosition("159941", "017092", 1000, 1000, nav: 1)
            });

        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("017092", leg.ActualCode);
        Assert.Equal("C类", leg.ChannelClass);
        Assert.Equal(500, leg.Amount);
    }

    [Fact]
    public void OtcSellDoesNotCreateLegForMissingHolding()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 500, baseCurrentCost: 3000, baseTargetAmount: 0),
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1)
            });

        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("017091", leg.ActualCode);
        Assert.DoesNotContain(result.Legs, item => item.ActualCode == "017092");
    }

    [Fact]
    public void SellUsesExchangeBeforeOtcWhenExchangeCanCoverTarget()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 500, price: 1, baseCurrentCost: 5000, baseTargetAmount: 0),
            positions: new[] { Position("159941", "159941", 1000, 1000, 1) },
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1),
                OtcPosition("159941", "017092", 1000, 1000, nav: 1)
            },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场内ETF", draft.Source);
        Assert.Equal(500, draft.Quantity);
        Assert.Equal(500, draft.Amount);
        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("场内ETF", leg.Source);
        Assert.Equal("159941", leg.ActualCode);

        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay("159941", result.Drafts, result.Legs);
        Assert.Equal("500股", display.Text);
    }

    [Fact]
    public void SellUsesOtcCClassAfterExchangeCannotCoverTarget()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场内直投", 1500, price: 1, baseCurrentCost: 5000, baseTargetAmount: 0),
            positions: new[] { Position("159941", "159941", 200, 200, 1) },
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1),
                OtcPosition("159941", "017092", 1000, 1000, nav: 1)
            },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场内ETF+场外替代", draft.Source);
        Assert.Equal(200, draft.Quantity);
        Assert.Equal(1500, draft.Amount);
        Assert.Equal(3, result.Legs.Count);
        Assert.Equal("场内ETF", result.Legs[0].Source);
        Assert.Equal("场外替代", result.Legs[1].Source);
        Assert.Equal("017092", result.Legs[1].ActualCode);
        Assert.Equal("C类", result.Legs[1].ChannelClass);
        Assert.Equal("017091", result.Legs[2].ActualCode);
        Assert.Equal("A类", result.Legs[2].ChannelClass);

        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay("159941", result.Drafts, result.Legs);
        Assert.Equal("200股", display.Text);
        Assert.Contains("拆单：3笔", display.ToolTip);
        Assert.True(display.ToolTip!.IndexOf("159941", StringComparison.Ordinal) < display.ToolTip.IndexOf("017092 C类", StringComparison.Ordinal));
        Assert.True(display.ToolTip.IndexOf("017092 C类", StringComparison.Ordinal) < display.ToolTip.IndexOf("017091 A类", StringComparison.Ordinal));
    }

    [Fact]
    public void SellFallsBackToOtcWhenExchangeWouldBreakBase()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场内直投", 100, price: 1, baseCurrentCost: 3000, baseTargetAmount: 2500, baseRatio: 0.25),
            account: Account(cash: 100000, principal: 10000, totalPositionCost: 3000),
            positions: new[] { Position("159941", "159941", 100, 1000, 10) },
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 1000, 1000, nav: 1),
                OtcPosition("159941", "017092", 1000, 1000, nav: 1)
            },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("场外替代", draft.Source);
        OrderDraftLegStateRecord leg = Assert.Single(result.Legs);
        Assert.Equal("场外替代", leg.Source);
        Assert.Equal("017092", leg.ActualCode);
        Assert.Equal("C类", leg.ChannelClass);
        Assert.Equal(100, leg.Amount);
    }

    [Fact]
    public void PremiumSellWithoutExchangeHoldingDoesNotFallbackToOtcSell()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("888005", "溢价达标减仓(留底)", "溢价止盈", "场外替代", 500, baseCurrentCost: 1000, baseTargetAmount: 0),
            channels: new[] { Channel("888005", "017092", "C类", priority: 1) },
            otcPositions: new[] { OtcPosition("888005", "017092", 1000, 1000, nav: 1) },
            quotes: new[] { EtfQuote("888005", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Equal("场内ETF", draft.Source);
        Assert.Empty(result.Legs);
        Assert.DoesNotContain(result.Legs, leg => leg.Source == "场外替代");
        Assert.Contains("场内 ETF", draft.Reason);
    }

    [Fact]
    public void OtcSellRequiresRealNav()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 500, baseCurrentCost: 1000, baseTargetAmount: 0),
            channels: new[] { Channel("159941", "017092", "C类", priority: 1) },
            otcPositions: new[] { OtcPosition("159941", "017092", 1000, 1000, nav: null) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Empty(result.Legs);
    }

    [Fact]
    public void OtcSellCapsByPositionQuantity()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision("159941", "止盈减仓(留底)", "卖出", "场外替代", 5000, baseCurrentCost: 10000, baseTargetAmount: 0),
            channels: new[] { Channel("159941", "017092", "C类", priority: 1) },
            otcPositions: new[] { OtcPosition("159941", "017092", 100, 1000, nav: 2) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("部分可委托", draft.DraftStatus);
        Assert.Equal(100, draft.Quantity);
        Assert.Equal(200, draft.Amount);
    }

    [Fact]
    public void ExchangeSellRejectsWhenPostTradeWouldBreakDynamicBase()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision(
                "159941",
                "止盈减仓(留底)",
                "卖出",
                "场内直投",
                10000,
                price: 1,
                baseCurrentCost: 30000,
                baseTargetAmount: 30000,
                baseRatio: 0.30),
            account: Account(cash: 100000, principal: 100000, totalPositionCost: 30000),
            positions: new[] { Position("159941", "159941", 30000, 30000, 1) },
            quotes: new[] { EtfQuote("159941", 1) });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Equal("不可执行", draft.DraftStatus);
        Assert.Contains("底仓保护", draft.Reason);
    }

    [Fact]
    public void OtcMultiSellShrinksWhenPostTradeWouldBreakBase()
    {
        OrderDraftCalculationResult result = Calculate(
            Decision(
                "159941",
                "止盈减仓(留底)",
                "卖出",
                "场外替代",
                10000,
                baseCurrentCost: 30000,
                baseTargetAmount: 25000,
                baseRatio: 0.25),
            account: Account(cash: 100000, principal: 100000, totalPositionCost: 30000),
            channels: new[]
            {
                Channel("159941", "017091", "A类", priority: 1),
                Channel("159941", "017092", "C类", priority: 2)
            },
            otcPositions: new[]
            {
                OtcPosition("159941", "017091", 10000, 10000, nav: 1),
                OtcPosition("159941", "017092", 3000, 3000, nav: 1)
            });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.True(draft.IsExecutable);
        Assert.Equal("部分可委托", draft.DraftStatus);
        Assert.InRange(draft.Amount, 4990, 5010);
        Assert.Equal("017092", result.Legs[0].ActualCode);
        Assert.Equal("017091", result.Legs[1].ActualCode);
    }

    [Fact]
    public void DynamicBaseRatioChangesSellProtection()
    {
        OrderDraftCalculationResult twentyPercent = Calculate(
            Decision(
                "159941",
                "止盈减仓(留底)",
                "卖出",
                "场内直投",
                10000,
                price: 1,
                baseCurrentCost: 30000,
                baseTargetAmount: 20000,
                baseRatio: 0.20),
            account: Account(cash: 100000, principal: 100000, totalPositionCost: 30000),
            positions: new[] { Position("159941", "159941", 30000, 30000, 1) },
            quotes: new[] { EtfQuote("159941", 1) });
        OrderDraftCalculationResult thirtyPercent = Calculate(
            Decision(
                "159941",
                "止盈减仓(留底)",
                "卖出",
                "场内直投",
                10000,
                price: 1,
                baseCurrentCost: 30000,
                baseTargetAmount: 30000,
                baseRatio: 0.30),
            account: Account(cash: 100000, principal: 100000, totalPositionCost: 30000),
            positions: new[] { Position("159941", "159941", 30000, 30000, 1) },
            quotes: new[] { EtfQuote("159941", 1) });

        Assert.True(Assert.Single(twentyPercent.Drafts).IsExecutable);
        Assert.False(Assert.Single(thirtyPercent.Drafts).IsExecutable);
    }

    [Fact]
    public void NonActionableDecisionCreatesNonExecutableDraft()
    {
        OrderDraftCalculationResult result = Calculate(new StrategyDecisionStateRecord
        {
            StrategyCode = "159941",
            ActionInstruction = "--",
            StrategyStatus = "空仓观察",
            PreferredSource = "场内直投",
            IsActionable = false
        });

        OrderDraftStateRecord draft = Assert.Single(result.Drafts);
        Assert.False(draft.IsExecutable);
        Assert.Equal("NONE", draft.Side);
        Assert.Equal("不可执行", draft.DraftStatus);
    }

    [Fact]
    public void RepositoryFinalizationPersistsDraftAndDoesNotWriteTradeLog()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_order_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            OrderDraftCalculationResult result = Calculate(
                Decision("159941", "战略底仓", "买入", "场内直投", 1000, price: 1),
                account: Account(cash: 1000),
                quotes: new[] { EtfQuote("159941", 1) });

            repository.SaveOrderDraftStates(result.Drafts, result.Legs);
            int count = repository.FinalizeOrderDrafts(repository.ReadOrderDraftStates(), repository.ReadOrderDraftLegStates(), "单元测试定稿");

            Assert.Equal(1, count);
            OrderFinalizationStateRecord finalization = Assert.Single(repository.ReadOrderFinalizationStates());
            Assert.Equal("159941", finalization.StrategyCode);
            Assert.Equal(1000, finalization.Amount);
            Assert.Single(repository.ReadOrderFinalizationLegStates());
            Assert.Empty(repository.ReadTradeLogs());
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysExchangeBuyShares()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { Draft("159941", "买入", "场内ETF", quantity: 1000, amount: 1672, price: 1.672) },
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("1000股", display.Text);
        Assert.Contains("数量：1000股", display.ToolTip);
        Assert.Contains("金额：1672.00元", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysExchangeSellShares()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { Draft("159941", "卖出", "场内ETF", quantity: 600, amount: 1000.2, price: 1.667) },
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("600股", display.Text);
        Assert.Contains("方向：卖出", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryTooltipUsesDraftExecutionPrice()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { Draft("159941", "卖出", "场内ETF", quantity: 1000, amount: 1677, price: 1.677) },
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("1000股", display.Text);
        Assert.Contains("价格：1.677", display.ToolTip);
        Assert.Contains("数量：1000股", display.ToolTip);
        Assert.DoesNotContain("8.2442", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysSingleOtcBuyAmount()
    {
        OrderDraftStateRecord draft = Draft("159941", "买入", "场外替代", quantity: 0, amount: 5000);
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { draft },
            new[] { Leg(draft, "017091", "A类", quantity: 0, amount: 5000) });

        Assert.Equal("5000.00元", display.Text);
        Assert.Contains("017091 A类 5000.00元", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysMultiOtcBuyLegs()
    {
        OrderDraftStateRecord draft = Draft("159941", "买入", "场外替代", quantity: 0, amount: 8000);
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { draft },
            new[]
            {
                Leg(draft, "017091", "A类", quantity: 0, amount: 5000, priority: 1),
                Leg(draft, "017092", "C类", quantity: 0, amount: 3000, priority: 2)
            });

        Assert.Equal("8000.00元", display.Text);
        Assert.DoesNotContain("多通道 2笔", display.Text);
        Assert.Contains("拆单：多通道 2笔", display.ToolTip);
        Assert.Contains("017091 A类 5000.00元", display.ToolTip);
        Assert.Contains("017092 C类 3000.00元", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysOtcSellShares()
    {
        OrderDraftStateRecord draft = Draft("159941", "卖出", "场外替代", quantity: 123.4567, amount: 5000);
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { draft },
            new[] { Leg(draft, "017092", "C类", quantity: 123.4567, amount: 5000, nav: 40.5) });

        Assert.Equal("123.4567份", display.Text);
        Assert.Contains("赎回份额：123.4567份", display.ToolTip);
        Assert.Contains("估算赎回金额：5000.00元", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryDisplaysMultiOtcSellTotalShares()
    {
        OrderDraftStateRecord draft = Draft("159941", "卖出", "场外替代", quantity: 123.4567, amount: 5000);
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { draft },
            new[]
            {
                Leg(draft, "017091", "A类", quantity: 23.4567, amount: 2000, nav: 85.26, priority: 1),
                Leg(draft, "017092", "C类", quantity: 100.0000, amount: 3000, nav: 30, priority: 2)
            });

        Assert.Equal("123.4567份", display.Text);
        Assert.DoesNotContain("多通道 2笔", display.Text);
        Assert.Contains("拆单：多通道 2笔", display.ToolTip);
        Assert.Contains("017092 C类 100.0000份 估算金额 3000.00元", display.ToolTip);
        Assert.Contains("017091 A类 23.4567份 估算金额 2000.00元", display.ToolTip);
        Assert.True(display.ToolTip!.IndexOf("017092 C类", StringComparison.Ordinal) < display.ToolTip.IndexOf("017091 A类", StringComparison.Ordinal));
    }

    [Fact]
    public void EtfTableDraftSummaryNeverShowsMultiChannelAsMainValue()
    {
        OrderDraftStateRecord draft = Draft("159941", "买入", "场外替代", quantity: 0, amount: 8000);
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { draft },
            new[]
            {
                Leg(draft, "017091", "A类", quantity: 0, amount: 5000),
                Leg(draft, "017092", "C类", quantity: 0, amount: 3000)
            });

        Assert.Equal("8000.00元", display.Text);
        Assert.NotEqual("多通道 2笔", display.Text);
    }

    [Fact]
    public void EtfTableDraftSummaryHidesNonExecutableDraft()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { Draft("159941", "买入", "场内ETF", quantity: 0, amount: 0, status: "不可执行", reason: "金额不足100股", executable: false) },
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("--", display.Text);
        Assert.Contains("金额不足100股", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryHidesPriceForNonExecutableDraft()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            new[] { Draft("159941", "卖出", "场内ETF", quantity: 0, amount: 0, price: 8.2442, status: "不可执行", reason: "底仓保护", executable: false) },
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("--", display.Text);
        Assert.Contains("底仓保护", display.ToolTip);
        Assert.DoesNotContain("8.2442", display.ToolTip);
    }

    [Fact]
    public void EtfTableDraftSummaryHidesMissingDraft()
    {
        MainWindow.EtfOrderDraftDisplay display = MainWindow.BuildEtfOrderDraftDisplay(
            "159941",
            Array.Empty<OrderDraftStateRecord>(),
            Array.Empty<OrderDraftLegStateRecord>());

        Assert.Equal("--", display.Text);
        Assert.Null(display.ToolTip);
    }

    private OrderDraftCalculationResult Calculate(
        StrategyDecisionStateRecord decision,
        AccountReplayStateRecord? account = null,
        IReadOnlyList<PositionReplayStateRecord>? positions = null,
        IReadOnlyList<OtcPositionReplayStateRecord>? otcPositions = null,
        IReadOnlyList<OtcChannelRecord>? channels = null,
        IReadOnlyList<TradeLogRecord>? tradeLogs = null,
        IReadOnlyList<MarketQuoteRecord>? quotes = null,
        DateTime? today = null)
    {
        return _service.Calculate(new OrderDraftCalculationInput
        {
            StrategyDecisions = new[] { decision },
            AccountReplayState = account,
            PositionReplayStates = positions ?? Array.Empty<PositionReplayStateRecord>(),
            OtcPositionReplayStates = otcPositions ?? Array.Empty<OtcPositionReplayStateRecord>(),
            OtcChannels = channels ?? Array.Empty<OtcChannelRecord>(),
            TradeLogs = tradeLogs ?? Array.Empty<TradeLogRecord>(),
            MarketQuotes = quotes ?? Array.Empty<MarketQuoteRecord>(),
            Today = today ?? DateTime.Today
        });
    }

    private static StrategyDecisionStateRecord Decision(
        string code,
        string action,
        string side,
        string source,
        double targetAmount,
        double? price = null,
        double? realSniperPool = null,
        double? baseCurrentCost = null,
        double? baseTargetAmount = null,
        double? baseRatio = null,
        double? baseFixedAmount = null,
        string? baseMode = null)
    {
        return new StrategyDecisionStateRecord
        {
            StrategyCode = code,
            Name = code,
            ActionInstruction = action,
            StrategyStatus = side,
            PreferredSource = source,
            TargetAmount = targetAmount,
            SuggestedPrice = price,
            RealSniperPool = realSniperPool,
            BaseCurrentCost = baseCurrentCost,
            BaseTargetAmount = baseTargetAmount,
            BaseMode = baseMode ?? BasePositionSettings.RatioMode,
            BaseRatio = baseRatio ?? BasePositionSettings.DefaultRatio,
            BaseFixedAmount = baseFixedAmount,
            AvailableCash = 100000,
            IsActionable = true
        };
    }

    private static AccountReplayStateRecord Account(double cash, double principal = 100000, double totalPositionCost = 0)
        => new()
        {
            CalculatedAt = "2026-06-15 10:00:00",
            ReplayStatus = "正常",
            CashBalance = cash,
            Principal = principal,
            TotalPositionCost = totalPositionCost
        };

    private static PositionReplayStateRecord Position(string strategyCode, string actualCode, double quantity, double costAmount, double averageCost)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = "场内ETF",
            Quantity = quantity,
            CostAmount = costAmount,
            AverageCost = averageCost
        };

    private static OtcPositionReplayStateRecord OtcPosition(string strategyCode, string actualCode, double quantity, double costAmount, double? nav)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Quantity = quantity,
            CostAmount = costAmount,
            AverageCost = quantity > 0 ? costAmount / quantity : 0,
            Nav = nav
        };

    private static OtcChannelRecord Channel(string strategyCode, string otcCode, string classType, int priority, double dailyLimit = 0, double minBuy = 0)
        => new()
        {
            StrategyCode = strategyCode,
            OtcCode = otcCode,
            ClassType = classType,
            Enabled = true,
            Priority = priority,
            DailyLimit = dailyLimit,
            MinBuy = minBuy
        };

    private static MarketQuoteRecord EtfQuote(string symbol, double price)
        => new()
        {
            Symbol = symbol,
            MarketType = "ETF",
            Source = "TEST",
            Price = price,
            ReceivedAt = "2026-06-15 10:00:00"
        };

    private static MarketQuoteRecord OtcQuote(string symbol, double nav)
        => new()
        {
            Symbol = symbol,
            MarketType = "OTC",
            Source = "TEST",
            Price = nav,
            ReceivedAt = "2026-06-15 10:00:00"
        };

    private static TradeLogRecord Trade(string time, string strategyCode, string actualCode, string action, double amount)
        => new()
        {
            Time = time,
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Action = action,
            Amount = amount
        };

    private static OrderDraftStateRecord Draft(
        string strategyCode,
        string side,
        string source,
        double quantity,
        double amount,
        double? price = null,
        string status = "草案",
        string? reason = null,
        bool executable = true)
        => new()
        {
            Id = 1,
            DraftKey = strategyCode + "|" + side + "|" + source,
            CalculatedAt = "2026-06-15 10:00:00",
            SnapshotKey = "TEST",
            StrategyCode = strategyCode,
            Side = side,
            Source = source,
            Price = price,
            Quantity = quantity,
            Amount = amount,
            DraftStatus = status,
            Reason = reason,
            IsExecutable = executable
        };

    private static OrderDraftLegStateRecord Leg(
        OrderDraftStateRecord draft,
        string actualCode,
        string? classType,
        double quantity,
        double amount,
        double? price = null,
        double? nav = null,
        int priority = 1)
        => new()
        {
            Id = priority,
            DraftId = draft.Id,
            DraftKey = draft.DraftKey,
            CalculatedAt = draft.CalculatedAt,
            SnapshotKey = draft.SnapshotKey,
            StrategyCode = draft.StrategyCode,
            ActualCode = actualCode,
            Side = draft.Side,
            Source = draft.Source,
            ChannelClass = classType,
            Priority = priority,
            Price = price,
            Nav = nav,
            Quantity = quantity,
            Amount = amount,
            LegStatus = draft.DraftStatus,
            Reason = draft.Reason
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
