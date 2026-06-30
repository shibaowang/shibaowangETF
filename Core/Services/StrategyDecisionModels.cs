using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class StrategyDecisionCalculationInput
{
    public IReadOnlyList<StrategyConfigRecord> Strategies { get; init; } = Array.Empty<StrategyConfigRecord>();
    public AccountStateRecord? AccountState { get; init; }
    public AccountReplayStateRecord? AccountReplayState { get; init; }
    public IReadOnlyList<PositionStateRecord> PositionStates { get; init; } = Array.Empty<PositionStateRecord>();
    public IReadOnlyList<PositionReplayStateRecord> PositionReplayStates { get; init; } = Array.Empty<PositionReplayStateRecord>();
    public IReadOnlyList<OtcPositionReplayStateRecord> OtcPositionReplayStates { get; init; } = Array.Empty<OtcPositionReplayStateRecord>();
    public IReadOnlyList<OtcChannelRecord> OtcChannels { get; init; } = Array.Empty<OtcChannelRecord>();
    public IReadOnlyList<TradeLogRecord> TradeLogs { get; init; } = Array.Empty<TradeLogRecord>();
    public IReadOnlyList<MarketQuoteRecord> MarketQuotes { get; init; } = Array.Empty<MarketQuoteRecord>();
    public IReadOnlyList<MarketQuoteRecord> MarketHistory { get; init; } = Array.Empty<MarketQuoteRecord>();
    public BasePositionSettings BasePositionSettings { get; init; } = BasePositionSettings.Default();
    public bool RequireGlobalHistoryReady { get; init; } = true;
}

public sealed class StrategyDecisionCalculationResult
{
    public StrategyDecisionCalculationResult(
        IReadOnlyList<StrategyDecisionStateRecord> decisions,
        IReadOnlyList<StrategyDecisionRuntimeWarning> warnings)
    {
        Decisions = decisions;
        Warnings = warnings;
    }

    public IReadOnlyList<StrategyDecisionStateRecord> Decisions { get; }
    public IReadOnlyList<StrategyDecisionRuntimeWarning> Warnings { get; }
}

public sealed record StrategyDecisionRuntimeWarning(
    string Level,
    string Module,
    string Message,
    string Detail);
