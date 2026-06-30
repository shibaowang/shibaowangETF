using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class PercentValueParser
{
    public static double ParsePercentInput(string? input)
    {
        if (TryParsePercentInput(input, out double? value, out string? error))
        {
            return value ?? 0;
        }

        throw new FormatException(error);
    }

    public static bool TryParsePercentInput(string? input, out double? value, out string? error)
    {
        value = null;
        error = null;
        string text = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        bool hasPercentSuffix = text.EndsWith("%", StringComparison.Ordinal)
                                || text.EndsWith("％", StringComparison.Ordinal);
        if (hasPercentSuffix)
        {
            text = text[..^1].Trim();
        }

        if (!TryParseDouble(text, out double parsed))
        {
            error = $"百分比格式无效：{input}";
            return false;
        }

        bool hasDecimalSeparator = HasDecimalSeparator(text);
        value = hasPercentSuffix || Math.Abs(parsed) > 1 || !hasDecimalSeparator
            ? parsed / 100.0
            : parsed;
        return true;
    }

    public static double? NormalizeStoredPercent(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        double raw = value.Value;
        return Math.Abs(raw) > 1 ? raw / 100.0 : raw;
    }

    public static string FormatPercent(double? value)
    {
        double? normalized = NormalizeStoredPercent(value);
        return normalized.HasValue
            ? (normalized.Value * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%"
            : string.Empty;
    }

    private static bool TryParseDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
           || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static bool HasDecimalSeparator(string text)
    {
        string currentSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return text.Contains(".", StringComparison.Ordinal)
               || text.Contains(",", StringComparison.Ordinal)
               || (!string.IsNullOrEmpty(currentSeparator)
                   && text.Contains(currentSeparator, StringComparison.Ordinal));
    }
}
