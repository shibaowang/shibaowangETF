using System.Globalization;
using System.Windows.Data;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public sealed class PercentInputConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double number
            ? PercentValueParser.FormatPercent(number)
            : string.Empty;

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value?.ToString();
        if (PercentValueParser.TryParsePercentInput(text, out double? parsed, out string? error))
        {
            return parsed;
        }

        throw new FormatException(error);
    }
}
