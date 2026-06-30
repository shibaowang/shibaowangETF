using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class EtfDecisionTableMetrics
{
    public static double? CalculatePremiumRate(MarketQuoteRecord? quote)
    {
        if (quote?.Price is not double price || quote.Iopv is not double iopv || iopv <= 0)
        {
            return null;
        }

        return (price - iopv) / iopv;
    }

    public static EtfPositionCostMetrics CalculatePositionCostMetrics(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions)
    {
        var replayList = replayPositions.ToList();
        var otcList = otcPositions.ToList();
        bool hasOtcDetails = otcList.Count > 0;
        IEnumerable<PositionReplayStateRecord> marketReplayPositions = hasOtcDetails
            ? replayList.Where(position => !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            : replayList;
        double marketQuantity = marketReplayPositions.Sum(position => position.Quantity);
        double marketCost = marketReplayPositions
            .Where(position => !string.Equals(position.Source, "场外替代", StringComparison.Ordinal))
            .Sum(position => position.CostAmount);
        if (!hasOtcDetails)
        {
            marketCost = marketReplayPositions.Sum(position => position.CostAmount);
        }

        double otcQuantity = hasOtcDetails ? otcList.Sum(position => position.Quantity) : 0;
        double otcCost = otcPositions.Sum(position => position.CostAmount);
        double totalQuantity = marketQuantity + otcQuantity;
        double totalCostAmount = marketCost + otcCost;
        double averageCost = totalQuantity > 0 ? totalCostAmount / totalQuantity : 0;
        return new EtfPositionCostMetrics(totalQuantity, totalCostAmount, averageCost);
    }

    public static double CalculateCompositeCost(
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions)
        => CalculatePositionCostMetrics(replayPositions, otcPositions).TotalCostAmount;

    public static double? CalculatePrincipalRatio(double totalCostAmount, double principal)
        => principal > 0 && totalCostAmount > 0 ? totalCostAmount / principal : null;

    public static double? CalculateHoldingPnl(double? marketValue, double totalCostAmount)
        => marketValue.HasValue && totalCostAmount > 0 ? marketValue.Value - totalCostAmount : null;

    public static double? CalculateHoldingReturnRate(double? holdingPnl, double totalCostAmount)
        => holdingPnl.HasValue && totalCostAmount > 0 ? holdingPnl.Value / totalCostAmount : null;
}

public sealed record EtfPositionCostMetrics(double TotalQuantity, double TotalCostAmount, double AverageCost);
