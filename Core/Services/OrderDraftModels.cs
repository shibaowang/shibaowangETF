using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class OrderDraftCalculationInput
{
    public IReadOnlyList<StrategyDecisionStateRecord> StrategyDecisions { get; init; } = Array.Empty<StrategyDecisionStateRecord>();
    public AccountReplayStateRecord? AccountReplayState { get; init; }
    public IReadOnlyList<PositionReplayStateRecord> PositionReplayStates { get; init; } = Array.Empty<PositionReplayStateRecord>();
    public IReadOnlyList<OtcPositionReplayStateRecord> OtcPositionReplayStates { get; init; } = Array.Empty<OtcPositionReplayStateRecord>();
    public IReadOnlyList<OtcChannelRecord> OtcChannels { get; init; } = Array.Empty<OtcChannelRecord>();
    public IReadOnlyList<TradeLogRecord> TradeLogs { get; init; } = Array.Empty<TradeLogRecord>();
    public IReadOnlyList<MarketQuoteRecord> MarketQuotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public DateTime Today { get; init; } = DateTime.Today;
}

public sealed class OrderDraftCalculationResult
{
    public OrderDraftCalculationResult(
        IReadOnlyList<OrderDraftStateRecord> drafts,
        IReadOnlyList<OrderDraftLegStateRecord> legs,
        IReadOnlyList<OrderDraftRuntimeWarning> warnings)
    {
        Drafts = drafts;
        Legs = legs;
        Warnings = warnings;
    }

    public IReadOnlyList<OrderDraftStateRecord> Drafts { get; }
    public IReadOnlyList<OrderDraftLegStateRecord> Legs { get; }
    public IReadOnlyList<OrderDraftRuntimeWarning> Warnings { get; }
}

public sealed record OrderDraftRuntimeWarning(
    string Level,
    string Module,
    string Message,
    string Detail);
