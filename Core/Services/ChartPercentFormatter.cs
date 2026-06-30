using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartPercentFormatter
{
    public static string FormatRatio(double? ratio)
    {
        double? normalized = NormalizeRatioForDisplay(ratio);
        if (!normalized.HasValue)
        {
            return "--";
        }

        double percent = normalized.Value * 100.0;
        string prefix = percent > 0 ? "+" : string.Empty;
        return prefix + percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    public static double? NormalizeRatioForDisplay(double? ratio)
    {
        if (ratio is not double value || double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        double percent = Math.Round(value * 100.0, 2, MidpointRounding.AwayFromZero);
        if (percent == 0)
        {
            return 0;
        }

        return percent / 100.0;
    }
}
