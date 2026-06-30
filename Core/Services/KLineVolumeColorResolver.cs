namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public enum KLineVolumeColorKind
{
    Up,
    Down
}

public static class KLineVolumeColorResolver
{
    public static KLineVolumeColorKind Resolve(double open, double close)
        => close >= open ? KLineVolumeColorKind.Up : KLineVolumeColorKind.Down;
}
