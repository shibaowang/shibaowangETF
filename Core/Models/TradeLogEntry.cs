namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// V8.2 TradeLog 15列模型，账户唯一事实源。
/// </summary>
public class TradeLogEntry
{
    public int RowIndex { get; set; }
    public DateTime Time { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string ActualCode { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Price { get; set; }
    public double Quantity { get; set; }
    public double Amount { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Fee { get; set; }
    public string Memo { get; set; } = string.Empty;
    public double NetCashImpact { get; set; }
    public double Principal { get; set; }
    public double CashBalance { get; set; }
    public double TotalAssets { get; set; }

    public static readonly string[] RequiredHeaders =
    {
        "时间", "策略代码", "实际代码", "动作", "价格", "数量", "金额",
        "档位", "来源", "手续费", "备注", "净现金流", "本金", "现金余额", "总资产"
    };

    public static readonly string[] ValidActions =
    {
        "买入", "卖出", "分红", "送股", "拆分", "合并", "除权校准", "CASH", "入金", "出金"
    };

    public static readonly string[] TierNames =
    {
        "战略底仓", "狙击一档", "狙击二档", "狙击三档", "狙击四档", "狙击五档", "狙击六档"
    };

    public bool IsFunding => Action == "入金" || Action == "出金";
    public bool IsBuy => Action == "买入";
    public bool IsSell => Action == "卖出";
    public bool IsDividend => Action == "分红";
    public bool IsCorporateAction => Action == "送股" || Action == "拆分" || Action == "合并" || Action == "除权校准";
    public bool IsTierBuy => IsBuy && Array.IndexOf(TierNames, Tier) >= 0;
    public bool IsStrategicBase => Tier == "战略底仓";
}
