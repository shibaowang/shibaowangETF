namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class CapitalPositionReadModel
{
    public AccountReplayStateRecord? Account { get; init; }
    public IReadOnlyList<PositionReplayStateRecord> Positions { get; init; } = Array.Empty<PositionReplayStateRecord>();
    public IReadOnlyList<OtcPositionReplayStateRecord> OtcPositions { get; init; } = Array.Empty<OtcPositionReplayStateRecord>();
    public IReadOnlyList<StrategyConfigRecord> Strategies { get; init; } = Array.Empty<StrategyConfigRecord>();
    public IReadOnlyList<OtcChannelRecord> OtcChannels { get; init; } = Array.Empty<OtcChannelRecord>();
    public IReadOnlyList<MarketQuoteRecord> Quotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public StrategyDecisionStateRecord? LatestDecision { get; init; }
    public DateTimeOffset ReadAt { get; init; }
}

public sealed class CapitalPositionSnapshot
{
    public bool HasAccount { get; init; }
    public bool IsValuationComplete { get; init; }
    public CapitalPositionAccountSummary Summary { get; init; } = new();
    public IReadOnlyList<CapitalPositionEtfRow> EtfRows { get; init; } = Array.Empty<CapitalPositionEtfRow>();
    public IReadOnlyList<CapitalPositionOtcRow> OtcRows { get; init; } = Array.Empty<CapitalPositionOtcRow>();
    public IReadOnlyList<CapitalPositionStrategyAllocationRow> StrategyRows { get; init; } = Array.Empty<CapitalPositionStrategyAllocationRow>();
    public DateTimeOffset ReadAt { get; init; }
    public string SnapshotKey { get; init; } = string.Empty;
    public string ReadAtText { get; init; } = "--";
    public string AccountStatusText { get; init; } = "暂无账户回放结果";
    public string AccountStatusColor { get; init; } = "#F5A623";
    public string CalculatedAtText { get; init; } = "--";
    public string ReplayError { get; init; } = string.Empty;
    public string ReplayErrorSummary { get; init; } = "--";
    public string DataSourceExplanation { get; init; } =
        "账户金额来自 TradeLog 回放；行情金额使用最近一次持久化回放估值；本窗口只读，不重新计算或写入账务数据。";
    public bool HasEtfRows => EtfRows.Count > 0;
    public bool HasOtcRows => OtcRows.Count > 0;
}

public sealed class CapitalPositionAccountSummary
{
    public double? TotalAssets { get; init; }
    public double? CashBalance { get; init; }
    public double? Principal { get; init; }
    public double? KnownMarketValue { get; init; }
    public double? EtfMarketValue { get; init; }
    public double? OtcMarketValue { get; init; }
    public double? TotalUnrealizedPnl { get; init; }
    public double? TotalRealizedPnl { get; init; }
    public double? PositionRatio { get; init; }
    public double? RealSniperPool { get; init; }
    public double? BaseCompletionRate { get; init; }
    public double? CashRatio { get; init; }
    public double? EtfRatio { get; init; }
    public double? OtcRatio { get; init; }
    public double PositionRatioProgress { get; init; }
    public double BaseCompletionProgress { get; init; }
    public double CashRatioProgress { get; init; }
    public double EtfRatioProgress { get; init; }
    public double OtcRatioProgress { get; init; }
    public string TotalAssetsText { get; init; } = "--";
    public string CashBalanceText { get; init; } = "--";
    public string PrincipalText { get; init; } = "--";
    public string KnownMarketValueText { get; init; } = "--";
    public string EtfMarketValueText { get; init; } = "--";
    public string OtcMarketValueText { get; init; } = "--";
    public string TotalUnrealizedPnlText { get; init; } = "--";
    public string TotalRealizedPnlText { get; init; } = "--";
    public string PositionRatioText { get; init; } = "--";
    public string RealSniperPoolText { get; init; } = "--";
    public string BaseCompletionRateText { get; init; } = "--";
    public string CashRatioText { get; init; } = "--";
    public string EtfRatioText { get; init; } = "--";
    public string OtcRatioText { get; init; } = "--";
    public string ReplayStatusText { get; init; } = "暂无账户回放结果";
    public string TotalAssetsColor { get; init; } = "#EAF6FF";
    public string CashBalanceColor { get; init; } = "#EAF6FF";
    public string TotalUnrealizedPnlColor { get; init; } = "#EAF6FF";
    public string TotalRealizedPnlColor { get; init; } = "#EAF6FF";
    public string ReplayStatusColor { get; init; } = "#F5A623";
}

public sealed class CapitalPositionEtfRow
{
    public long ReplayId { get; init; }
    public string StrategyCode { get; init; } = string.Empty;
    public string EtfName { get; init; } = "--";
    public string ActualCode { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public double Quantity { get; init; }
    public double AverageCost { get; init; }
    public double? MarketPrice { get; init; }
    public double? MarketValue { get; init; }
    public double? UnrealizedPnl { get; init; }
    public double? ReturnRate { get; init; }
    public double? AssetRatio { get; init; }
    public string QuantityText { get; init; } = "--";
    public string AverageCostText { get; init; } = "--";
    public string MarketPriceText { get; init; } = "--";
    public string MarketValueText { get; init; } = "--";
    public string UnrealizedPnlText { get; init; } = "--";
    public string ReturnRateText { get; init; } = "--";
    public string AssetRatioText { get; init; } = "--";
    public string QuoteSource { get; init; } = string.Empty;
    public string QuoteSourceText { get; init; } = "--";
    public string QuoteTimeText { get; init; } = "--";
    public string ReceivedAtText { get; init; } = "--";
    public string CacheStatus { get; init; } = "未关联";
    public string CacheAgeText { get; init; } = "--";
    public string CacheToolTip { get; init; } = "未关联到本次回放估值价格对应的真实行情缓存";
    public string UnrealizedPnlColor { get; init; } = "#EAF6FF";
}

public sealed class CapitalPositionOtcRow
{
    public long ReplayId { get; init; }
    public string StrategyCode { get; init; } = string.Empty;
    public string FundCode { get; init; } = string.Empty;
    public string FundName { get; init; } = "--";
    public double Quantity { get; init; }
    public double CostAmount { get; init; }
    public double AverageCost { get; init; }
    public double? Nav { get; init; }
    public double? MarketValue { get; init; }
    public double? UnrealizedPnl { get; init; }
    public double? ReturnRate { get; init; }
    public double? AssetRatio { get; init; }
    public int? ChannelPriority { get; init; }
    public string QuantityText { get; init; } = "--";
    public string CostAmountText { get; init; } = "--";
    public string AverageCostText { get; init; } = "--";
    public string NavText { get; init; } = "--";
    public string MarketValueText { get; init; } = "--";
    public string UnrealizedPnlText { get; init; } = "--";
    public string ReturnRateText { get; init; } = "--";
    public string AssetRatioText { get; init; } = "--";
    public string ChannelPriorityText { get; init; } = "--";
    public string QuoteSource { get; init; } = string.Empty;
    public string QuoteSourceText { get; init; } = "--";
    public string QuoteTimeText { get; init; } = "--";
    public string ReceivedAtText { get; init; } = "--";
    public string CacheStatus { get; init; } = "未关联";
    public string CacheAgeText { get; init; } = "--";
    public string CacheToolTip { get; init; } = "未关联到本次回放估值净值对应的真实行情缓存";
    public string UnrealizedPnlColor { get; init; } = "#EAF6FF";
}

public sealed class CapitalPositionStrategyAllocationRow
{
    public string StrategyCode { get; init; } = string.Empty;
    public string StrategyName { get; init; } = "--";
    public double? EtfMarketValue { get; init; }
    public double? OtcMarketValue { get; init; }
    public double? TotalMarketValue { get; init; }
    public double? AssetRatio { get; init; }
    public double AssetRatioProgress { get; init; }
    public string EtfMarketValueText { get; init; } = "--";
    public string OtcMarketValueText { get; init; } = "--";
    public string TotalMarketValueText { get; init; } = "--";
    public string AssetRatioText { get; init; } = "--";
}
