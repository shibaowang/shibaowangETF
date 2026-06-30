using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class KLineVolumeMetrics
{
    public static double MaxVisibleVolume(IEnumerable<KLinePoint> points)
        => points
            .Where(point => point.Volume.HasValue)
            .Select(point => Math.Max(0, point.Volume!.Value))
            .DefaultIfEmpty(0)
            .Max();

    public static double ScaleBarHeight(double? volume, double maxVisibleVolume, double chartHeight)
    {
        if (!volume.HasValue || volume.Value <= 0 || maxVisibleVolume <= 0 || chartHeight <= 0)
        {
            return 0;
        }

        return Math.Max(0, volume.Value) / maxVisibleVolume * chartHeight;
    }
}
