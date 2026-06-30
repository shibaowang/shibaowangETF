using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record AlertSettings
{
    public const string PushPlusEnabledKey = "alert_pushplus_enabled";
    public const string PushPlusTokenKey = "alert_pushplus_token";
    public const string VoiceEnabledKey = "alert_voice_enabled";
    public const string RepeatIntervalMinutesKey = "alert_repeat_interval_minutes";
    public const string SevereIntervalMinutesKey = "alert_severe_interval_minutes";
    public const string MarketIntervalMinutesKey = "alert_market_interval_minutes";
    public const string RuntimeLogAlertCursorKey = "alert_runtime_log_last_processed_id";

    public bool PushPlusEnabled { get; init; }
    public string PushPlusToken { get; init; } = string.Empty;
    public bool VoiceEnabled { get; init; }
    public int RepeatIntervalMinutes { get; init; } = 30;
    public int SevereIntervalMinutes { get; init; } = 5;
    public int MarketIntervalMinutes { get; init; } = 10;

    public static AlertSettings Default => new();

    public TimeSpan GetIntervalFor(AlertEvent alert)
    {
        if (string.Equals(alert.Severity, AlertSeverity.Market, StringComparison.OrdinalIgnoreCase)
            || string.Equals(alert.AlertType, AlertTypes.MarketRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(Math.Max(1, MarketIntervalMinutes));
        }

        if (string.Equals(alert.Severity, AlertSeverity.Severe, StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(Math.Max(1, SevereIntervalMinutes));
        }

        return TimeSpan.FromMinutes(Math.Max(1, RepeatIntervalMinutes));
    }

    public static AlertSettings FromStoredValues(IReadOnlyDictionary<string, string?> values)
        => Normalize(new AlertSettings
        {
            PushPlusEnabled = ParseBool(values.GetValueOrDefault(PushPlusEnabledKey)),
            PushPlusToken = values.GetValueOrDefault(PushPlusTokenKey) ?? string.Empty,
            VoiceEnabled = ParseBool(values.GetValueOrDefault(VoiceEnabledKey)),
            RepeatIntervalMinutes = ParseInt(values.GetValueOrDefault(RepeatIntervalMinutesKey), 30),
            SevereIntervalMinutes = ParseInt(values.GetValueOrDefault(SevereIntervalMinutesKey), 5),
            MarketIntervalMinutes = ParseInt(values.GetValueOrDefault(MarketIntervalMinutesKey), 10)
        });

    public static AlertSettings Normalize(AlertSettings settings)
        => settings with
        {
            PushPlusToken = settings.PushPlusToken.Trim(),
            RepeatIntervalMinutes = ClampMinutes(settings.RepeatIntervalMinutes, 30),
            SevereIntervalMinutes = ClampMinutes(settings.SevereIntervalMinutes, 5),
            MarketIntervalMinutes = ClampMinutes(settings.MarketIntervalMinutes, 10)
        };

    private static bool ParseBool(string? value)
        => bool.TryParse(value, out bool parsed) && parsed;

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    private static int ClampMinutes(int value, int fallback)
        => value is >= 1 and <= 1440 ? value : fallback;
}

public static class AlertTypes
{
    public const string StrategyDecision = "策略决策";
    public const string OrderNotExecutable = "委托不可执行";
    public const string MarketRuntime = "行情异常";
    public const string AccountReplay = "账户回放异常";
    public const string Test = "测试预警";
}
