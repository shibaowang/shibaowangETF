namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record BrokerHoldingPnlMetrics(
    string StrategyCode,
    double OpenCycleNetInvestment,
    double DilutedCostAmount,
    double? DilutedAverageCost,
    double? BrokerHoldingPnl,
    double? BrokerHoldingReturnRate,
    double TotalQuantity,
    double? MarketValue,
    int ActiveInstrumentCount);
