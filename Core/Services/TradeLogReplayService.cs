using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 ReplayCashFromLog / ReplayPositionFromLog 等价实现。
/// 从 TradeLog 顺序重演现金、持仓、成本、已实现盈亏。
/// </summary>
public class TradeLogReplayService
{
    private const double Epsilon = 0.015;

    public TradeLogReplayResult Replay(IEnumerable<TradeLogEntry> entries)
    {
        var result = new TradeLogReplayResult();
        if (entries == null) return result;

        var sorted = entries.OrderBy(e => e.Time).ThenBy(e => e.RowIndex).ToList();
        double cash = 0;
        double totalIn = 0;
        double totalOut = 0;
        double realizedPnl = 0;
        double totalFee = 0;
        double totalDividend = 0;

        foreach (var entry in sorted)
        {
            switch (entry.Action)
            {
                case "入金":
                    totalIn += entry.Amount;
                    cash += TradeLogPreCheckService.GetFundingNetImpact("入金", entry.Amount, entry.Fee,
                        Math.Abs(entry.NetCashImpact) > 0.0000001 ? entry.NetCashImpact : null);
                    totalFee += entry.Fee;
                    break;

                case "出金":
                    totalOut += entry.Amount;
                    cash += TradeLogPreCheckService.GetFundingNetImpact("出金", entry.Amount, entry.Fee,
                        Math.Abs(entry.NetCashImpact) > 0.0000001 ? entry.NetCashImpact : null);
                    totalFee += entry.Fee;
                    break;

                case "买入":
                    cash -= (entry.Amount + entry.Fee);
                    totalFee += entry.Fee;
                    ApplyBuy(result, entry);
                    break;

                case "卖出":
                    cash += (entry.Amount - entry.Fee);
                    totalFee += entry.Fee;
                    ApplySell(result, entry);
                    break;

                case "分红":
                    cash += (entry.Amount - entry.Fee);
                    totalDividend += entry.Amount;
                    totalFee += entry.Fee;
                    break;

                case "送股":
                case "拆分":
                    ApplyShareIncrease(result, entry);
                    break;

                case "合并":
                    ApplyShareDecrease(result, entry);
                    break;

                case "除权校准":
                    ApplyAdjustment(result, entry);
                    break;
            }

            // 校验现金余额一致性
            if (Math.Abs(entry.CashBalance) > 0.000001)
            {
                double diff = Math.Abs(cash - entry.CashBalance);
                if (diff > 0.02)
                {
                    result.ReplayWarnings.Add(
                        $"第 {entry.RowIndex} 行: Replay 现金余额 {cash:F2} 与日志现金余额 {entry.CashBalance:F2} 偏差 {diff:F2}");
                }
            }
        }

        realizedPnl = result.Holdings.Values.Sum(h => h.RealizedPnl);

        result.CashBalance = cash;
        result.TotalBuyAmount = result.Holdings.Values.Sum(h => h.TotalBuyAmount);
        result.TotalSellAmount = result.Holdings.Values.Sum(h => h.TotalSellAmount);
        result.RealizedPnl = realizedPnl;
        result.TotalFee = totalFee;
        result.TotalDividend = totalDividend;

        // PrincipalBase = 入金 - 出金 + 已实现盈亏 - 手续费 + 分红
        result.PrincipalBase = totalIn - totalOut + realizedPnl - totalFee + totalDividend;
        result.PrincipalBaseSource = "Replay";

        return result;
    }

    private static void ApplyBuy(TradeLogReplayResult result, TradeLogEntry entry)
    {
        string key = entry.StrategyCode;
        if (!result.Holdings.ContainsKey(key))
            result.Holdings[key] = new HoldingSnapshot { StrategyCode = key };

        var h = result.Holdings[key];
        bool isOtc = string.Equals(entry.Source, "场外替代", StringComparison.OrdinalIgnoreCase);

        if (isOtc)
        {
            h.OtcQuantity += entry.Quantity;
            h.OtcCost += entry.Amount;
        }
        else
        {
            h.EtfQuantity += entry.Quantity;
            h.EtfCost += entry.Amount;
        }
        h.TotalBuyAmount += entry.Amount;
    }

    private static void ApplySell(TradeLogReplayResult result, TradeLogEntry entry)
    {
        string key = entry.StrategyCode;
        if (!result.Holdings.ContainsKey(key))
        {
            result.ReplayErrors.Add($"第 {entry.RowIndex} 行: 卖出 [{key}] 但无持仓记录。");
            return;
        }

        var h = result.Holdings[key];
        bool isOtc = string.Equals(entry.Source, "场外替代", StringComparison.OrdinalIgnoreCase);

        double availableQty = isOtc ? h.OtcQuantity : h.EtfQuantity;
        double totalCost = isOtc ? h.OtcCost : h.EtfCost;

        if (entry.Quantity > availableQty + Epsilon)
        {
            result.ReplayErrors.Add(
                $"第 {entry.RowIndex} 行: 卖出 [{key}] {entry.Quantity} 超过持仓 {availableQty}。");
            return;
        }

        double costPart = availableQty > 0 ? (totalCost / availableQty) * entry.Quantity : 0;
        double pnl = entry.Amount - entry.Fee - costPart;

        if (isOtc)
        {
            h.OtcQuantity -= entry.Quantity;
            h.OtcCost -= costPart;
            if (h.OtcQuantity < 0) h.OtcQuantity = 0;
            if (h.OtcCost < 0) h.OtcCost = 0;
        }
        else
        {
            h.EtfQuantity -= entry.Quantity;
            h.EtfCost -= costPart;
            if (h.EtfQuantity < 0) h.EtfQuantity = 0;
            if (h.EtfCost < 0) h.EtfCost = 0;
        }

        h.RealizedPnl += pnl;
        h.TotalSellAmount += entry.Amount;
    }

    private static void ApplyShareIncrease(TradeLogReplayResult result, TradeLogEntry entry)
    {
        string key = entry.StrategyCode;
        if (!result.Holdings.ContainsKey(key))
            result.Holdings[key] = new HoldingSnapshot { StrategyCode = key };

        var h = result.Holdings[key];
        bool isOtc = string.Equals(entry.Source, "场外替代", StringComparison.OrdinalIgnoreCase);

        if (isOtc)
            h.OtcQuantity += entry.Quantity;
        else
            h.EtfQuantity += entry.Quantity;
    }

    private static void ApplyShareDecrease(TradeLogReplayResult result, TradeLogEntry entry)
    {
        string key = entry.StrategyCode;
        if (!result.Holdings.ContainsKey(key))
        {
            result.ReplayErrors.Add($"第 {entry.RowIndex} 行: 合并 [{key}] 但无持仓记录。");
            return;
        }

        var h = result.Holdings[key];
        bool isOtc = string.Equals(entry.Source, "场外替代", StringComparison.OrdinalIgnoreCase);

        if (isOtc)
        {
            h.OtcQuantity -= entry.Quantity;
            if (h.OtcQuantity < 0) h.OtcQuantity = 0;
        }
        else
        {
            h.EtfQuantity -= entry.Quantity;
            if (h.EtfQuantity < 0) h.EtfQuantity = 0;
        }
    }

    private static void ApplyAdjustment(TradeLogReplayResult result, TradeLogEntry entry)
    {
        string key = entry.StrategyCode;
        if (!result.Holdings.ContainsKey(key))
            result.Holdings[key] = new HoldingSnapshot { StrategyCode = key };

        result.Holdings[key].AdjustmentFactor = entry.Quantity > 0 ? entry.Quantity : 1.0;
    }
}
