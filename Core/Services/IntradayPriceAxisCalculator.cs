namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed record IntradayPriceAxis(
    double PreviousClose,
    double DisplayMin,
    double DisplayMax)
{
    public double ZeroLineRatio => GetVerticalRatio(PreviousClose);

    public double GetVerticalRatio(double price)
        => (DisplayMax - price) / (DisplayMax - DisplayMin);
}

public static class IntradayPriceAxisCalculator
{
    private const double MinimumDeltaRatio = 0.001;

    public static bool TryCreate(double? previousClose, IEnumerable<double> prices, out IntradayPriceAxis axis)
    {
        axis = new IntradayPriceAxis(0, 0, 1);
        if (!IsValidPositive(previousClose))
        {
            return false;
        }

        double basePrice = previousClose!.Value;
        double maxAbove = 0;
        double maxBelow = 0;
        foreach (double price in prices.Where(price => IsValidPositive(price)))
        {
            maxAbove = Math.Max(maxAbove, price - basePrice);
            maxBelow = Math.Max(maxBelow, basePrice - price);
        }

        double maxDelta = Math.Max(Math.Max(maxAbove, maxBelow), basePrice * MinimumDeltaRatio);
        axis = new IntradayPriceAxis(basePrice, basePrice - maxDelta, basePrice + maxDelta);
        return true;
    }

    private static bool IsValidPositive(double? value)
        => value is double number && number > 0 && !double.IsNaN(number) && !double.IsInfinity(number);
}
