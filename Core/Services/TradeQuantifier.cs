using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 GetQuantifiedTrade 等价实现。
/// 场内ETF买入向上取整100股，卖出向下取整100股。
/// 场外买入金额到分，场外卖出份额到0.0001。
/// 现金不足时不生成伪可执行建议。
/// </summary>
public class TradeQuantifier
{
    private const double EstSellFeeRate = 0.00013;

    /// <summary>
    /// 主量化接口。
    /// </summary>
    public QuantifiedTradeResult Quantify(
        string action,
        string tradeSource,
        double amt,
        double price,
        double fee = 0,
        double cashLimit = 0)
    {
        if (amt <= 0 || price <= 0)
            return QuantifiedTradeResult.NotExecutable("金额或价格无效");

        if (action != "买入" && action != "卖出")
        {
            double shares = amt / price;
            return new QuantifiedTradeResult
            {
                Quantity = shares,
                Amount = amt,
                NetCashImpact = 0,
                DisplayText = ""
            };
        }

        bool isOtc = string.Equals(tradeSource, "场外替代", StringComparison.OrdinalIgnoreCase);

        if (action == "买入")
        {
            return QuantifyBuy(isOtc, amt, price, fee, cashLimit);
        }
        else
        {
            return QuantifySell(isOtc, amt, price, fee);
        }
    }

    private static QuantifiedTradeResult QuantifyBuy(bool isOtc, double amt, double price, double fee, double cashLimit)
    {
        double qAmt, qShares, netImpact;
        string qText;

        if (isOtc)
        {
            // 场外买入：金额向上到分
            qAmt = Math.Ceiling(amt * 100) / 100.0;
            if (cashLimit > 0 && qAmt + fee > cashLimit + 0.0001)
                return QuantifiedTradeResult.NotExecutable("现金不足");

            if (qAmt >= 0.01)
            {
                qShares = qAmt / price;
                netImpact = -(qAmt + fee);
                qText = $"{qAmt:F2}元";
            }
            else
            {
                return QuantifiedTradeResult.NotExecutable("金额低于最小单位");
            }
        }
        else
        {
            // 场内买入：向上取整到100股 (V8.2 RoundUpToLot100)
            double rawShares = amt / price;
            qShares = Math.Floor((rawShares + 99.999999) / 100.0) * 100;
            if (qShares < 100)
                return QuantifiedTradeResult.NotExecutable("不足100股");

            qAmt = qShares * price;
            if (cashLimit > 0 && qAmt + fee > cashLimit + 0.0001)
                return QuantifiedTradeResult.NotExecutable("现金不足");

            netImpact = -(qAmt + fee);
            qText = $"{qShares:0}股";
        }

        // 买入取整后金额必须 >= 目标金额
        if (qAmt + 0.0001 < amt)
            return QuantifiedTradeResult.NotExecutable("取整后金额小于目标金额");

        return new QuantifiedTradeResult
        {
            Quantity = qShares,
            Amount = qAmt,
            NetCashImpact = netImpact,
            DisplayText = qText
        };
    }

    private static QuantifiedTradeResult QuantifySell(bool isOtc, double amt, double price, double fee)
    {
        double qShares, qAmt, netImpact;
        string qText;

        if (isOtc)
        {
            // 场外卖出：份额向下到0.0001
            qShares = Math.Floor((amt / price) * 10000) / 10000.0;
            if (qShares >= 0.0001)
            {
                qAmt = qShares * price;
                netImpact = qAmt - fee;
                qText = $"{qShares:F4}份";
            }
            else
            {
                return QuantifiedTradeResult.NotExecutable("份额低于最小单位");
            }
        }
        else
        {
            // 场内卖出：向下取整到100股
            qShares = Math.Floor((amt / price) / 100.0) * 100;
            if (qShares < 100)
                return QuantifiedTradeResult.NotExecutable("不足100股");

            qAmt = qShares * price;
            netImpact = qAmt - fee;
            qText = $"{qShares:0}股";
        }

        return new QuantifiedTradeResult
        {
            Quantity = qShares,
            Amount = qAmt,
            NetCashImpact = netImpact,
            DisplayText = qText
        };
    }

    /// <summary>
    /// V8.2 GetCashCappedBuyTrade：现金上限买入兜底。
    /// 当前档位剩余额度过大、现金无法一次打满时，按现有现金买入最大可买数量。
    /// 只在显式策略启用，不得默认启用。
    /// </summary>
    public QuantifiedTradeResult CashCappedBuy(string tradeSource, double cashLimit, double price, double fee = 0)
    {
        if (cashLimit <= 0 || price <= 0)
            return QuantifiedTradeResult.NotExecutable("现金或价格无效");

        double usableCash = cashLimit - fee;
        if (usableCash <= 0)
            return QuantifiedTradeResult.NotExecutable("可用现金不足");

        return Quantify("买入", tradeSource, usableCash, price, fee, cashLimit);
    }
}
