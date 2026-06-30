using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class AlertDedupService
{
    public bool ShouldDeliver(
        AlertEvent alert,
        AlertDeliveryStateRecord? state,
        AlertSettings settings,
        DateTimeOffset now,
        bool bypassLimit = false)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(settings);

        if (bypassLimit || state is null || string.IsNullOrWhiteSpace(state.LastSentAt))
        {
            return true;
        }

        if (!IsMarketAlert(alert)
            && !string.Equals(state.LastContentHash, alert.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParseSentAt(state.LastSentAt, now.Offset, out DateTimeOffset lastSentAt))
        {
            return true;
        }

        return now - lastSentAt >= settings.GetIntervalFor(alert);
    }

    private static bool IsMarketAlert(AlertEvent alert)
        => string.Equals(alert.AlertType, AlertTypes.MarketRuntime, StringComparison.OrdinalIgnoreCase)
           || string.Equals(alert.Severity, AlertSeverity.Market, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseSentAt(string value, TimeSpan offset, out DateTimeOffset sentAt)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime localTime))
        {
            sentAt = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), offset);
            return true;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out sentAt);
    }
}
