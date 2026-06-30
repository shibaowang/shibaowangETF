using System.Windows;
using System.Windows.Media.Animation;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public static class WindowInteractionEffects
{
    public static void ApplySmoothOpen(Window window)
    {
        window.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            window.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
    }
}
