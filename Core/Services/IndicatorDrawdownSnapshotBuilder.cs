using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class IndicatorDrawdownSnapshotBuilder
{
    public const string AllFilter = "全部";
    public const string IndexFilter = "指数";
    public const string EtfFilter = "场内 ETF";

    private static readonly (string Code, string Name)[] FixedIndexes =
    {
        ("251.NDXTMC", "纳斯达克科技指数"),
        ("100.NDX100", "纳斯达克100指数")
    };

    private readonly IndicatorDrawdownMetricsBuilder _metricsBuilder = new();

    public static IReadOnlyList<IndicatorDrawdownInstrument> BuildInstruments(
        IEnumerable<StrategyConfigRecord> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var instruments = new List<IndicatorDrawdownInstrument>();
        instruments.AddRange(FixedIndexes.Select((item, index) => new IndicatorDrawdownInstrument(
            $"INDEX|{item.Code}",
            IndexFilter,
            "INDEX",
            item.Code,
            item.Name,
            string.Empty,
            MarketSources.EastMoneyHistory,
            MarketSources.EastMoney,
            index)));

        foreach (IGrouping<string, StrategyConfigRecord> group in strategies
                     .Where(strategy => strategy.Enabled)
                     .Select(strategy => new
                     {
                         Strategy = strategy,
                         Code = MarketSymbolNormalizer.DigitsOnly(strategy.Code)
                     })
                     .Where(item => item.Code.Length == 6)
                     .GroupBy(item => item.Code, item => item.Strategy, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            StrategyConfigRecord[] ordered = group
                .OrderBy(strategy => strategy.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(strategy => strategy.Id)
                .ToArray();
            string[] strategyCodes = ordered
                .Select(strategy => strategy.Code.Trim())
                .Where(code => code.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string name = ordered.Select(strategy => strategy.Name?.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "--";
            instruments.Add(new IndicatorDrawdownInstrument(
                $"ETF|{group.Key}",
                EtfFilter,
                "ETF",
                group.Key,
                name,
                string.Join(", ", strategyCodes),
                MarketSources.TencentHistory,
                MarketSources.Tencent,
                instruments.Count));
        }

        return instruments;
    }

    public IndicatorDrawdownSnapshot Build(
        IndicatorDrawdownReadModel model,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);
        IndicatorDrawdownRow[] rows = model.Instruments
            .Select(instrument => _metricsBuilder.Build(
                instrument,
                model.HistoryCandidates,
                model.Quotes,
                model.SourceStatuses,
                now))
            .OrderBy(row => row.CurrentDrawdown.HasValue ? 0 : 1)
            .ThenBy(row => row.CurrentDrawdown ?? double.MaxValue)
            .ThenBy(row => row.MaximumDrawdown ?? double.MaxValue)
            .ThenBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return CreateSnapshot(rows, model.ReadAt == default ? now : model.ReadAt, now);
    }

    public IndicatorDrawdownSnapshot ReplaceRows(
        IndicatorDrawdownSnapshot current,
        IEnumerable<IndicatorDrawdownRow> replacements,
        DateTimeOffset generatedAt,
        DateTimeOffset historyCheckedAt,
        IReadOnlySet<string>? validKeys = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        Dictionary<string, IndicatorDrawdownRow> replacementByKey = replacements
            .ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);
        IndicatorDrawdownRow[] rows = current.Rows
            .Where(row => !replacementByKey.ContainsKey(row.Key)
                          && (validKeys is null || validKeys.Contains(row.Key)))
            .Concat(replacementByKey.Values)
            .OrderBy(row => row.CurrentDrawdown.HasValue ? 0 : 1)
            .ThenBy(row => row.CurrentDrawdown ?? double.MaxValue)
            .ThenBy(row => row.MaximumDrawdown ?? double.MaxValue)
            .ThenBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return CreateSnapshot(rows, generatedAt, historyCheckedAt);
    }

    public IndicatorDrawdownSnapshot RefreshRealtime(
        IndicatorDrawdownSnapshot current,
        IndicatorDrawdownRealtimeReadModel realtime,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(realtime);
        Dictionary<string, IndicatorDrawdownInstrument> instrumentByKey = realtime.Instruments
            .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        IndicatorDrawdownRow[] rows = current.Rows
            .Where(row => instrumentByKey.ContainsKey(row.Key))
            .Select(row => _metricsBuilder.RefreshRealtime(
                row,
                instrumentByKey[row.Key],
                realtime.Quotes,
                realtime.SourceStatuses,
                now))
            .ToArray();
        return CreateSnapshot(rows, realtime.ReadAt == default ? now : realtime.ReadAt, current.HistoryCheckedAt);
    }

    public static IReadOnlyList<IndicatorDrawdownRow> FilterRows(
        IEnumerable<IndicatorDrawdownRow> rows,
        string? filter,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(rows);
        string normalizedFilter = string.IsNullOrWhiteSpace(filter) ? AllFilter : filter.Trim();
        string search = searchText?.Trim() ?? string.Empty;
        return rows
            .Where(row => normalizedFilter == AllFilter
                          || string.Equals(row.Category, normalizedFilter, StringComparison.Ordinal))
            .Where(row => search.Length == 0
                          || Contains(row.Code, search)
                          || Contains(row.Name, search)
                          || Contains(row.StrategyCodes, search)
                          || Contains(row.HistorySource, search)
                          || Contains(row.QuoteSource, search)
                          || Contains(row.DataStatus, search))
            .ToArray();
    }

    private static IndicatorDrawdownSnapshot CreateSnapshot(
        IReadOnlyList<IndicatorDrawdownRow> rows,
        DateTimeOffset generatedAt,
        DateTimeOffset historyCheckedAt)
    {
        IndicatorDrawdownRow? deepestCurrent = rows
            .Where(row => row.CurrentDrawdown.HasValue)
            .OrderBy(row => row.CurrentDrawdown)
            .FirstOrDefault();
        IndicatorDrawdownRow? deepestMaximum = rows
            .Where(row => row.MaximumDrawdown.HasValue)
            .OrderBy(row => row.MaximumDrawdown)
            .FirstOrDefault();
        int normalCount = rows.Count(row => row.DataStatus == "正常");
        int insufficientCount = rows.Count(row => row.DataStatus == "数据不足");
        int staleCount = rows.Count(row => row.DataStatus == "历史滞后");
        int corruptCount = rows.Count(row => row.DataStatus == "数据损坏");
        int noHistoryCount = rows.Count(row => row.DataStatus == "无历史");
        int sourceErrorCount = rows.Count(row => row.DataStatus == "数据源异常");
        int cooldownCount = rows.Count(row => row.DataStatus == "熔断冷却");
        int rateLimitCount = rows.Count(row => row.DataStatus == "限频");
        int missingRealtimeCount = rows.Count(row => row.DataStatus == "实时行情缺失");
        int abnormalOrMissingCount = corruptCount
                                     + noHistoryCount
                                     + sourceErrorCount
                                     + cooldownCount
                                     + rateLimitCount
                                     + missingRealtimeCount;
        return new IndicatorDrawdownSnapshot
        {
            Rows = rows,
            FilteredRows = rows,
            TotalCount = rows.Count,
            NormalCount = normalCount,
            InsufficientCount = insufficientCount,
            StaleCount = staleCount,
            MissingOrCorruptCount = noHistoryCount + corruptCount,
            CorruptCount = corruptCount,
            NoHistoryCount = noHistoryCount,
            SourceErrorCount = sourceErrorCount,
            CooldownCount = cooldownCount,
            RateLimitCount = rateLimitCount,
            MissingRealtimeCount = missingRealtimeCount,
            AbnormalOrMissingCount = abnormalOrMissingCount,
            AbnormalOrMissingToolTip = $"无历史 {noHistoryCount}｜损坏 {corruptCount}｜源异常 {sourceErrorCount}｜熔断 {cooldownCount}｜限频 {rateLimitCount}｜实时缺失 {missingRealtimeCount}",
            DeepestCurrentCode = deepestCurrent?.Code ?? string.Empty,
            DeepestCurrentDrawdown = deepestCurrent?.CurrentDrawdown,
            DeepestMaximumCode = deepestMaximum?.Code ?? string.Empty,
            DeepestMaximumDrawdown = deepestMaximum?.MaximumDrawdown,
            DeepestCurrentText = deepestCurrent is null ? "--" : $"{deepestCurrent.Code} {deepestCurrent.CurrentDrawdownText}",
            DeepestMaximumText = deepestMaximum is null ? "--" : $"{deepestMaximum.Code} {deepestMaximum.MaximumDrawdownText}",
            GeneratedAt = generatedAt,
            HistoryCheckedAt = historyCheckedAt,
            HistorySignatures = rows.ToDictionary(row => row.Key, row => row.HistorySignature, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool Contains(string? value, string search)
        => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}
