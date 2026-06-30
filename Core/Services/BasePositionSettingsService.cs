using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class BasePositionSettingsService
{
    public static BasePositionSettings Normalize(BasePositionSettings? settings)
    {
        BasePositionSettings source = settings ?? BasePositionSettings.Default();
        string mode = string.Equals(source.Mode, BasePositionSettings.AmountMode, StringComparison.OrdinalIgnoreCase)
            ? BasePositionSettings.AmountMode
            : BasePositionSettings.RatioMode;
        return new BasePositionSettings
        {
            Mode = mode,
            Ratio = Math.Clamp(NormalizeFinite(source.Ratio, BasePositionSettings.DefaultRatio), 0, 1),
            FixedAmount = Math.Max(0, NormalizeFinite(source.FixedAmount, 0))
        };
    }

    public static BasePositionSettings CreateRatio(double ratio)
        => Normalize(new BasePositionSettings
        {
            Mode = BasePositionSettings.RatioMode,
            Ratio = ratio,
            FixedAmount = 0
        });

    public static BasePositionSettings CreateAmount(double amount, double fallbackRatio = BasePositionSettings.DefaultRatio)
        => Normalize(new BasePositionSettings
        {
            Mode = BasePositionSettings.AmountMode,
            Ratio = fallbackRatio,
            FixedAmount = amount
        });

    public static BasePositionTargetResult ResolveBaseTarget(double principal, BasePositionSettings? settings)
    {
        BasePositionSettings normalized = Normalize(settings);
        double safePrincipal = Math.Max(0, NormalizeFinite(principal, 0));
        double rawTarget = normalized.Mode == BasePositionSettings.AmountMode
            ? normalized.FixedAmount
            : safePrincipal * normalized.Ratio;
        double nonNegativeTarget = Math.Max(0, rawTarget);
        double target = Math.Min(nonNegativeTarget, safePrincipal);
        return new BasePositionTargetResult(
            target,
            nonNegativeTarget > safePrincipal,
            normalized.Mode,
            normalized.Ratio,
            normalized.FixedAmount);
    }

    public static double CalculateCompletionRate(double currentCost, double baseTarget)
        => baseTarget > 0 ? Math.Max(0, currentCost) / baseTarget : 0;

    public static string FormatDisplay(BasePositionSettings? settings)
    {
        BasePositionSettings normalized = Normalize(settings);
        return normalized.Mode == BasePositionSettings.AmountMode
            ? $"固定 {normalized.FixedAmount:#,0.##}"
            : $"本金 {PercentValueParser.FormatPercent(normalized.Ratio)}";
    }

    public static bool TryParseRatio(string? text, out double ratio, out string? error)
    {
        if (!PercentValueParser.TryParsePercentInput(text, out double? parsed, out error))
        {
            ratio = BasePositionSettings.DefaultRatio;
            return false;
        }

        ratio = Math.Clamp(parsed ?? BasePositionSettings.DefaultRatio, 0, 1);
        return true;
    }

    private static double NormalizeFinite(double value, double fallback)
        => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
}
