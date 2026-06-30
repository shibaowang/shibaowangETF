using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public enum IntradayVolumeFieldKind
{
    Minute,
    Cumulative
}

public static class IntradayVolumeNormalizer
{
    // LOCKED: Volume bars must use real source volume only; never synthesize index volume from quote or price movement.
    public static IReadOnlyList<IntradayPoint> Normalize(
        IReadOnlyList<IntradayPoint> points,
        IntradayVolumeFieldKind fieldKind)
    {
        IntradayPoint[] ordered = points
            .OrderBy(point => point.Time)
            .Select(Clone)
            .ToArray();

        if (fieldKind == IntradayVolumeFieldKind.Minute)
        {
            return ordered;
        }

        double? previousVolumeCumulative = null;
        double? previousAmountCumulative = null;
        foreach (IntradayPoint point in ordered)
        {
            if (point.Volume.HasValue)
            {
                double currentVolumeCumulative = Math.Max(0, point.Volume.Value);
                double minuteVolume = previousVolumeCumulative.HasValue
                    ? currentVolumeCumulative - previousVolumeCumulative.Value
                    : currentVolumeCumulative;
                if (minuteVolume < 0)
                {
                    // Cross-day or abnormal cumulative reset: keep a safe real value, never synthesize a bar.
                    minuteVolume = currentVolumeCumulative;
                }

                point.Volume = minuteVolume;
                previousVolumeCumulative = currentVolumeCumulative;
            }

            if (point.Amount.HasValue)
            {
                double currentAmountCumulative = Math.Max(0, point.Amount.Value);
                double minuteAmount = previousAmountCumulative.HasValue
                    ? currentAmountCumulative - previousAmountCumulative.Value
                    : currentAmountCumulative;
                if (minuteAmount < 0)
                {
                    minuteAmount = currentAmountCumulative;
                }

                point.Amount = minuteAmount;
                previousAmountCumulative = currentAmountCumulative;
            }
        }

        return ordered;
    }

    public static double MaxVisibleMinuteVolume(IEnumerable<IntradayPoint> points)
        => points
            .Where(point => point.Volume.HasValue)
            .Select(point => Math.Max(0, point.Volume!.Value))
            .DefaultIfEmpty(0)
            .Max();

    public static double ScaleBarHeight(double minuteVolume, double maxVisibleMinuteVolume, double chartHeight)
    {
        if (minuteVolume <= 0 || maxVisibleMinuteVolume <= 0 || chartHeight <= 0)
        {
            return 0;
        }

        return Math.Max(0, minuteVolume) / maxVisibleMinuteVolume * chartHeight;
    }

    private static IntradayPoint Clone(IntradayPoint point)
        => new()
        {
            Time = point.Time,
            Price = point.Price,
            AveragePrice = point.AveragePrice,
            Volume = point.Volume,
            Amount = point.Amount,
            IsQuoteTail = point.IsQuoteTail,
            IsQuoteCloseDisplayPoint = point.IsQuoteCloseDisplayPoint,
            PointSource = point.PointSource
        };
}
