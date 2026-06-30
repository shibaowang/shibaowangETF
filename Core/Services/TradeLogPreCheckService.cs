using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// V8.2 PreCheck 预检引擎。
/// 校验入金/出金净现金流、买卖分红送股拆分合并除权校准的合法性。
/// </summary>
public class TradeLogPreCheckService
{
    public struct PreCheckError
    {
        public int RowIndex;
        public string Message;
        public string Column;
    }

    public List<PreCheckError> Errors { get; } = new();

    /// <summary>
    /// V8.2 GetFundingNetImpact 等价实现。
    /// </summary>
    public static double GetFundingNetImpact(string action, double amount, double fee, double? existingNetImpact = null)
    {
        if (fee < 0)
            throw new ArgumentException("资金行手续费不能小于0。");
        if (amount < 0)
            throw new ArgumentException("资金行金额不能小于0。");

        if (action == "入金")
        {
            double expectedNet = amount - fee;
            if (expectedNet < -0.01)
                throw new ArgumentException("入金净现金流不能为负。");
            if (existingNetImpact.HasValue && Math.Abs(existingNetImpact.Value) > 0.0000001)
            {
                if (existingNetImpact.Value < 0)
                    throw new ArgumentException("入金行净现金流不能为负。");
                if (Math.Abs(existingNetImpact.Value - expectedNet) > 0.01)
                    throw new ArgumentException("入金行净现金流应等于金额-手续费。");
                return existingNetImpact.Value;
            }
            return expectedNet;
        }
        else if (action == "出金")
        {
            double expectedNet = -(amount + fee);
            if (existingNetImpact.HasValue && Math.Abs(existingNetImpact.Value) > 0.0000001)
            {
                if (existingNetImpact.Value > 0)
                    throw new ArgumentException("出金行净现金流不能为正。");
                if (Math.Abs(existingNetImpact.Value - expectedNet) > 0.01)
                    throw new ArgumentException("出金行净现金流应等于-(金额+手续费)。");
                return existingNetImpact.Value;
            }
            return expectedNet;
        }
        return 0;
    }

    public bool PreCheck(IEnumerable<TradeLogEntry> entries)
    {
        Errors.Clear();
        if (entries == null) return false;

        foreach (var entry in entries)
        {
            int r = entry.RowIndex;

            if (string.IsNullOrWhiteSpace(entry.StrategyCode))
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = "缺少实际或策略代码，预检拦截！", Column = "策略代码" });
                continue;
            }

            string action = entry.Action?.Trim() ?? "";
            if (string.IsNullOrEmpty(action))
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = "缺少动作。", Column = "动作" });
                continue;
            }

            // 入金/出金校验
            if (action == "入金" || action == "出金")
            {
                if (!string.Equals(entry.StrategyCode, "CASH", StringComparison.OrdinalIgnoreCase))
                {
                    Errors.Add(new PreCheckError
                    {
                        RowIndex = r,
                        Message = $"第 {r} 行为{action}记录，策略代码必须填写 CASH，预检拦截！",
                        Column = "策略代码"
                    });
                }

                if (entry.Amount < 0)
                {
                    Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行金额不能小于0，预检拦截！", Column = "金额" });
                }

                if (entry.Fee < 0)
                {
                    Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行手续费不能小于0，预检拦截！", Column = "手续费" });
                }

                if (Math.Abs(entry.CashBalance) < 0.000001 && entry.CashBalance == 0 && entry.Amount == 0)
                {
                    Errors.Add(new PreCheckError
                    {
                        RowIndex = r,
                        Message = $"第 {r} 行为{action}记录，金额或净现金流至少填写一个有效数字，预检拦截！",
                        Column = "金额"
                    });
                }

                if (Math.Abs(entry.NetCashImpact) > 0.0000001)
                {
                    if (action == "入金" && entry.NetCashImpact < 0)
                    {
                        Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行入金净现金流不能为负数，预检拦截！", Column = "净现金流" });
                    }
                    if (action == "出金" && entry.NetCashImpact > 0)
                    {
                        Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行出金净现金流应为负数，预检拦截！", Column = "净现金流" });
                    }
                }

                try
                {
                    GetFundingNetImpact(action, entry.Amount, entry.Fee,
                        Math.Abs(entry.NetCashImpact) > 0.0000001 ? entry.NetCashImpact : null);
                }
                catch (ArgumentException ex)
                {
                    Errors.Add(new PreCheckError { RowIndex = r, Message = ex.Message, Column = "净现金流" });
                }
                continue;
            }

            // 非资金行动作校验
            if (Array.IndexOf(TradeLogEntry.ValidActions, action) < 0)
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行动作 [{action}] 不合法。", Column = "动作" });
            }

            if (double.IsNaN(entry.Price) || double.IsInfinity(entry.Price))
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行价格非数值。", Column = "价格" });
            }

            if (double.IsNaN(entry.Quantity) || double.IsInfinity(entry.Quantity))
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行数量非数值。", Column = "数量" });
            }

            if (double.IsNaN(entry.Amount) || double.IsInfinity(entry.Amount))
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行金额非数值。", Column = "金额" });
            }

            if (entry.Fee < 0)
            {
                Errors.Add(new PreCheckError { RowIndex = r, Message = $"第 {r} 行手续费不能小于0。", Column = "手续费" });
            }
        }

        return Errors.Count == 0;
    }
}
