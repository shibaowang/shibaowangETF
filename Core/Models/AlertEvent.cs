using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record AlertEvent
{
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string AlertType { get; init; } = string.Empty;
    public string Severity { get; init; } = AlertSeverity.Normal;
    public string? StrategyCode { get; init; }
    public string? ActualCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string DedupeKey { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? Action { get; init; }
    public string? Reason { get; init; }
    public double? Price { get; init; }
    public double? PremiumRate { get; init; }
    public string ContentHash { get; init; } = string.Empty;

    public AlertEvent WithStableHash()
        => this with
        {
            ContentHash = ComputeContentHash(AlertType, StrategyCode, ActualCode, Action, Reason, Severity, Source)
        };

    public static string BuildDedupeKey(string alertType, string? strategyCode, string? action, string? reason, string? source = null)
    {
        string subject = string.IsNullOrWhiteSpace(strategyCode) ? source ?? "--" : strategyCode.Trim();
        return string.Join("|", new[]
        {
            NormalizePart(alertType),
            NormalizePart(subject),
            NormalizePart(action),
            NormalizePart(reason)
        });
    }

    private static string ComputeContentHash(params string?[] parts)
    {
        string raw = string.Join("|", parts.Select(NormalizePart));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLower(CultureInfo.InvariantCulture);
    }

    private static string NormalizePart(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
}

public static class AlertSeverity
{
    public const string Normal = "普通";
    public const string Severe = "严重";
    public const string Market = "行情";
}
