using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartChangePercentCalculator
{
    public static double? ResolveChangeForPeriod(
        SecurityChartPeriod period,
        MarketQuoteRecord? quote,
        IReadOnlyList<IntradayPoint> intradayPoints,
        IReadOnlyList<KLinePoint> kLines)
        => period == SecurityChartPeriod.Intraday
            ? CalculateIntradayChange(quote, intradayPoints)
            : CalculateKLineChange(kLines);

    public static double? CalculateIntradayChange(MarketQuoteRecord? quote, IReadOnlyList<IntradayPoint> intradayPoints)
    {
        if (IsValidFinite(quote?.ChangePercent))
        {
            return quote!.ChangePercent!.Value;
        }

        double? latestPrice = IsValidPositive(quote?.Price)
            ? quote!.Price
            : intradayPoints.LastOrDefault(point => point.Price > 0)?.Price;

        return CalculateChange(latestPrice, quote?.LastClose);
    }

    public static double? CalculateKLineChange(IReadOnlyList<KLinePoint> kLines)
    {
        if (kLines.Count < 2)
        {
            return null;
        }

        return CalculateChange(kLines[^1].Close, kLines[^2].Close);
    }

    public static double? CalculateChange(double? currentClose, double? previousClose)
    {
        if (!IsValidPositive(currentClose) || !IsValidPositive(previousClose))
        {
            return null;
        }

        return currentClose!.Value / previousClose!.Value - 1.0;
    }

    private static bool IsValidPositive(double? value)
        => value is double number && number > 0 && !double.IsNaN(number) && !double.IsInfinity(number);

    private static bool IsValidFinite(double? value)
        => value is double number && !double.IsNaN(number) && !double.IsInfinity(number);
}
