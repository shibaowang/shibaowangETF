using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

// LOCKED: Order drafts are suggestions only; never treat them as trades or write TradeLog automatically.
public sealed class OrderDraftService
{
    private const string Buy = "买入";
    private const string Sell = "卖出";
    private const string None = "NONE";
    private const string ExchangeSource = "场内ETF";
    private const string OtcSource = "场外替代";
    private const string DraftStatus = "草案";
    private const string PartialStatus = "部分可委托";
    private const string NotExecutableStatus = "不可执行";
    private const string AClass = "A类";
    private const string CClass = "C类";
    private const double MoneyTolerance = 0.000001;
    private const double BaseProtectionTolerance = 0.01;
    private const double EstimatedSellFeeRate = 0.00013;

    public OrderDraftCalculationResult Calculate(OrderDraftCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string calculatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string snapshotKey = BuildSnapshotKey(input);
        var drafts = new List<OrderDraftStateRecord>();
        var legs = new List<OrderDraftLegStateRecord>();
        var warnings = new List<OrderDraftRuntimeWarning>();

        foreach (StrategyDecisionStateRecord decision in input.StrategyDecisions.OrderBy(item => item.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id))
        {
            OrderDraftStateRecord draft = CreateBaseDraft(decision, calculatedAt, snapshotKey);
            drafts.Add(draft);

            if (!decision.IsActionable || !(decision.TargetAmount is double targetAmount) || targetAmount <= MoneyTolerance)
            {
                MarkNotExecutable(draft, decision.StrategyStatus ?? "无可执行策略建议");
                continue;
            }

            string side = ResolveSide(decision);
            draft.Side = side;
            if (side == None)
            {
                MarkNotExecutable(draft, "策略指令不是买入或卖出，不生成委托草案");
                continue;
            }

            draft.Source = ResolveSource(decision);
            if (side == Sell)
            {
                CalculateSellDraft(input, decision, draft, legs);
            }
            else if (draft.Source == OtcSource)
            {
                CalculateOtcDraft(input, decision, draft, legs);
            }
            else
            {
                CalculateExchangeDraft(input, decision, draft, legs);
            }

            if (!draft.IsExecutable)
            {
                warnings.Add(new OrderDraftRuntimeWarning("WARN", "OrderDraft", $"委托草案不可执行：{draft.StrategyCode}", draft.Reason ?? "未量化出可执行数量"));
            }
        }

        return new OrderDraftCalculationResult(drafts, legs, warnings);
    }

    private static void CalculateSellDraft(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        List<OrderDraftLegStateRecord> legs)
    {
        double targetAmount = Math.Max(0, decision.TargetAmount ?? 0);
        bool exchangeOnlyPremiumSell = IsExchangePremiumSellDecision(decision);
        ExchangeSellCandidate exchange = FindSafeExchangeSellCandidate(input, decision, targetAmount);
        if (exchange.Quantity >= 100 && exchange.Amount > MoneyTolerance)
        {
            double remainingAmount = RoundDownToCents(targetAmount - exchange.Amount);
            if (remainingAmount <= MoneyTolerance)
            {
                SetDraftSource(decision, draft, ExchangeSource);
                draft.Price = exchange.Price;
                ApplyExecutableDraft(draft, exchange.Quantity, exchange.Amount, DraftStatus, exchange.Reason);
                legs.Add(CreateLeg(draft, decision.StrategyCode, exchange.Price, null, exchange.Quantity, exchange.Amount, DraftStatus, draft.Reason, sourceOverride: ExchangeSource));
                return;
            }

            if (exchangeOnlyPremiumSell)
            {
                SetDraftSource(decision, draft, ExchangeSource);
                draft.Price = exchange.Price;
                legs.Add(CreateLeg(draft, decision.StrategyCode, exchange.Price, null, exchange.Quantity, exchange.Amount, DraftStatus, exchange.Reason, sourceOverride: ExchangeSource));
                ApplyExecutableDraft(draft, exchange.Quantity, exchange.Amount, PartialStatus, exchange.Reason ?? "场内溢价卖出仅限场内 ETF 持仓");
                return;
            }

            SetDraftSource(decision, draft, ExchangeSource + "+场外替代");
            draft.Price = exchange.Price;
            var mixedLegs = new List<OrderDraftLegStateRecord>
            {
                CreateLeg(draft, decision.StrategyCode, exchange.Price, null, exchange.Quantity, exchange.Amount, DraftStatus, exchange.Reason, sourceOverride: ExchangeSource)
            };
            OtcSellCandidate otc = FindSafeOtcSellCandidate(input, decision, draft, remainingAmount, exchange.Amount, exchange.CostPart);
            if (string.IsNullOrWhiteSpace(otc.Error) && otc.TotalAmount > MoneyTolerance && otc.TotalQuantity > MoneyTolerance)
            {
                mixedLegs.AddRange(otc.Legs);
                legs.AddRange(mixedLegs);
                double totalAmount = RoundToCents(exchange.Amount + otc.TotalAmount);
                string status = totalAmount + MoneyTolerance < targetAmount ? PartialStatus : DraftStatus;
                string? reason = status == PartialStatus ? "场内优先后，受底仓保护、场外持仓或通道约束" : null;
                ApplyExecutableDraft(draft, exchange.Quantity, totalAmount, status, reason);
                return;
            }

            SetDraftSource(decision, draft, ExchangeSource);
            draft.Price = exchange.Price;
            legs.Add(CreateLeg(draft, decision.StrategyCode, exchange.Price, null, exchange.Quantity, exchange.Amount, DraftStatus, exchange.Reason, sourceOverride: ExchangeSource));
            ApplyExecutableDraft(draft, exchange.Quantity, exchange.Amount, PartialStatus, exchange.Reason ?? otc.Error ?? "场内优先后，场外卖出不可执行");
            return;
        }

        if (exchangeOnlyPremiumSell)
        {
            SetDraftSource(decision, draft, ExchangeSource);
            MarkNotExecutable(draft, exchange.Error ?? "没有可卖出的场内 ETF 持仓");
            return;
        }

        SetDraftSource(decision, draft, OtcSource);
        CalculateOtcSellDraft(input, decision, draft, legs);
        if (!draft.IsExecutable && !string.IsNullOrWhiteSpace(exchange.Error))
        {
            draft.Reason = draft.Reason is { Length: > 0 }
                ? exchange.Error + "；" + draft.Reason
                : exchange.Error;
        }
    }

    private static void SetDraftSource(StrategyDecisionStateRecord decision, OrderDraftStateRecord draft, string source)
    {
        draft.Source = source;
        draft.DraftKey = BuildDraftKey(decision.StrategyCode, decision.ActionInstruction, draft.Side, source);
    }

    private static OrderDraftStateRecord CreateBaseDraft(StrategyDecisionStateRecord decision, string calculatedAt, string snapshotKey)
    {
        string source = ResolveSource(decision);
        string side = ResolveSide(decision);
        return new OrderDraftStateRecord
        {
            DraftKey = BuildDraftKey(decision.StrategyCode, decision.ActionInstruction, side, source),
            CalculatedAt = calculatedAt,
            SnapshotKey = snapshotKey,
            StrategyCode = decision.StrategyCode,
            Name = decision.Name,
            ActionInstruction = decision.ActionInstruction,
            Side = side,
            Source = source,
            TargetTier = decision.TargetTier,
            TargetAmount = decision.TargetAmount,
            Price = null,
            DraftStatus = NotExecutableStatus,
            Reason = decision.PrerequisiteMessage,
            IsExecutable = false
        };
    }

    private static void CalculateExchangeDraft(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        List<OrderDraftLegStateRecord> legs)
    {
        double targetAmount = Math.Max(0, decision.TargetAmount ?? 0);
        double price = ResolveExchangeExecutionPrice(input, decision.StrategyCode);
        if (price <= 0)
        {
            MarkNotExecutable(draft, "缺少真实场内 ETF 价格，无法量化委托");
            return;
        }

        draft.Price = price;
        if (draft.Side == Buy)
        {
            double cash = Math.Max(0, input.AccountReplayState?.CashBalance ?? decision.AvailableCash ?? 0);
            double pool = decision.RealSniperPool is double sniperPool && sniperPool > 0 ? sniperPool : cash;
            double spendable = Math.Min(targetAmount, Math.Min(cash, pool));
            double quantity = FloorToBoardLot(spendable / price);
            if (quantity < 100)
            {
                MarkNotExecutable(draft, "现金或狙击资金池不足 100 股整手");
                return;
            }

            ApplyExecutableDraft(draft, quantity, RoundToCents(quantity * price), DraftStatus, spendable + MoneyTolerance < targetAmount ? "受现金或资金池约束" : null);
            legs.Add(CreateLeg(draft, decision.StrategyCode, price, null, quantity, draft.Amount, DraftStatus, draft.Reason));
            return;
        }

        ExchangeSellCandidate exchange = FindSafeExchangeSellCandidate(input, decision, price, targetAmount);
        if (exchange.Quantity < 100)
        {
            MarkNotExecutable(draft, exchange.Error ?? exchange.Reason ?? "底仓保护后无 100 股整手可卖");
            return;
        }

        ApplyExecutableDraft(draft, exchange.Quantity, exchange.Amount, DraftStatus, exchange.Reason);
        legs.Add(CreateLeg(draft, decision.StrategyCode, price, null, exchange.Quantity, draft.Amount, DraftStatus, draft.Reason));
    }

    private static void CalculateOtcDraft(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        List<OrderDraftLegStateRecord> legs)
    {
        if (draft.Side == Buy)
        {
            CalculateOtcBuyDraft(input, decision, draft, legs);
            return;
        }

        CalculateOtcSellDraft(input, decision, draft, legs);
    }

    private static void CalculateOtcBuyDraft(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        List<OrderDraftLegStateRecord> legs)
    {
        double targetAmount = Math.Max(0, decision.TargetAmount ?? 0);
        double cash = Math.Max(0, input.AccountReplayState?.CashBalance ?? decision.AvailableCash ?? 0);
        double pool = decision.RealSniperPool is double sniperPool && sniperPool > 0 ? sniperPool : cash;
        double remaining = RoundDownToCents(Math.Min(targetAmount, Math.Min(cash, pool)));
        if (remaining <= MoneyTolerance)
        {
            MarkNotExecutable(draft, "现金或狙击资金池不足，无法生成场外申购草案");
            return;
        }

        OtcChannelRecord[] channels = EnabledChannels(input, decision.StrategyCode)
            .OrderBy(channel => channel.Priority)
            .ThenBy(channel => ClassRank(channel.ClassType))
            .ThenBy(channel => channel.OtcCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (channels.Length == 0)
        {
            MarkNotExecutable(draft, "缺少启用的 OTCMap 通道");
            return;
        }

        double total = 0;
        foreach (OtcChannelRecord channel in channels)
        {
            if (remaining <= MoneyTolerance)
            {
                break;
            }

            double todayUsed = CalculateTodayUsed(input.TradeLogs, channel, input.Today);
            double dailyRemain = channel.DailyLimit > 0 ? Math.Max(0, channel.DailyLimit - todayUsed) : remaining;
            double amount = RoundDownToCents(Math.Min(remaining, dailyRemain));
            if (amount + MoneyTolerance < Math.Max(0, channel.MinBuy))
            {
                continue;
            }

            MarketQuoteRecord? quote = FindOtcQuote(input, channel.OtcCode);
            legs.Add(CreateLeg(draft, channel.OtcCode, null, quote?.Price, 0, amount, DraftStatus, null, channel));
            total += amount;
            remaining = RoundDownToCents(remaining - amount);
        }

        if (total <= MoneyTolerance)
        {
            MarkNotExecutable(draft, "OTCMap 通道限额或最低申购额不足");
            return;
        }

        string status = total + MoneyTolerance < targetAmount ? PartialStatus : DraftStatus;
        ApplyExecutableDraft(draft, 0, total, status, status == PartialStatus ? "受现金、资金池或通道限额约束" : null);
    }

    private static void CalculateOtcSellDraft(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        List<OrderDraftLegStateRecord> legs)
    {
        double targetAmount = Math.Max(0, decision.TargetAmount ?? 0);
        OtcSellCandidate candidate = FindSafeOtcSellCandidate(input, decision, draft, targetAmount);
        if (!string.IsNullOrWhiteSpace(candidate.Error))
        {
            MarkNotExecutable(draft, candidate.Error);
            return;
        }

        if (candidate.TotalAmount <= MoneyTolerance || candidate.TotalQuantity <= MoneyTolerance)
        {
            MarkNotExecutable(draft, "底仓保护后无可卖场外金额");
            return;
        }

        foreach (OrderDraftLegStateRecord leg in candidate.Legs)
        {
            legs.Add(leg);
        }

        string status = candidate.TotalAmount + MoneyTolerance < targetAmount ? PartialStatus : DraftStatus;
        ApplyExecutableDraft(draft, candidate.TotalQuantity, candidate.TotalAmount, status, status == PartialStatus ? "受底仓保护或持仓数量约束" : null);
    }

    private static ExchangeSellCandidate FindSafeExchangeSellCandidate(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        double targetAmount)
    {
        double price = ResolveExchangeExecutionPrice(input, decision.StrategyCode);
        return price <= 0
            ? ExchangeSellCandidate.Empty("缺少真实场内 ETF 价格，无法量化委托")
            : FindSafeExchangeSellCandidate(input, decision, price, targetAmount);
    }

    private static ExchangeSellCandidate FindSafeExchangeSellCandidate(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        double price,
        double targetAmount)
    {
        PositionReplayStateRecord[] positions = input.PositionReplayStates
            .Where(position => SameCode(position.StrategyCode, decision.StrategyCode)
                               && !TextEquals(position.Source, OtcSource)
                               && position.Quantity > 0)
            .ToArray();
        double positionQuantity = positions.Sum(position => position.Quantity);
        if (positionQuantity <= 0)
        {
            return ExchangeSellCandidate.Empty("没有可卖出的场内 ETF 持仓");
        }

        double averageCost = positions.Sum(position => position.CostAmount) / positionQuantity;
        if (averageCost <= 0)
        {
            return ExchangeSellCandidate.Empty("场内 ETF 平均成本无效，无法计算底仓保护卖出量");
        }

        double targetQuantity = targetAmount / price;
        double raw = Math.Min(positionQuantity, targetQuantity);
        int highLots = (int)Math.Floor(FloorToBoardLot(raw) / 100);
        int lowLots = 0;
        int bestLots = 0;
        while (lowLots <= highLots)
        {
            int midLots = (lowLots + highLots) / 2;
            double midQuantity = midLots * 100.0;
            double sellCostPart = averageCost * midQuantity;
            double sellAmount = RoundToCents(midQuantity * price);
            if (midLots > 0 && IsSellBaseProtected(input, decision, sellAmount, sellCostPart))
            {
                bestLots = midLots;
                lowLots = midLots + 1;
            }
            else
            {
                highLots = midLots - 1;
            }
        }

        double quantity = bestLots * 100.0;
        if (quantity <= 0)
        {
            return ExchangeSellCandidate.Empty("底仓保护后不足 100 股整手");
        }

        string? reason = null;
        if (quantity + 0.0001 < targetQuantity)
        {
            reason = "受持仓数量或底仓保护约束";
        }

        double amount = RoundToCents(quantity * price);
        double costPart = averageCost * quantity;
        return new ExchangeSellCandidate(quantity, amount, costPart, price, reason, null);
    }

    private static OtcSellCandidate FindSafeOtcSellCandidate(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        double targetAmount,
        double priorSellAmount = 0,
        double priorSellCostPart = 0)
    {
        OtcSellCandidate direct = BuildOtcSellCandidate(input, decision, draft, targetAmount);
        if (!string.IsNullOrWhiteSpace(direct.Error))
        {
            return direct;
        }

        if (direct.TotalAmount > MoneyTolerance
            && IsSellBaseProtected(input, decision, priorSellAmount + direct.TotalAmount, priorSellCostPart + direct.TotalCostPart))
        {
            return direct;
        }

        double lowAmount = 0;
        double highAmount = targetAmount;
        OtcSellCandidate best = OtcSellCandidate.Empty();
        for (int i = 0; i < 32; i++)
        {
            double midAmount = (lowAmount + highAmount) / 2.0;
            OtcSellCandidate candidate = BuildOtcSellCandidate(input, decision, draft, midAmount);
            if (!string.IsNullOrWhiteSpace(candidate.Error))
            {
                return candidate;
            }

            if (candidate.TotalAmount > MoneyTolerance
                && IsSellBaseProtected(input, decision, priorSellAmount + candidate.TotalAmount, priorSellCostPart + candidate.TotalCostPart))
            {
                best = candidate;
                lowAmount = midAmount;
            }
            else
            {
                highAmount = midAmount;
            }
        }

        return best.TotalAmount > MoneyTolerance
            ? best
            : OtcSellCandidate.Empty("底仓保护后无可卖场外金额");
    }

    private static OtcSellCandidate BuildOtcSellCandidate(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        OrderDraftStateRecord draft,
        double targetAmount)
    {
        double remainingAmount = RoundDownToCents(targetAmount);
        if (remainingAmount <= MoneyTolerance)
        {
            return OtcSellCandidate.Empty();
        }

        var channelsByCode = EnabledChannels(input, decision.StrategyCode)
            .GroupBy(channel => DigitsOnly(channel.OtcCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Priority).First(), StringComparer.OrdinalIgnoreCase);
        var holdings = input.OtcPositionReplayStates
            .Where(position => SameCode(position.StrategyCode, decision.StrategyCode) && position.Quantity > 0)
            .Select(position => new
            {
                Position = position,
                Channel = channelsByCode.GetValueOrDefault(DigitsOnly(position.ActualCode))
            })
            .OrderBy(item => ClassRank(item.Channel?.ClassType))
            .ThenBy(item => item.Channel?.Priority ?? 999)
            .ThenBy(item => item.Position.ActualCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (holdings.Length == 0)
        {
            return OtcSellCandidate.Empty("没有可卖出的场外替代持仓");
        }

        var candidateLegs = new List<OrderDraftLegStateRecord>();
        double totalAmount = 0;
        double totalQuantity = 0;
        double totalCostPart = 0;
        foreach (var item in holdings)
        {
            if (remainingAmount <= MoneyTolerance)
            {
                break;
            }

            double? nav = FirstPositive(item.Position.Nav, FindOtcQuote(input, item.Position.ActualCode)?.Price);
            if (!nav.HasValue)
            {
                return OtcSellCandidate.Empty($"场外基金 {item.Position.ActualCode} 缺少真实净值，无法完整量化卖出");
            }

            double quantity = Math.Min(item.Position.Quantity, FloorQuantity(remainingAmount / nav.Value));
            if (quantity <= 0)
            {
                continue;
            }

            double amount = RoundDownToCents(quantity * nav.Value);
            double costPart = CalculateOtcCostPart(item.Position, quantity);
            candidateLegs.Add(CreateLeg(draft, item.Position.ActualCode, null, nav.Value, quantity, amount, DraftStatus, null, item.Channel, OtcSource));
            totalAmount += amount;
            totalQuantity += quantity;
            totalCostPart += costPart;
            remainingAmount = RoundDownToCents(remainingAmount - amount);
        }

        return totalAmount <= MoneyTolerance
            ? OtcSellCandidate.Empty("场外替代持仓不足或净值无效")
            : new OtcSellCandidate(candidateLegs, totalQuantity, totalAmount, totalCostPart, null);
    }

    private static double CalculateOtcCostPart(OtcPositionReplayStateRecord position, double quantity)
    {
        double averageCost = position.AverageCost > 0
            ? position.AverageCost
            : position.Quantity > 0 ? position.CostAmount / position.Quantity : 0;
        return Math.Max(0, averageCost * quantity);
    }

    private static bool IsSellBaseProtected(
        OrderDraftCalculationInput input,
        StrategyDecisionStateRecord decision,
        double sellAmount,
        double sellCostPart)
    {
        if (sellAmount <= MoneyTolerance || sellCostPart <= MoneyTolerance)
        {
            return false;
        }

        double totalCost = GetAccountTotalPositionCost(input, decision);
        if (totalCost <= MoneyTolerance)
        {
            return false;
        }

        double principal = Math.Max(0, input.AccountReplayState?.Principal ?? 0);
        double fee = EstimateSellFee(sellAmount);
        double realizedPnl = sellAmount - fee - sellCostPart;
        double postPrincipal = principal > 0 ? Math.Max(0, principal + realizedPnl) : principal;
        double postTarget = principal > 0
            ? BasePositionSettingsService.ResolveBaseTarget(postPrincipal, ResolveBaseSettings(decision)).TargetAmount
            : Math.Max(0, decision.BaseTargetAmount ?? 0);
        double postCost = Math.Max(0, totalCost - sellCostPart);
        return postCost + BaseProtectionTolerance >= postTarget;
    }

    private static BasePositionSettings ResolveBaseSettings(StrategyDecisionStateRecord decision)
    {
        string mode = string.Equals(decision.BaseMode, BasePositionSettings.AmountMode, StringComparison.OrdinalIgnoreCase)
            ? BasePositionSettings.AmountMode
            : BasePositionSettings.RatioMode;
        return mode == BasePositionSettings.AmountMode
            ? BasePositionSettingsService.CreateAmount(decision.BaseFixedAmount ?? 0, decision.BaseRatio ?? BasePositionSettings.DefaultRatio)
            : BasePositionSettingsService.CreateRatio(decision.BaseRatio ?? BasePositionSettings.DefaultRatio);
    }

    private static double GetAccountTotalPositionCost(OrderDraftCalculationInput input, StrategyDecisionStateRecord decision)
    {
        if (decision.BaseCurrentCost is double decisionCost && decisionCost > 0)
        {
            return decisionCost;
        }

        if (input.AccountReplayState?.TotalPositionCost is double replayCost && replayCost > 0)
        {
            return replayCost;
        }

        if (input.PositionReplayStates.Count > 0)
        {
            return input.PositionReplayStates.Sum(position => Math.Max(0, position.CostAmount));
        }

        return input.OtcPositionReplayStates.Sum(position => Math.Max(0, position.CostAmount));
    }

    private static OrderDraftLegStateRecord CreateLeg(
        OrderDraftStateRecord draft,
        string? actualCode,
        double? price,
        double? nav,
        double quantity,
        double amount,
        string status,
        string? reason,
        OtcChannelRecord? channel = null,
        string? sourceOverride = null)
    {
        return new OrderDraftLegStateRecord
        {
            DraftKey = draft.DraftKey,
            CalculatedAt = draft.CalculatedAt,
            SnapshotKey = draft.SnapshotKey,
            StrategyCode = draft.StrategyCode,
            ActualCode = actualCode,
            Side = draft.Side,
            Source = sourceOverride ?? draft.Source,
            ChannelClass = channel?.ClassType,
            Priority = channel?.Priority,
            Price = price,
            Nav = nav,
            Quantity = quantity,
            Amount = amount,
            LegStatus = status,
            Reason = reason
        };
    }

    private static void ApplyExecutableDraft(OrderDraftStateRecord draft, double quantity, double amount, string status, string? reason)
    {
        draft.Quantity = quantity;
        draft.Amount = amount;
        draft.DraftStatus = status;
        draft.Reason = reason;
        draft.IsExecutable = amount > MoneyTolerance;
    }

    private static void MarkNotExecutable(OrderDraftStateRecord draft, string reason)
    {
        draft.Quantity = 0;
        draft.Amount = 0;
        draft.DraftStatus = NotExecutableStatus;
        draft.Reason = reason;
        draft.IsExecutable = false;
    }

    private static IReadOnlyList<OtcChannelRecord> EnabledChannels(OrderDraftCalculationInput input, string strategyCode)
        => input.OtcChannels
            .Where(channel => channel.Enabled && SameCode(channel.StrategyCode, strategyCode))
            .ToArray();

    private static double CalculateTodayUsed(IEnumerable<TradeLogRecord> tradeLogs, OtcChannelRecord channel, DateTime today)
    {
        return tradeLogs
            .Where(record => IsBuy(record)
                             && SameCode(record.StrategyCode, channel.StrategyCode)
                             && SameCode(record.ActualCode, channel.OtcCode)
                             && IsSameLocalDate(record.Time, today))
            .Sum(record => record.Amount);
    }

    private static string ResolveSide(StrategyDecisionStateRecord decision)
    {
        string text = (decision.ActionInstruction ?? string.Empty) + "|" + (decision.StrategyStatus ?? string.Empty);
        if (text.Contains("止盈", StringComparison.Ordinal)
            || text.Contains("减仓", StringComparison.Ordinal)
            || text.Contains("卖", StringComparison.Ordinal)
            || text.Contains("极端溢价", StringComparison.Ordinal))
        {
            return Sell;
        }

        if (text.Contains("战略底仓", StringComparison.Ordinal)
            || text.Contains("狙击", StringComparison.Ordinal)
            || text.Contains("建仓", StringComparison.Ordinal)
            || text.Contains("买", StringComparison.Ordinal))
        {
            return Buy;
        }

        return None;
    }

    private static string ResolveSource(StrategyDecisionStateRecord decision)
        => ContainsText(decision.PreferredSource, "场外") ? OtcSource : ExchangeSource;

    private static bool IsExchangePremiumSellDecision(StrategyDecisionStateRecord decision)
    {
        string text = (decision.ActionInstruction ?? string.Empty) + "|" + (decision.StrategyStatus ?? string.Empty);
        return text.Contains("溢价止盈", StringComparison.Ordinal)
               || text.Contains("溢价达标", StringComparison.Ordinal)
               || text.Contains("极端溢价", StringComparison.Ordinal);
    }

    private static double ResolveExchangeExecutionPrice(OrderDraftCalculationInput input, string strategyCode)
        => FindEtfQuote(input, strategyCode)?.Price is double price && price > 0 ? price : 0;

    private static MarketQuoteRecord? FindEtfQuote(OrderDraftCalculationInput input, string strategyCode)
    {
        string code = DigitsOnly(strategyCode);
        return input.MarketQuotes
            .Where(quote => TextEquals(quote.MarketType, "ETF")
                            && (TextEquals(quote.Symbol, code) || TextEquals(DigitsOnly(quote.Symbol), code)))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static MarketQuoteRecord? FindOtcQuote(OrderDraftCalculationInput input, string? otcCode)
    {
        string code = DigitsOnly(otcCode);
        return input.MarketQuotes
            .Where(quote => TextEquals(quote.MarketType, "OTC")
                            && (TextEquals(quote.Symbol, code) || TextEquals(DigitsOnly(quote.Symbol), code)))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string BuildSnapshotKey(OrderDraftCalculationInput input)
    {
        var builder = new StringBuilder();
        foreach (StrategyDecisionStateRecord decision in input.StrategyDecisions.OrderBy(item => item.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id))
        {
            builder.Append(decision.StrategyCode).Append('|')
                .Append(decision.CalculatedAt).Append('|')
                .Append(decision.ActionInstruction).Append('|')
                .Append(decision.StrategyStatus).Append('|')
                .Append(decision.PreferredSource).Append('|')
                .Append(decision.TargetAmount?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(decision.SuggestedPrice?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(decision.RealSniperPool?.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        builder.Append("A:")
            .Append(input.AccountReplayState?.CalculatedAt).Append('|')
            .Append(input.AccountReplayState?.CashBalance?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(input.AccountReplayState?.TotalPositionCost?.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        foreach (OtcChannelRecord channel in input.OtcChannels.OrderBy(item => item.StrategyCode, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.OtcCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("C:")
                .Append(channel.Id).Append('|')
                .Append(channel.StrategyCode).Append('|')
                .Append(channel.OtcCode).Append('|')
                .Append(channel.ClassType).Append('|')
                .Append(channel.Enabled ? "1" : "0").Append('|')
                .Append(channel.DailyLimit.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(channel.Priority).Append('|')
                .Append(channel.MinBuy.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        foreach (TradeLogRecord log in input.TradeLogs.OrderBy(item => item.Id).ThenBy(item => item.Time, StringComparer.Ordinal))
        {
            builder.Append("T:")
                .Append(log.Id).Append('|')
                .Append(log.Time).Append('|')
                .Append(log.StrategyCode).Append('|')
                .Append(log.ActualCode).Append('|')
                .Append(log.Action).Append('|')
                .Append(log.Amount.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }

    private static string BuildDraftKey(string strategyCode, string? action, string side, string source)
        => string.Join("|", strategyCode.Trim(), action?.Trim() ?? string.Empty, side, source);

    private static bool IsBuy(TradeLogRecord record)
        => TextEquals(record.Action?.Trim(), Buy);

    private static bool IsSameLocalDate(string? value, DateTime today)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
           || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed.Date == today.Date
            : value?.Length >= 10 && today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == value[..10];

    private static double FloorToBoardLot(double quantity)
        => quantity <= 0 ? 0 : Math.Floor((quantity + 0.000000001) / 100) * 100;

    private static double FloorQuantity(double quantity)
        => quantity <= 0 ? 0 : Math.Floor((quantity + 0.000000001) * 10000) / 10000;

    private static double RoundDownToCents(double amount)
        => amount <= 0 ? 0 : Math.Floor((amount + 0.000000001) * 100) / 100;

    private static double RoundToCents(double amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static double EstimateSellFee(double sellAmount)
        => sellAmount <= 0 ? 0 : sellAmount * EstimatedSellFeeRate;

    private static int ClassRank(string? classType)
        => ContainsText(classType, CClass) ? 0 : ContainsText(classType, AClass) ? 1 : 2;

    private static double? FirstPositive(params double?[] values)
        => values.FirstOrDefault(value => value.HasValue && value.Value > 0);

    private static bool SameCode(string? left, string? right)
    {
        string leftDigits = DigitsOnly(left);
        string rightDigits = DigitsOnly(right);
        return (!string.IsNullOrWhiteSpace(leftDigits)
                && !string.IsNullOrWhiteSpace(rightDigits)
                && string.Equals(leftDigits, rightDigits, StringComparison.OrdinalIgnoreCase))
               || TextEquals(left, right);
    }

    private static string DigitsOnly(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    private static bool TextEquals(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(string? value, string text)
        => value?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

    private sealed record ExchangeSellCandidate(
        double Quantity,
        double Amount,
        double CostPart,
        double Price,
        string? Reason,
        string? Error)
    {
        public static ExchangeSellCandidate Empty(string? error = null)
            => new(0, 0, 0, 0, null, error);
    }

    private sealed record OtcSellCandidate(
        IReadOnlyList<OrderDraftLegStateRecord> Legs,
        double TotalQuantity,
        double TotalAmount,
        double TotalCostPart,
        string? Error)
    {
        public static OtcSellCandidate Empty(string? error = null)
            => new(Array.Empty<OrderDraftLegStateRecord>(), 0, 0, 0, error);
    }
}
