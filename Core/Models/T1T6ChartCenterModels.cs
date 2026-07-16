namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class T1T6ChartCenterReadModel
{
    public IReadOnlyList<StrategyConfigRecord> EnabledStrategies { get; init; } = Array.Empty<StrategyConfigRecord>();
    public IReadOnlyList<StrategyDecisionStateRecord> LatestDecisions { get; init; } = Array.Empty<StrategyDecisionStateRecord>();
    public IReadOnlyList<MarketQuoteRecord> RelatedQuotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public IReadOnlyList<MarketSourceStatusRecord> RelatedSourceStatuses { get; init; } = Array.Empty<MarketSourceStatusRecord>();
    public DateTimeOffset ReadAt { get; init; }
    public string? ReadError { get; init; }
}

public sealed record T1T6StrategyReadItem(
    StrategyConfigRecord StrategyConfig,
    StrategyDecisionStateRecord? LatestDecision,
    MarketQuoteRecord? SelectedQuote,
    MarketSourceStatusRecord? RelatedSourceStatus);

public sealed class T1T6TierDisplayDefinition
{
    public int TierNumber { get; init; }
    public string TierCode { get; init; } = string.Empty;
    public string TierName { get; init; } = string.Empty;
    public double TriggerDrawdown { get; init; }
    public double ConfiguredWeight { get; init; }
    public double CumulativeWeight { get; init; }
    public double CumulativeWeightRatio { get; init; }
    public bool IsConditionMet { get; init; }
    public bool IsCurrentSuggestedTier { get; init; }
    public string ConditionStatusText { get; init; } = string.Empty;
    public string WeightText { get; init; } = string.Empty;
    public string CumulativeWeightText { get; init; } = string.Empty;
    public string CumulativeWeightRatioText { get; init; } = string.Empty;
    public string TriggerText { get; init; } = string.Empty;
}

public sealed class T1T6StrategyRow
{
    public long StrategyConfigId { get; init; }
    public string StrategyCode { get; init; } = string.Empty;
    public string StrategyName { get; init; } = string.Empty;
    public string EtfCode { get; init; } = string.Empty;
    public string EtfName { get; init; } = string.Empty;
    public string IndexSecId { get; init; } = string.Empty;
    public bool IsDuplicateEtfCode { get; init; }
    public int DuplicateEtfStrategyCount { get; init; }
    public string DuplicateHintText { get; init; } = string.Empty;
    public double? LatestPrice { get; init; }
    public string LatestPriceText { get; init; } = "--";
    public string QuoteSource { get; init; } = "--";
    public string QuoteTime { get; init; } = "--";
    public string ReceivedAt { get; init; } = "--";
    public string QuoteStatus { get; init; } = string.Empty;
    public string QuoteStatusText { get; init; } = string.Empty;
    public string QuoteToolTip { get; init; } = string.Empty;
    public bool IsQuoteHealthy { get; init; }
    public bool HasDecision { get; init; }
    public string DecisionCalculatedAt { get; init; } = string.Empty;
    public string DecisionCalculatedAtText { get; init; } = "--";
    public double? CurrentIndexDrawdown { get; init; }
    public string CurrentIndexDrawdownText { get; init; } = "--";
    public double? CurrentPremium { get; init; }
    public string CurrentPremiumText { get; init; } = "--";
    public string CurrentAction { get; init; } = "--";
    public string CurrentSuggestedTier { get; init; } = string.Empty;
    public string CurrentSuggestedTierText { get; init; } = "--";
    public string DecisionStatusText { get; init; } = string.Empty;
    public string DecisionToolTip { get; init; } = string.Empty;
    public string DataStatusText { get; init; } = string.Empty;
    public string DataStatusToolTip { get; init; } = string.Empty;
    public bool CanOpenChart { get; init; }
    public string ChartToolTip { get; init; } = string.Empty;
    public IReadOnlyList<T1T6TierDisplayDefinition> Tiers { get; init; } = Array.Empty<T1T6TierDisplayDefinition>();
}

public sealed class T1T6ChartCenterSnapshot
{
    public IReadOnlyList<T1T6StrategyRow> Rows { get; init; } = Array.Empty<T1T6StrategyRow>();
    public T1T6StrategyRow? SelectedRow { get; init; }
    public string? SelectedStrategyCode { get; init; }
    public int EnabledStrategyCount { get; init; }
    public int HealthyQuoteCount { get; init; }
    public int MissingDecisionCount { get; init; }
    public int DuplicateEtfStrategyCount { get; init; }
    public string DuplicateEtfToolTip { get; init; } = "无同标的多策略";
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset ReadAt { get; init; }
    public string? ReadError { get; init; }
    public string ReadStatusText { get; init; } = string.Empty;
}

public sealed record T1T6ChartOpenRequest(
    string MarketType,
    string Symbol,
    string DisplayName,
    string StrategyCode);
