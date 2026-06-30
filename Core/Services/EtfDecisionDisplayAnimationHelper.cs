using System.Globalization;
using System.Text;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public enum EtfValueChangeDirection
{
    None,
    Up,
    Down
}

public enum EtfDecisionTextAlignment
{
    Left,
    Center
}

public static class EtfDecisionDisplayAnimationHelper
{
    private const double Epsilon = 0.000001;
    public const string OperationWarningForeground = "#FFFFFF";
    public const string OperationWarningBackground = "#B91C1C";
    public const string EtfDefaultDataForeground = "#E5EEF8";
    public const string OperationNormalForeground = EtfDefaultDataForeground;
    public const string OperationNormalBackground = "Transparent";
    public const string EtfNameHoldingForeground = "#F59E0B";
    public const string EtfNameEmptyForeground = "#E5EEF8";

    private static readonly string[] WarningInstructionKeywords =
    [
        "极端溢价",
        "全清换现金",
        "溢价达标减仓",
        "止盈减仓",
        "禁止建仓",
        "不可执行",
        "行情异常",
        "账户异常",
        "TradeLog异常",
        "底仓保护",
        "战略底仓"
    ];

    public static EtfValueChangeDirection DetectValueChange(string? oldValue, string? newValue)
    {
        if (!TryParseDisplayNumber(oldValue, out double oldNumber)
            || !TryParseDisplayNumber(newValue, out double newNumber)
            || Math.Abs(newNumber - oldNumber) < Epsilon)
        {
            return EtfValueChangeDirection.None;
        }

        return newNumber > oldNumber ? EtfValueChangeDirection.Up : EtfValueChangeDirection.Down;
    }

    public static FinancialValueTone GetSignedNumberTone(object? value)
    {
        return TryParseDisplayNumber(value, out double number)
            ? number > Epsilon ? FinancialValueTone.Positive
                : number < -Epsilon ? FinancialValueTone.Negative
                : FinancialValueTone.Neutral
            : FinancialValueTone.Neutral;
    }

    public static bool IsWarningInstruction(string? instruction)
    {
        string normalized = instruction?.Trim() ?? string.Empty;
        return normalized.Length > 0
               && WarningInstructionKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetOperationInstructionForeground(string? instruction)
        => IsWarningInstruction(instruction) ? OperationWarningForeground : OperationNormalForeground;

    public static string GetOperationInstructionBackground(string? instruction)
        => IsWarningInstruction(instruction) ? OperationWarningBackground : OperationNormalBackground;

    public static bool HasEtfPosition(double totalQuantity, double totalCostAmount)
        => totalQuantity > Epsilon || totalCostAmount > Epsilon;

    public static string GetEtfNameForeground(bool hasPosition)
        => hasPosition ? EtfNameHoldingForeground : EtfNameEmptyForeground;

    public static bool UsesDefaultWhiteForeground(int sourceColumn)
        => sourceColumn is 14 or 15;

    public static EtfDecisionTextAlignment GetEtfHeaderTextAlignment(int sourceColumn)
        => EtfDecisionTextAlignment.Center;

    public static EtfDecisionTextAlignment GetEtfDataTextAlignment(int sourceColumn)
        => sourceColumn == 1 ? EtfDecisionTextAlignment.Left : EtfDecisionTextAlignment.Center;

    public static string? BuildLongTextToolTip(string? value, int minLength = 8)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < minLength || trimmed == "--")
        {
            return null;
        }

        return trimmed;
    }

    public static string StripPinnedMarker(string? displayCode)
    {
        string value = displayCode?.Trim() ?? string.Empty;
        while (value.Length > 0 && (value[0] == '\u2605' || value[0] == '\u25B2' || value[0] == '*'))
        {
            value = value[1..].TrimStart();
        }

        return value;
    }

    public static string? RetainSelectedCode(IEnumerable<string> displayedCodes, string? previousSelectedCode)
    {
        string selected = StripPinnedMarker(previousSelectedCode);
        if (selected.Length == 0)
        {
            return null;
        }

        return displayedCodes.Any(code => string.Equals(StripPinnedMarker(code), selected, StringComparison.OrdinalIgnoreCase))
            ? selected
            : null;
    }

    private static bool TryParseDisplayNumber(object? value, out double number)
    {
        number = 0;
        if (value is null)
        {
            return false;
        }

        if (value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }
            catch (OverflowException)
            {
                number = 0;
                return false;
            }
        }

        string normalized = NormalizeNumberText(value.ToString());
        return normalized.Length > 0
               && double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string NormalizeNumberText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char ch in value.Trim())
        {
            if (char.IsDigit(ch) || ch is '-' or '+' or '.')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
