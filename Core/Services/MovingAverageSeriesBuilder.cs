using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class MovingAverageSeriesBuilder
{
    public static readonly IReadOnlyList<int> DefaultPeriods = new[] { 5, 10, 20, 60 };

    public static IReadOnlyDictionary<int, MovingAverageSeries> BuildDefault(IReadOnlyList<KLinePoint> kLines)
        => DefaultPeriods.ToDictionary(period => period, period => Build(kLines, period));

    public static MovingAverageSeries Build(IReadOnlyList<KLinePoint> kLines, int period)
    {
        ArgumentNullException.ThrowIfNull(kLines);
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        var values = new double?[kLines.Count];
        double sum = 0;
        int invalidCount = 0;
        for (int index = 0; index < kLines.Count; index++)
        {
            double close = kLines[index].Close;
            if (IsValidClose(close))
            {
                sum += close;
            }
            else
            {
                invalidCount++;
            }

            if (index >= period)
            {
                double expired = kLines[index - period].Close;
                if (IsValidClose(expired))
                {
                    sum -= expired;
                }
                else
                {
                    invalidCount--;
                }
            }

            if (index >= period - 1 && invalidCount == 0)
            {
                values[index] = sum / period;
            }
        }

        return new MovingAverageSeries(period, values);
    }

    private static bool IsValidClose(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
