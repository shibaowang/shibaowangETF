namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

/// <summary>
/// Replay 引擎输出：包含本金基准、现金余额、持仓、已实现盈亏。
/// </summary>
public class TradeLogReplayResult
{
    public double PrincipalBase { get; set; }
    public double CashBalance { get; set; }
    public double TotalBuyAmount { get; set; }
    public double TotalSellAmount { get; set; }
    public double RealizedPnl { get; set; }
    public double TotalFee { get; set; }
    public double TotalDividend { get; set; }

    public Dictionary<string, HoldingSnapshot> Holdings { get; set; } = new();

    public List<string> ReplayErrors { get; set; } = new();
    public List<string> ReplayWarnings { get; set; } = new();

    /// <summary>
    /// Replay / Fallback / Uninitialized
    /// </summary>
    public string PrincipalBaseSource { get; set; } = "Uninitialized";

    public bool OverallSuccess => ReplayErrors.Count == 0;
    public bool HasWarnings => ReplayWarnings.Count > 0;
}
