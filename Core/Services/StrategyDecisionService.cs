using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

// LOCKED: VBA-parity strategy behavior. Do not change without docs/LOCKED_MODULES.md and user confirmation.
public sealed class StrategyDecisionService
{
    private const double MoneyEpsilon = 0.01;
    private static readonly double[] DefaultTierWeights = { 1, 2, 4, 8, 16, 32 };
    private static readonly string[] RequiredIndexHistorySymbols = { "251.NDXTMC", "100.NDX100" };
    private static readonly string[] TierNames =
    {
        "狙击一档",
        "狙击二档",
        "狙击三档",
        "狙击四档",
        "狙击五档",
        "狙击六档"
    };

    public StrategyDecisionCalculationResult Calculate(StrategyDecisionCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var warnings = new List<StrategyDecisionRuntimeWarning>();
        var decisions = new List<StrategyDecisionStateRecord>();
        string calculatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (input.Strategies.Count == 0)
        {
            warnings.Add(Warning("策略配置缺失", "本地 strategy_config 未配置任何 ETF 策略。"));
            return new StrategyDecisionCalculationResult(decisions, warnings);
        }

        double principal = GetPrincipal(input);
        BasePositionSettings baseSettings = BasePositionSettingsService.Normalize(input.BasePositionSettings);
        BasePositionTargetResult baseTargetResult = BasePositionSettingsService.ResolveBaseTarget(principal, baseSettings);
        double availableCash = Math.Max(0, input.AccountReplayState?.CashBalance ?? input.AccountState?.CashBalance ?? 0);
        double totalPositionCost = CalculateTotalPositionCost(input);
        double accountBaseGap = Math.Max(0, baseTargetResult.TargetAmount - totalPositionCost);
        double realSniperPool = CalculateRealSniperPool(availableCash, accountBaseGap);
        bool accountReplayError = string.Equals(input.AccountReplayState?.ReplayStatus, "财务异常", StringComparison.Ordinal);
        bool globalHistoryReady = !input.RequireGlobalHistoryReady
                                  || RequiredIndexHistorySymbols.All(symbol => HasUsableHistory(input, symbol, "INDEX"));
        string globalHistoryMessage = globalHistoryReady
            ? "T1-T6 全局历史 K 线前置已就绪。"
            : "251.NDXTMC / 100.NDX100 历史 K 线未成功返回。";
        if (!globalHistoryReady)
        {
            warnings.Add(Warning("T1-T6 前置未就绪", globalHistoryMessage));
        }

        SniperPoolContext poolContext = CalculateSniperPoolContext(
            input.TradeLogs,
            principal,
            baseTargetResult.TargetAmount,
            realSniperPool);

        foreach (StrategyConfigRecord strategy in input.Strategies)
        {
            StrategyDecisionStateRecord decision = CreateBaseDecision(strategy, calculatedAt, availableCash, poolContext.RealSniperPool);
            decisions.Add(decision);

            double[] weights = ResolveTierWeights(strategy);
            double totalParts = weights.Sum();
            decision.TierTotalParts = totalParts;

            string etfCode = DigitsOnly(strategy.Code);
            string? indexSymbol = NormalizeSecId(strategy.IndexSecId);
            MarketQuoteRecord? etfQuote = FindQuote(input.MarketQuotes, etfCode, "ETF");
            MarketQuoteRecord? indexQuote = FindQuote(input.MarketQuotes, indexSymbol, "INDEX");
            MarketQuoteRecord? etfHistory = FindQuote(input.MarketHistory, etfCode, "ETF");
            MarketQuoteRecord? indexHistory = FindQuote(input.MarketHistory, indexSymbol, "INDEX");
            EtfPositionCostMetrics costMetrics = CalculateCostMetrics(input, strategy);
            EtfPositionCostMetrics exchangeCostMetrics = CalculateExchangeCostMetrics(input, strategy);
            double currentCost = costMetrics.TotalCostAmount;
            double exchangeCost = exchangeCostMetrics.TotalCostAmount;
            double baseTarget = baseTargetResult.TargetAmount;
            double baseTolerance = principal > 0 ? Math.Max(1, principal * 0.001) : 0;
            double baseGap = accountBaseGap;
            double accountSellableExcessCost = Math.Max(0, totalPositionCost - baseTarget);
            double baseCompletion = BasePositionSettingsService.CalculateCompletionRate(totalPositionCost, baseTarget);
            double? premium = EtfDecisionTableMetrics.CalculatePremiumRate(etfQuote);
            double? returnRate = CalculateReturnRate(input, strategy, currentCost);
            double? etfDrawdown = CalculateDrawdown(etfQuote?.Price, FirstPositive(etfHistory?.HighValue, strategy.EtfHigh));
            double? indexDrawdown = CalculateDrawdown(indexQuote?.Price, indexHistory?.HighValue);
            IReadOnlyList<OtcChannelRecord> enabledOtcChannels = input.OtcChannels
                .Where(channel => channel.Enabled && SameCode(channel.StrategyCode, strategy.Code))
                .ToArray();
            string preferredSource = ShouldPreferOtc(enabledOtcChannels, premium, strategy.AddPremiumLimit)
                ? "场外替代"
                : "场内直投";
            double? suggestedPrice = preferredSource == "场外替代"
                ? FindFirstOtcPrice(input, enabledOtcChannels) ?? etfQuote?.Price
                : etfQuote?.Price;

            decision.PreferredSource = preferredSource;
            decision.SuggestedPrice = suggestedPrice;
            decision.Premium = premium;
            decision.ReturnRate = returnRate;
            decision.EtfDrawdown = etfDrawdown;
            decision.IndexDrawdown = indexDrawdown;
            decision.BaseMode = baseTargetResult.Mode;
            decision.BaseRatio = baseTargetResult.Ratio;
            decision.BaseFixedAmount = baseTargetResult.FixedAmount;
            decision.BaseTargetAmount = baseTarget > 0 ? baseTarget : null;
            decision.BaseCurrentCost = totalPositionCost > 0 ? totalPositionCost : 0;
            decision.BaseCompletionRate = baseCompletion;
            decision.BaseGapAmount = baseGap > 0 ? baseGap : 0;
            decision.BaseTargetCapped = baseTargetResult.IsCappedToPrincipal;
            if (baseTargetResult.IsCappedToPrincipal)
            {
                warnings.Add(Warning("底仓基准封顶", $"{strategy.Code} 固定底仓金额超过本金，已按本金封顶。"));
            }

            bool indexHistoryReady = !string.IsNullOrWhiteSpace(indexSymbol) && HasUsableHistory(input, indexSymbol, "INDEX");
            decision.PrerequisiteStatus = globalHistoryReady && indexHistoryReady ? "已就绪" : "未就绪";
            decision.PrerequisiteMessage = BuildPrerequisiteMessage(indexSymbol, globalHistoryReady, indexHistoryReady, globalHistoryMessage);

            if (!strategy.Enabled)
            {
                ApplyNoAction(decision, "本地停用", "ETF 策略未启用。");
                warnings.Add(Warning("策略配置缺失", $"{strategy.Code} 当前未启用。"));
                continue;
            }

            if (accountReplayError)
            {
                ApplyNoAction(decision, "前置异常", input.AccountReplayState?.ReplayError ?? "账户回放状态为财务异常。");
                warnings.Add(Warning("账户回放异常", $"{strategy.Code} 账户回放状态为财务异常。"));
                continue;
            }

            if (principal <= 0)
            {
                ApplyNoAction(decision, "前置异常", "本金无法计算，策略决策暂停。");
                warnings.Add(Warning("持仓数据异常", $"{strategy.Code} 本金无法计算。"));
                continue;
            }

            if (!HasPositiveValue(etfQuote?.Price))
            {
                ApplyNoAction(decision, "行情缺失", "ETF 真实行情价格缺失。");
                warnings.Add(Warning("行情缺失", $"{strategy.Code} ETF 真实行情价格缺失。"));
                continue;
            }

            bool hasHolding = HasHolding(costMetrics);
            bool hasExchangeHolding = HasHolding(exchangeCostMetrics);
            bool hasOtcHolding = HasOtcSubstituteHolding(input, strategy);
            bool extremePremiumHit = IsThresholdHit(premium, strategy.ExtraPrice);
            bool premiumTakeProfitHit = IsThresholdHit(premium, strategy.TakeProfitPrice);
            bool returnTakeProfitHit = IsThresholdHit(returnRate, strategy.SellRatio);
            bool deferOtcPremiumTakeProfit = premiumTakeProfitHit
                                             && ShouldDeferOtcPremiumTakeProfit(input, strategy, preferredSource);
            bool effectivePremiumTakeProfitHit = premiumTakeProfitHit && !deferOtcPremiumTakeProfit;
            double sellTarget = CalculateSellTarget(currentCost, accountSellableExcessCost);
            double exchangeSellTarget = CalculateSellTarget(exchangeCost, accountSellableExcessCost);

            if (baseGap > baseTolerance)
            {
                ApplyBuyDecision(
                    decision,
                    "战略底仓",
                    availableCash > 0 && availableCash < baseGap ? "现金上限" : "逢低吸筹",
                    CapBuyTarget(baseGap, availableCash),
                    availableCash);
                if (availableCash > 0 && availableCash < baseGap)
                {
                    warnings.Add(Warning("可用现金不足", $"{strategy.Code} 战略底仓缺口 {baseGap:0.##}，现金上限 {availableCash:0.##}。"));
                }

                continue;
            }

            if (extremePremiumHit)
            {
                if (!hasExchangeHolding)
                {
                    if (hasOtcHolding)
                    {
                        ApplyNoAction(decision, "场外替代", "当前仅有场外替代持仓，场内极端溢价不触发场外卖出。");
                        decision.ActionInstruction = "√ 持股待涨";
                    }
                    else
                    {
                        ApplyNoAction(decision, "禁止建仓", "当前场内溢价达到极端阈值，禁止建仓。");
                        decision.ActionInstruction = "极端溢价";
                    }

                    continue;
                }

                if (exchangeSellTarget <= MoneyEpsilon)
                {
                    ApplyNoAction(decision, "底仓保护", "账户级可卖超额成本不足，不输出减仓建议。");
                    continue;
                }

                ApplySellDecision(decision, "全清换现金(留底)", "极端溢价", exchangeSellTarget);
                continue;
            }

            if (hasExchangeHolding && effectivePremiumTakeProfitHit && exchangeSellTarget > MoneyEpsilon)
            {
                ApplySellDecision(decision, "溢价达标减仓(留底)", "溢价止盈", exchangeSellTarget);
                continue;
            }

            if (hasHolding && returnTakeProfitHit && sellTarget > MoneyEpsilon)
            {
                ApplySellDecision(decision, "止盈减仓(留底)", "收益达标", sellTarget);
                continue;
            }

            if (!globalHistoryReady || !indexHistoryReady || !indexDrawdown.HasValue)
            {
                ApplyNoAction(decision, "T1-T6前置未就绪", decision.PrerequisiteMessage ?? "历史 K 线未成功返回。");
                warnings.Add(Warning("历史高点未就绪", $"{strategy.Code} {decision.PrerequisiteMessage}"));
                continue;
            }

            int tierLevel = GetTriggeredTierLevel(indexDrawdown.Value);
            if (tierLevel <= 0)
            {
                if (hasHolding)
                {
                    ApplyHoldingObservation(decision);
                }
                else
                {
                    ApplyNoAction(decision, "空仓观察", "指数回撤尚未触发 T1-T6 档位。");
                    decision.ActionInstruction = "等待建仓";
                }

                continue;
            }

            double cumulativeTarget = poolContext.TierBudgetBase * weights.Take(tierLevel).Sum() / totalParts;
            double executedAmount = CalculateExecutedTierAmount(poolContext.CurrentCycleTradeLogs);
            double remainAmount = Math.Max(0, cumulativeTarget - executedAmount);
            decision.TargetTier = TierNames[tierLevel - 1];
            decision.TierCumulativeTarget = cumulativeTarget;
            decision.TierExecutedAmount = executedAmount;
            decision.TierRemainAmount = remainAmount;

            if (deferOtcPremiumTakeProfit)
            {
                ApplyNoAction(decision, $"{TierLevelText(tierLevel)}建仓完成", "当前仅有场外替代持仓，普通溢价止盈不提前触发场外卖出。");
                decision.ActionInstruction = $"{TierLevelText(tierLevel)}建仓完成";
                decision.StrategyStatus = "场外替代";
                continue;
            }

            if (remainAmount <= 0.01)
            {
                ApplyNoAction(decision, $"{TierLevelText(tierLevel)}建仓完成", "当前触发档位累计目标已完成。");
                decision.ActionInstruction = $"{TierLevelText(tierLevel)}建仓完成";
                decision.StrategyStatus = preferredSource == "场外替代" ? "场外替代" : "持仓观察";
                continue;
            }

            string status = preferredSource == "场外替代" ? "溢价替代" : "资金配置";
            double actionableCash = Math.Min(availableCash, poolContext.RealSniperPool);
            double targetAmount = CapBuyTarget(remainAmount, actionableCash);
            if (actionableCash <= 0)
            {
                status = "现金不足";
                warnings.Add(Warning("可用现金不足", $"{strategy.Code} 现金不足，T{tierLevel} 不输出可执行金额。"));
            }
            else if (actionableCash < remainAmount)
            {
                status = "现金上限";
                warnings.Add(Warning("可用现金不足", $"{strategy.Code} T{tierLevel} 目标 {remainAmount:0.##}，实盘狙击资金池上限 {actionableCash:0.##}。"));
            }

            ApplyBuyDecision(decision, $"狙击{TierLevelText(tierLevel)}", status, targetAmount, availableCash);
        }

        return new StrategyDecisionCalculationResult(decisions, warnings);
    }

    private static StrategyDecisionStateRecord CreateBaseDecision(
        StrategyConfigRecord strategy,
        string calculatedAt,
        double availableCash,
        double realSniperPool)
    {
        return new StrategyDecisionStateRecord
        {
            CalculatedAt = calculatedAt,
            StrategyCode = strategy.Code,
            Name = strategy.Name,
            ActionInstruction = "--",
            StrategyStatus = "正常趋势",
            AvailableCash = availableCash,
            RealSniperPool = realSniperPool,
            IsActionable = false
        };
    }

    private static void ApplyNoAction(StrategyDecisionStateRecord decision, string status, string message)
    {
        decision.ActionInstruction = "--";
        decision.StrategyStatus = status;
        decision.TargetAmount = null;
        decision.IsActionable = false;
        if (string.IsNullOrWhiteSpace(decision.PrerequisiteMessage))
        {
            decision.PrerequisiteMessage = message;
        }
    }

    private static void ApplyHoldingObservation(StrategyDecisionStateRecord decision)
    {
        decision.ActionInstruction = "√ 持股待涨";
        decision.StrategyStatus = "正常趋势";
        decision.TargetAmount = null;
        decision.IsActionable = false;
    }

    private static void ApplyBuyDecision(
        StrategyDecisionStateRecord decision,
        string action,
        string status,
        double targetAmount,
        double availableCash)
    {
        decision.ActionInstruction = action;
        decision.StrategyStatus = status;
        decision.TargetAmount = Math.Max(0, targetAmount);
        decision.AvailableCash = availableCash;
        decision.IsActionable = targetAmount > 0.01;
    }

    private static void ApplySellDecision(StrategyDecisionStateRecord decision, string action, string status, double targetAmount)
    {
        decision.ActionInstruction = action;
        decision.StrategyStatus = status;
        decision.TargetAmount = Math.Max(0, targetAmount);
        decision.IsActionable = targetAmount > 0.01;
    }

    private static string BuildPrerequisiteMessage(string? indexSymbol, bool globalReady, bool indexReady, string globalMessage)
    {
        if (!globalReady)
        {
            return globalMessage;
        }

        if (string.IsNullOrWhiteSpace(indexSymbol))
        {
            return "策略未配置跟踪指数 secid。";
        }

        return indexReady
            ? "该 ETF 跟踪指数历史 K 线前置已就绪。"
            : $"{indexSymbol} 历史 K 线未成功返回。";
    }

    private static EtfPositionCostMetrics CalculateCostMetrics(StrategyDecisionCalculationInput input, StrategyConfigRecord strategy)
    {
        PositionReplayStateRecord[] replayPositions = input.PositionReplayStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code))
            .ToArray();
        OtcPositionReplayStateRecord[] otcPositions = input.OtcPositionReplayStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code))
            .ToArray();
        if (replayPositions.Length > 0 || otcPositions.Length > 0)
        {
            return EtfDecisionTableMetrics.CalculatePositionCostMetrics(replayPositions, otcPositions);
        }

        PositionStateRecord[] manualPositions = input.PositionStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code))
            .ToArray();
        double quantity = manualPositions.Sum(position => position.Quantity);
        double costAmount = manualPositions.Sum(position => position.CostAmount);
        return new EtfPositionCostMetrics(quantity, costAmount, quantity > 0 ? costAmount / quantity : 0);
    }

    private static EtfPositionCostMetrics CalculateExchangeCostMetrics(StrategyDecisionCalculationInput input, StrategyConfigRecord strategy)
    {
        PositionReplayStateRecord[] replayPositions = input.PositionReplayStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code)
                               && !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            .ToArray();
        if (replayPositions.Length > 0)
        {
            double quantity = replayPositions.Sum(position => Math.Max(0, position.Quantity));
            double costAmount = replayPositions.Sum(position => Math.Max(0, position.CostAmount));
            return new EtfPositionCostMetrics(quantity, costAmount, quantity > 0 ? costAmount / quantity : 0);
        }

        if (input.PositionReplayStates.Count > 0)
        {
            return new EtfPositionCostMetrics(0, 0, 0);
        }

        PositionStateRecord[] manualPositions = input.PositionStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code)
                               && !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            .ToArray();
        double manualQuantity = manualPositions.Sum(position => Math.Max(0, position.Quantity));
        double manualCostAmount = manualPositions.Sum(position => Math.Max(0, position.CostAmount));
        return new EtfPositionCostMetrics(manualQuantity, manualCostAmount, manualQuantity > 0 ? manualCostAmount / manualQuantity : 0);
    }

    private static bool HasHolding(EtfPositionCostMetrics metrics)
        => metrics.TotalCostAmount > MoneyEpsilon || metrics.TotalQuantity > 0.00000001;

    private static bool ShouldDeferOtcPremiumTakeProfit(
        StrategyDecisionCalculationInput input,
        StrategyConfigRecord strategy,
        string preferredSource)
    {
        if (!string.Equals(preferredSource, "场外替代", StringComparison.Ordinal))
        {
            return false;
        }

        return HasOtcSubstituteHolding(input, strategy) && !HasExchangeHolding(input, strategy);
    }

    private static bool HasExchangeHolding(StrategyDecisionCalculationInput input, StrategyConfigRecord strategy)
    {
        if (input.PositionReplayStates.Any(position =>
                SameCode(position.StrategyCode, strategy.Code)
                && !string.Equals(position.Source, "场外替代", StringComparison.Ordinal)
                && (position.Quantity > 0.00000001 || position.CostAmount > MoneyEpsilon)))
        {
            return true;
        }

        return input.PositionReplayStates.Count == 0
               && input.PositionStates.Any(position =>
                   SameCode(position.StrategyCode, strategy.Code)
                   && !string.Equals(position.Source, "场外替代", StringComparison.Ordinal)
                   && (position.Quantity > 0.00000001 || position.CostAmount > MoneyEpsilon));
    }

    private static bool HasOtcSubstituteHolding(StrategyDecisionCalculationInput input, StrategyConfigRecord strategy)
    {
        if (input.OtcPositionReplayStates.Any(position =>
                SameCode(position.StrategyCode, strategy.Code)
                && (position.Quantity > 0.00000001 || position.CostAmount > MoneyEpsilon)))
        {
            return true;
        }

        if (input.PositionReplayStates.Any(position =>
                SameCode(position.StrategyCode, strategy.Code)
                && string.Equals(position.Source, "场外替代", StringComparison.Ordinal)
                && (position.Quantity > 0.00000001 || position.CostAmount > MoneyEpsilon)))
        {
            return true;
        }

        return input.PositionReplayStates.Count == 0
               && input.PositionStates.Any(position =>
                   SameCode(position.StrategyCode, strategy.Code)
                   && string.Equals(position.Source, "场外替代", StringComparison.Ordinal)
                   && (position.Quantity > 0.00000001 || position.CostAmount > MoneyEpsilon));
    }

    private static double? CalculateReturnRate(StrategyDecisionCalculationInput input, StrategyConfigRecord strategy, double currentCost)
    {
        if (currentCost <= 0)
        {
            return null;
        }

        PositionReplayStateRecord[] replayPositions = input.PositionReplayStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code))
            .ToArray();
        OtcPositionReplayStateRecord[] otcPositions = input.OtcPositionReplayStates
            .Where(position => SameCode(position.StrategyCode, strategy.Code))
            .ToArray();
        double? marketValue = null;
        if (replayPositions.Length > 0 && replayPositions.All(position => position.MarketValue.HasValue))
        {
            marketValue = replayPositions.Sum(position => position.MarketValue!.Value);
        }
        else if (replayPositions.Length == 0 && otcPositions.Length > 0 && otcPositions.All(position => position.MarketValue.HasValue))
        {
            marketValue = otcPositions.Sum(position => position.MarketValue!.Value);
        }

        if (marketValue.HasValue)
        {
            return (marketValue.Value - currentCost) / currentCost;
        }

        double weightedReturn = 0;
        double weightedCost = 0;
        if (replayPositions.Length > 0)
        {
            foreach (PositionReplayStateRecord position in replayPositions)
            {
                if (position.ReturnRate.HasValue && position.CostAmount > 0)
                {
                    weightedReturn += position.ReturnRate.Value * position.CostAmount;
                    weightedCost += position.CostAmount;
                }
            }
        }
        else
        {
            foreach (OtcPositionReplayStateRecord position in otcPositions)
            {
                if (position.ReturnRate.HasValue && position.CostAmount > 0)
                {
                    weightedReturn += position.ReturnRate.Value * position.CostAmount;
                    weightedCost += position.CostAmount;
                }
            }
        }

        return weightedCost > 0 ? weightedReturn / weightedCost : null;
    }

    private static double GetPrincipal(StrategyDecisionCalculationInput input)
    {
        double? replayPrincipal = input.AccountReplayState?.Principal;
        if (HasPositiveValue(replayPrincipal))
        {
            return replayPrincipal!.Value;
        }

        return Math.Max(0, input.AccountState?.Principal ?? 0);
    }

    private static double CalculateTotalPositionCost(StrategyDecisionCalculationInput input)
    {
        double? replayTotalPositionCost = input.AccountReplayState?.TotalPositionCost;
        if (HasPositiveValue(replayTotalPositionCost))
        {
            return replayTotalPositionCost!.Value;
        }

        double replayPositionCost = input.PositionReplayStates.Sum(position => Math.Max(0, position.CostAmount))
                                    + input.OtcPositionReplayStates.Sum(position => Math.Max(0, position.CostAmount));
        if (replayPositionCost > 0)
        {
            return replayPositionCost;
        }

        return Math.Max(0, input.PositionStates.Sum(position => position.CostAmount));
    }

    private static double CalculateRealSniperPool(double cashBalance, double baseGap)
    {
        if (cashBalance <= 0)
        {
            return 0;
        }

        return Math.Max(0, cashBalance - Math.Max(0, baseGap));
    }

    private static SniperPoolContext CalculateSniperPoolContext(
        IReadOnlyList<TradeLogRecord> tradeLogs,
        double principal,
        double baseTarget,
        double realSniperPool)
    {
        TradeLogRecord[] ordered = tradeLogs
            .OrderBy(record => ParseTime(record.Time))
            .ThenBy(record => record.Id)
            .ToArray();
        int lastCycleEndIndex = -1;
        for (int i = 0; i < ordered.Length; i++)
        {
            if (IsCycleEnd(ordered[i]))
            {
                lastCycleEndIndex = i;
            }
        }

        IReadOnlyList<TradeLogRecord> currentCycleLogs = lastCycleEndIndex >= 0
            ? ordered.Skip(lastCycleEndIndex + 1).ToArray()
            : ordered;

        double tierBudgetBase;
        if (lastCycleEndIndex >= 0 && ordered[lastCycleEndIndex].CashBalance > 0)
        {
            tierBudgetBase = Math.Max(0, ordered[lastCycleEndIndex].CashBalance);
        }
        else
        {
            double strategicBaseBuys = currentCycleLogs
                .Where(record => IsBuy(record) && string.Equals(record.Tier?.Trim(), "战略底仓", StringComparison.Ordinal))
                .Sum(record => record.Amount);
            double baseUsed = strategicBaseBuys > 0.01 ? strategicBaseBuys : baseTarget;
            baseUsed = Math.Clamp(baseUsed, 0, Math.Max(0, principal));
            tierBudgetBase = Math.Max(0, principal - baseUsed);
        }

        return new SniperPoolContext(
            Math.Max(0, realSniperPool),
            Math.Max(0, tierBudgetBase),
            currentCycleLogs);
    }

    private static double CalculateExecutedTierAmount(IEnumerable<TradeLogRecord> currentCycleTradeLogs)
    {
        return currentCycleTradeLogs
            .Where(record => IsBuy(record) && TryParseTierLevel(record.Tier, out _))
            .Sum(record => record.Amount);
    }

    private static bool IsCycleEnd(TradeLogRecord record)
        => string.Equals(record.Tier?.Trim(), "周期结束", StringComparison.Ordinal);

    private static bool IsBuy(TradeLogRecord record)
        => string.Equals(record.Action?.Trim(), "买入", StringComparison.Ordinal);

    private static double[] ResolveTierWeights(StrategyConfigRecord strategy)
    {
        double[] configured =
        {
            strategy.T1Weight ?? 0,
            strategy.T2Weight ?? 0,
            strategy.T3Weight ?? 0,
            strategy.T4Weight ?? 0,
            strategy.T5Weight ?? 0,
            strategy.T6Weight ?? 0
        };

        for (int i = 0; i < configured.Length; i++)
        {
            if (configured[i] <= 0)
            {
                configured[i] = DefaultTierWeights[i];
            }
        }

        double total = configured.Sum();
        return total > 0 ? configured : DefaultTierWeights.ToArray();
    }

    private static int GetTriggeredTierLevel(double indexDrawdown)
    {
        const double tolerance = 0.000000001;
        if (indexDrawdown <= -0.30 + tolerance) return 6;
        if (indexDrawdown <= -0.25 + tolerance) return 5;
        if (indexDrawdown <= -0.20 + tolerance) return 4;
        if (indexDrawdown <= -0.15 + tolerance) return 3;
        if (indexDrawdown <= -0.10 + tolerance) return 2;
        return indexDrawdown <= -0.05 + tolerance ? 1 : 0;
    }

    private static string TierLevelText(int tierLevel)
        => tierLevel is >= 1 and <= 6 ? TierNames[tierLevel - 1].Replace("狙击", string.Empty, StringComparison.Ordinal) : string.Empty;

    private static bool TryParseTierLevel(string? tier, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(tier))
        {
            return false;
        }

        for (int i = 0; i < TierNames.Length; i++)
        {
            if (tier.Contains(TierNames[i], StringComparison.Ordinal))
            {
                level = i + 1;
                return true;
            }
        }

        return false;
    }

    private static bool ShouldPreferOtc(IReadOnlyList<OtcChannelRecord> enabledOtcChannels, double? premium, double? addPremiumLimit)
        => enabledOtcChannels.Count > 0
           && premium.HasValue
           && addPremiumLimit.HasValue
           && premium.Value > addPremiumLimit.Value;

    private static double? FindFirstOtcPrice(StrategyDecisionCalculationInput input, IReadOnlyList<OtcChannelRecord> channels)
    {
        foreach (OtcChannelRecord channel in channels.OrderBy(channel => channel.Priority).ThenBy(channel => channel.OtcCode, StringComparer.OrdinalIgnoreCase))
        {
            MarketQuoteRecord? quote = FindQuote(input.MarketQuotes, DigitsOnly(channel.OtcCode), "OTC");
            if (quote?.Price is double price && price > 0)
            {
                return price;
            }
        }

        return null;
    }

    private static bool HasUsableHistory(StrategyDecisionCalculationInput input, string? symbol, string marketType)
        => FindQuote(input.MarketHistory, NormalizeSecId(symbol), marketType)?.HighValue is double high && high > 0;

    private static MarketQuoteRecord? FindQuote(IEnumerable<MarketQuoteRecord> quotes, string? symbol, string? marketType)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        return quotes
            .Where(quote => string.Equals(quote.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                            && (marketType is null || string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static double? CalculateDrawdown(double? currentValue, double? highValue)
        => HasPositiveValue(currentValue) && HasPositiveValue(highValue)
            ? currentValue!.Value / highValue!.Value - 1.0
            : null;

    private static double? FirstPositive(params double?[] values)
        => values.FirstOrDefault(HasPositiveValue);

    private static bool IsThresholdHit(double? value, double? threshold)
        => value.HasValue && threshold.HasValue && value.Value >= threshold.Value;

    private static double CapBuyTarget(double targetAmount, double availableCash)
    {
        if (targetAmount <= 0 || availableCash <= 0)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(targetAmount, availableCash));
    }

    private static double CalculateSellTarget(double currentCost, double accountSellableExcessCost)
    {
        if (currentCost <= 0 || accountSellableExcessCost <= 0)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(currentCost, accountSellableExcessCost));
    }

    private static bool SameCode(string? left, string? right)
        => string.Equals(DigitsOnly(left), DigitsOnly(right), StringComparison.OrdinalIgnoreCase)
           || string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string DigitsOnly(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());

    private static string? NormalizeSecId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Contains('.', StringComparison.Ordinal) ? trimmed : DigitsOnly(trimmed);
    }

    private static DateTime ParseTime(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed
            : DateTime.MinValue;

    private static double? SumNullable(IEnumerable<double?> values)
    {
        double total = 0;
        bool hasValue = false;
        foreach (double? value in values)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                continue;
            }

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    private static bool HasPositiveValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value > 0;

    private static StrategyDecisionRuntimeWarning Warning(string message, string detail)
        => new("WARN", "StrategyDecision", message, detail);

    private sealed record SniperPoolContext(
        double RealSniperPool,
        double TierBudgetBase,
        IReadOnlyList<TradeLogRecord> CurrentCycleTradeLogs);
}
