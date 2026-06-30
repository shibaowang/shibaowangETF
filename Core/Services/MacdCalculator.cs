using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class MacdCalculator
{
    public const int FastPeriod = 12;
    public const int SlowPeriod = 26;
    public const int SignalPeriod = 9;
    public const int MinimumInputCount = SlowPeriod + SignalPeriod;

    public static IReadOnlyList<MacdPoint> Calculate(IEnumerable<KLinePoint> kLines)
        => CalculateCore(kLines.Select(point => (Time: point.Date, Close: point.Close)));

    public static IReadOnlyList<MacdPoint> CalculateFromPrices(IEnumerable<(DateTime Time, double Close)> prices)
        => CalculateCore(prices);

    private static IReadOnlyList<MacdPoint> CalculateCore(IEnumerable<(DateTime Time, double Close)> prices)
    {
        (DateTime Time, double Close)[] ordered = prices
            .Where(point => point.Close > 0)
            .OrderBy(point => point.Time)
            .ToArray();
        if (ordered.Length < MinimumInputCount)
        {
            return Array.Empty<MacdPoint>();
        }

        double fastAlpha = 2.0 / (FastPeriod + 1);
        double slowAlpha = 2.0 / (SlowPeriod + 1);
        double signalAlpha = 2.0 / (SignalPeriod + 1);
        double emaFast = ordered[0].Close;
        double emaSlow = ordered[0].Close;
        double dea = 0;
        var result = new List<MacdPoint>(ordered.Length);

        for (int i = 0; i < ordered.Length; i++)
        {
            double close = ordered[i].Close;
            if (i == 0)
            {
                emaFast = close;
                emaSlow = close;
            }
            else
            {
                emaFast = close * fastAlpha + emaFast * (1 - fastAlpha);
                emaSlow = close * slowAlpha + emaSlow * (1 - slowAlpha);
            }

            double dif = emaFast - emaSlow;
            dea = i == 0 ? dif : dif * signalAlpha + dea * (1 - signalAlpha);
            result.Add(new MacdPoint(ordered[i].Time, dif, dea, 2 * (dif - dea)));
        }

        return result;
    }
}
