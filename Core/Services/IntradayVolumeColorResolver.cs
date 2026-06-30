namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public enum IntradayVolumeColorKind
{
    Neutral,
    Up,
    Down
}

public static class IntradayVolumeColorResolver
{
    private const double Epsilon = 0.0000001;

    public static IntradayVolumeColorKind Resolve(
        double currentPrice,
        double? previousPrice,
        IntradayVolumeColorKind? previousColor)
    {
        if (!previousPrice.HasValue || previousPrice.Value <= 0)
        {
            return IntradayVolumeColorKind.Neutral;
        }

        double diff = currentPrice - previousPrice.Value;
        if (diff > Epsilon)
        {
            return IntradayVolumeColorKind.Up;
        }

        if (diff < -Epsilon)
        {
            return IntradayVolumeColorKind.Down;
        }

        return previousColor ?? IntradayVolumeColorKind.Neutral;
    }
}
