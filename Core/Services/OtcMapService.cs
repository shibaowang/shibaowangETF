using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 OTCMap 多通道服务（纯内存，不联网）。
/// 场外买入按优先级、多通道、每日限额、最小申购拆单。
/// 场外卖出 C 类优先，只卖真实场外持仓。
/// </summary>
public class OtcMapService
{
    private readonly List<OtcChannel> _channels;
    private readonly Dictionary<string, double> _otcHoldings;    // key: strategyCode|otcCode -> quantity
    private readonly Dictionary<string, double> _otcHoldingCosts; // key: strategyCode|otcCode -> cost

    public OtcMapService(IEnumerable<OtcChannel> channels)
    {
        _channels = channels?.Where(c => c.Enabled).OrderBy(c => c.Priority).ToList()
                    ?? new List<OtcChannel>();
        _otcHoldings = new Dictionary<string, double>();
        _otcHoldingCosts = new Dictionary<string, double>();
    }

    public IReadOnlyList<OtcChannel> EnabledChannels => _channels;

    /// <summary>
    /// 注册场外持仓（来自 Replay）。
    /// </summary>
    public void RegisterHolding(string strategyCode, string otcCode, double qty, double cost)
    {
        string key = $"{strategyCode}|{otcCode}";
        _otcHoldings[key] = qty;
        _otcHoldingCosts[key] = cost;
    }

    /// <summary>
    /// 获取策略的所有已启用通道。
    /// </summary>
    public List<OtcChannel> GetChannelsForStrategy(string strategyCode)
    {
        return _channels
            .Where(c => string.Equals(c.StrategyCode, strategyCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Priority)
            .ToList();
    }

    /// <summary>
    /// V8.2 场外买入多通道拆单。
    /// 按优先级遍历，遵守每日限额（已买金额 + 本次 <= 限额），遵守最小申购金额。
    /// </summary>
    public List<OtcTradeLeg> BuildBuyLegs(
        string strategyCode,
        double needAmt,
        double curCash,
        string tier,
        string memo,
        Dictionary<string, double> todayBoughtAmt,
        Dictionary<string, double> navPrices)
    {
        var legs = new List<OtcTradeLeg>();
        var channels = GetChannelsForStrategy(strategyCode);

        double remainNeed = needAmt;
        double cashLeft = curCash;

        foreach (var ch in channels)
        {
            if (remainNeed <= 0.01 || cashLeft <= 0.01) break;

            double nav = navPrices.GetValueOrDefault(ch.OtcCode, 0);
            if (nav <= 0) continue;

            double todayBought = todayBoughtAmt.GetValueOrDefault($"{strategyCode}|{ch.OtcCode}", 0);
            double remainLimit = ch.DailyLimit > 0 ? Math.Max(0, ch.DailyLimit - todayBought) : remainNeed;
            if (remainLimit <= 0) continue;

            double useAmt = Math.Min(Math.Min(remainNeed, remainLimit), cashLeft);
            useAmt = Math.Floor(useAmt * 100) / 100.0;

            if (useAmt + 0.0001 < ch.MinBuyAmount) continue;

            var quantifier = new TradeQuantifier();
            var qResult = quantifier.Quantify("买入", "场外替代", useAmt, nav, 0, cashLeft);

            if (!qResult.IsExecutable) continue;

            legs.Add(new OtcTradeLeg
            {
                StrategyCode = strategyCode,
                ActualCode = ch.OtcCode,
                ClassName = ch.ClassName,
                Action = "买入",
                Quantity = qResult.Quantity,
                Amount = qResult.Amount,
                PriceOrNav = nav,
                Fee = 0,
                NetCashImpact = qResult.NetCashImpact,
                Tier = tier,
                Memo = memo
            });

            remainNeed -= qResult.Amount;
            cashLeft -= qResult.Amount;
        }

        return legs;
    }

    /// <summary>
    /// V8.2 场外卖出多通道拆单：C 类优先（Priority == 2），只卖真实场外持仓。
    /// 按实际代码分别扣减成本。
    /// </summary>
    public List<OtcTradeLeg> BuildSellLegs(
        string strategyCode,
        double targetSellValue,
        Dictionary<string, double> navPrices)
    {
        var legs = new List<OtcTradeLeg>();
        var channels = GetChannelsForStrategy(strategyCode);

        if (targetSellValue <= 0) return legs;

        // 第一轮：C 类优先（Priority == 2）
        var priorityChannels = channels.Where(c => c.IsSellPriority).ToList();
        double remainValue = targetSellValue;

        BuildSellLegsFromChannels(priorityChannels, strategyCode, ref remainValue, navPrices, legs);

        // 第二轮：其他优先级
        var otherChannels = channels.Where(c => !c.IsSellPriority).OrderBy(c => c.Priority).ToList();
        BuildSellLegsFromChannels(otherChannels, strategyCode, ref remainValue, navPrices, legs);

        return legs;
    }

    private void BuildSellLegsFromChannels(
        List<OtcChannel> channels,
        string strategyCode,
        ref double remainValue,
        Dictionary<string, double> navPrices,
        List<OtcTradeLeg> legs)
    {
        foreach (var ch in channels)
        {
            if (remainValue <= 0.0001) break;

            string posKey = $"{strategyCode}|{ch.OtcCode}";
            if (!_otcHoldings.TryGetValue(posKey, out double qty) || qty <= 0) continue;
            if (!_otcHoldingCosts.TryGetValue(posKey, out double costAmt) || costAmt <= 0) continue;

            double nav = navPrices.GetValueOrDefault(ch.OtcCode, 0);
            if (nav <= 0) continue;

            double mktValue = qty * nav;
            double costPerQty = costAmt / qty;

            double sellQty;
            if (remainValue >= mktValue - 0.0001)
                sellQty = qty;
            else
                sellQty = Math.Floor((remainValue / nav) * 10000) / 10000.0;

            if (sellQty > qty) sellQty = qty;
            if (sellQty < 0.0001) continue;

            double sellAmt = sellQty * nav;
            double costPart = sellQty * costPerQty;
            double fee = sellAmt * 0.00013;

            legs.Add(new OtcTradeLeg
            {
                StrategyCode = strategyCode,
                ActualCode = ch.OtcCode,
                ClassName = ch.ClassName,
                Action = "卖出",
                Quantity = sellQty,
                Amount = sellAmt,
                PriceOrNav = nav,
                Fee = fee,
                NetCashImpact = sellAmt - fee,
                CostPart = costPart
            });

            remainValue -= sellAmt;
        }
    }

    /// <summary>
    /// 获取 OTC 多腿卖出合计成本扣减。
    /// </summary>
    public static double GetTotalCostPart(List<OtcTradeLeg> legs)
    {
        return legs?.Sum(l => l.CostPart) ?? 0;
    }

    /// <summary>
    /// 获取 OTC 多腿卖出合计金额。
    /// </summary>
    public static double GetTotalSellAmount(List<OtcTradeLeg> legs)
    {
        return legs?.Sum(l => l.Amount) ?? 0;
    }
}
