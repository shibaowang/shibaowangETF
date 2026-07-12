using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ChartTradeMarkerBuilder
{
    public static IReadOnlyList<ChartTradeMarker> Build(
        ChartSecurityInfo security,
        SecurityChartPeriod period,
        IReadOnlyList<KLinePoint> kLines,
        IEnumerable<TradeLogRecord> tradeLogs,
        IEnumerable<StrategyConfigRecord> strategies)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(kLines);
        ArgumentNullException.ThrowIfNull(tradeLogs);
        ArgumentNullException.ThrowIfNull(strategies);
        if (period == SecurityChartPeriod.Intraday || kLines.Count == 0)
        {
            return Array.Empty<ChartTradeMarker>();
        }

        Dictionary<DateTime, int> kLineIndexes = kLines
            .Select((point, index) => new { Key = ResolvePeriodKey(period, point.Date), Index = index })
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Last().Index);

        HashSet<string> indexStrategyCodes = security.InstrumentType == ChartInstrumentType.Index
            ? ResolveIndexStrategyCodes(security, strategies)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return tradeLogs
            .Where(record => TryResolveMarkerType(record.Action, out _))
            .Where(record => security.InstrumentType == ChartInstrumentType.Index
                ? indexStrategyCodes.Contains(Normalize(record.StrategyCode))
                : MatchesEtfTrade(security, record))
            .Select(record => new
            {
                Record = record,
                TradeDate = TryParseLocalTime(record.Time, out DateTime parsed) ? parsed.Date : (DateTime?)null,
                MarkerType = ResolveMarkerType(record.Action)
            })
            .Where(item => item.TradeDate.HasValue)
            .Select(item => new
            {
                item.TradeDate,
                item.MarkerType,
                PeriodKey = ResolvePeriodKey(period, item.TradeDate!.Value)
            })
            .Where(item => kLineIndexes.ContainsKey(item.PeriodKey))
            .GroupBy(item => new { item.PeriodKey, item.MarkerType })
            .Select(group => new ChartTradeMarker(
                group.Key.PeriodKey,
                group.Key.MarkerType,
                group.Min(item => item.TradeDate!.Value),
                kLineIndexes[group.Key.PeriodKey]))
            .OrderBy(marker => marker.KLineIndex)
            .ThenBy(marker => marker.MarkerType)
            .ToArray();
    }

    public static DateTime ResolvePeriodKey(SecurityChartPeriod period, DateTime date)
        => period switch
        {
            SecurityChartPeriod.Weekly => KLineAggregator.ResolveWeekStart(date),
            SecurityChartPeriod.Monthly => new DateTime(date.Year, date.Month, 1),
            _ => date.Date
        };

    private static bool MatchesEtfTrade(ChartSecurityInfo security, TradeLogRecord record)
    {
        if (string.Equals(Normalize(record.Source), "场外替代", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(record.ActualCode))
        {
            return string.Equals(
                MarketSymbolNormalizer.DigitsOnly(record.ActualCode),
                MarketSymbolNormalizer.DigitsOnly(security.ActualCode),
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            Normalize(record.StrategyCode),
            Normalize(security.StrategyCode),
            StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveIndexStrategyCodes(
        ChartSecurityInfo security,
        IEnumerable<StrategyConfigRecord> strategies)
    {
        string targetSecId = MarketSymbolNormalizer.NormalizeEastMoneySecId(
            security.EastMoneySecId,
            preferIndex: true);
        return strategies
            .Where(strategy => strategy.Enabled && !string.IsNullOrWhiteSpace(strategy.IndexSecId))
            .Where(strategy => string.Equals(
                MarketSymbolNormalizer.NormalizeEastMoneySecId(strategy.IndexSecId!, preferIndex: true),
                targetSecId,
                StringComparison.OrdinalIgnoreCase))
            .Select(strategy => Normalize(strategy.Code))
            .Where(code => code.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ChartTradeMarkerType ResolveMarkerType(string? action)
        => string.Equals(Normalize(action), "买入", StringComparison.Ordinal)
            ? ChartTradeMarkerType.B
            : ChartTradeMarkerType.S;

    private static bool TryResolveMarkerType(string? action, out ChartTradeMarkerType markerType)
    {
        string normalized = Normalize(action);
        if (string.Equals(normalized, "买入", StringComparison.Ordinal))
        {
            markerType = ChartTradeMarkerType.B;
            return true;
        }

        if (string.Equals(normalized, "卖出", StringComparison.Ordinal))
        {
            markerType = ChartTradeMarkerType.S;
            return true;
        }

        markerType = default;
        return false;
    }

    private static bool TryParseLocalTime(string? value, out DateTime parsed)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
           || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
