using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class MarketMonitorSnapshotBuilder
{
    public const string AllFilter = "全部";
    public const string IndexAndFxFilter = "指数/汇率";
    public const string EtfFilter = "场内ETF";
    public const string OtcFilter = "场外基金";

    private static readonly string[] CoreSources =
    {
        MarketSources.Tencent,
        MarketSources.EastMoney,
        MarketSources.SinaFund,
        MarketSources.EastMoneyHistory
    };

    public MarketMonitorSnapshot Build(
        IReadOnlyList<StrategyConfigRecord> strategies,
        IReadOnlyList<PositionStateRecord> positions,
        IReadOnlyList<OtcChannelRecord> otcChannels,
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(otcChannels);
        ArgumentNullException.ThrowIfNull(quotes);
        ArgumentNullException.ThrowIfNull(sourceStatuses);

        Dictionary<string, InstrumentDefinition> instruments = BuildInstrumentDefinitions(
            strategies,
            positions,
            otcChannels,
            quotes);
        Dictionary<string, MarketQuoteRecord[]> quotesByKey = quotes
            .Where(quote => !string.IsNullOrWhiteSpace(BuildMarketKey(quote.Symbol, quote.MarketType)))
            .GroupBy(quote => BuildMarketKey(quote.Symbol, quote.MarketType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        MarketMonitorQuoteRow[] quoteRows = instruments.Values
            .Select(instrument => BuildQuoteRow(
                instrument,
                quotesByKey.TryGetValue(instrument.Key, out MarketQuoteRecord[]? candidates)
                    ? SelectQuote(candidates, instrument.MarketType, now.Offset)
                    : null,
                now))
            .OrderBy(row => CategoryRank(row.MarketType))
            .ThenBy(row => instruments[BuildMarketKey(row.Code, row.MarketType)].FixedOrder)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MarketMonitorSourceRow[] sourceRows = BuildSourceRows(sourceStatuses, now.Offset);
        return new MarketMonitorSnapshot
        {
            QuoteRows = quoteRows,
            SourceRows = sourceRows,
            TotalCount = quoteRows.Length,
            NormalCount = quoteRows.Count(row => row.FreshnessStatus == "正常"),
            DelayedCount = quoteRows.Count(row => row.FreshnessStatus == "延迟"),
            ExpiredCount = quoteRows.Count(row => row.FreshnessStatus == "过期"),
            NoDataCount = quoteRows.Count(row => row.FreshnessStatus == "无数据"),
            InvalidTimeCount = quoteRows.Count(row => row.FreshnessStatus == "时间无效"),
            NormalSourceCount = sourceRows.Count(row => row.IsNormal),
            AbnormalSourceCount = sourceRows.Count(row => !row.IsNormal),
            GeneratedAt = now
        };
    }

    public static IReadOnlyList<MarketMonitorQuoteRow> FilterRows(
        IEnumerable<MarketMonitorQuoteRow> rows,
        string? filter,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(rows);
        string normalizedFilter = string.IsNullOrWhiteSpace(filter) ? AllFilter : filter.Trim();
        string search = searchText?.Trim() ?? string.Empty;

        return rows
            .Where(row => normalizedFilter == AllFilter
                          || string.Equals(row.FilterGroup, normalizedFilter, StringComparison.Ordinal))
            .Where(row => search.Length == 0
                          || Contains(row.Code, search)
                          || Contains(row.Name, search)
                          || Contains(row.StrategyCodes, search)
                          || Contains(row.Source, search)
                          || Contains(row.SourceName, search))
            .ToArray();
    }

    public static string ResolveFreshnessStatus(MarketQuoteRecord? quote, DateTimeOffset now)
    {
        if (quote?.Price is null || !double.IsFinite(quote.Price.Value))
        {
            return "无数据";
        }

        if (!TryParseTimestamp(quote.ReceivedAt, now.Offset, out DateTimeOffset receivedAt))
        {
            return "时间无效";
        }

        TimeSpan age = now - receivedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        (TimeSpan normal, TimeSpan delayed) = FreshnessThresholds(quote.Source);
        if (age <= normal)
        {
            return "正常";
        }

        return age <= delayed ? "延迟" : "过期";
    }

    public static string FormatCacheAge(MarketQuoteRecord? quote, DateTimeOffset now)
    {
        if (quote is null
            || !TryParseTimestamp(quote.ReceivedAt, now.Offset, out DateTimeOffset receivedAt))
        {
            return "--";
        }

        TimeSpan age = now - receivedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        int totalSeconds = (int)Math.Floor(age.TotalSeconds);
        if (totalSeconds < 60)
        {
            return $"{totalSeconds}秒";
        }

        if (totalSeconds < 3600)
        {
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return seconds == 0 ? $"{minutes}分" : $"{minutes}分{seconds}秒";
        }

        int hours = totalSeconds / 3600;
        int remainingMinutes = totalSeconds % 3600 / 60;
        return remainingMinutes == 0 ? $"{hours}小时" : $"{hours}小时{remainingMinutes}分";
    }

    public static string BuildMarketKey(string? symbol, string? marketType)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(marketType))
        {
            return string.Empty;
        }

        string normalizedMarketType = marketType.Trim().ToUpperInvariant();
        string normalizedSymbol = normalizedMarketType is "ETF" or "OTC"
            ? MarketSymbolNormalizer.DigitsOnly(symbol)
            : symbol.Trim().ToUpperInvariant();
        return normalizedSymbol.Length == 0 ? string.Empty : normalizedMarketType + "|" + normalizedSymbol;
    }

    public static string ResolveSourceName(string? source)
        => source?.Trim().ToUpperInvariant() switch
        {
            MarketSources.Tencent => "腾讯场内ETF",
            MarketSources.EastMoney => "东方财富指数/汇率",
            MarketSources.SinaFund => "新浪场外净值",
            MarketSources.EastMoneyHistory => "东方财富历史K线",
            null or "" => "--",
            _ => source!.Trim()
        };

    public static string ResolveSourceStatus(string? status)
        => status?.Trim().ToUpperInvariant() switch
        {
            "OK" => "正常",
            "RATE_LIMIT" => "限频",
            "COOLDOWN" => "熔断冷却",
            "ERROR" => "异常",
            null or "" => "未记录",
            _ => status!.Trim()
        };

    private static Dictionary<string, InstrumentDefinition> BuildInstrumentDefinitions(
        IReadOnlyList<StrategyConfigRecord> strategies,
        IReadOnlyList<PositionStateRecord> positions,
        IReadOnlyList<OtcChannelRecord> otcChannels,
        IReadOnlyList<MarketQuoteRecord> quotes)
    {
        var instruments = new Dictionary<string, InstrumentDefinition>(StringComparer.OrdinalIgnoreCase);
        MarketWatchItem[] topItems = MarketSymbolNormalizer.DefaultTopBarItems().ToArray();
        for (int index = 0; index < topItems.Length; index++)
        {
            MarketWatchItem item = topItems[index];
            AddInstrument(instruments, item.Symbol, item.MarketType, item.DisplayName, null, 0, index);
        }

        StrategyConfigRecord[] enabledStrategies = strategies
            .Where(strategy => strategy.Enabled)
            .OrderBy(strategy => strategy.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(strategy => strategy.Id)
            .ToArray();
        Dictionary<string, string> strategyNames = strategies
            .GroupBy(strategy => NormalizeEtfSymbol(strategy.Code), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(strategy => strategy.Enabled).ThenBy(strategy => strategy.Id).First().Name,
                StringComparer.OrdinalIgnoreCase);

        foreach (StrategyConfigRecord strategy in enabledStrategies)
        {
            AddInstrument(instruments, strategy.Code, "ETF", strategy.Name, strategy.Code, 1);
            if (!string.IsNullOrWhiteSpace(strategy.IndexSecId))
            {
                string indexCode = MarketSymbolNormalizer.NormalizeEastMoneySecId(strategy.IndexSecId, true);
                AddInstrument(instruments, indexCode, "INDEX", strategy.Name + " 跟踪指数", strategy.Code, 1);
            }
        }

        foreach (PositionStateRecord position in positions
                     .Where(position => string.Equals(position.Source, "场内ETF", StringComparison.Ordinal))
                     .OrderBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(position => position.Id))
        {
            string normalizedCode = NormalizeEtfSymbol(position.ActualCode);
            string name = strategyNames.TryGetValue(normalizedCode, out string? strategyName)
                ? strategyName
                : normalizedCode;
            AddInstrument(instruments, position.ActualCode, "ETF", name, position.StrategyCode, 2);
        }

        Dictionary<string, StrategyConfigRecord> strategyByCode = strategies
            .GroupBy(strategy => NormalizeEtfSymbol(strategy.Code), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(strategy => strategy.Enabled).ThenBy(strategy => strategy.Id).First(),
                StringComparer.OrdinalIgnoreCase);
        foreach (OtcChannelRecord channel in otcChannels
                     .Where(channel => channel.Enabled)
                     .OrderBy(channel => channel.StrategyCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(channel => channel.Priority)
                     .ThenBy(channel => channel.OtcCode, StringComparer.OrdinalIgnoreCase))
        {
            string strategyCode = NormalizeEtfSymbol(channel.StrategyCode);
            string strategyName = strategyByCode.TryGetValue(strategyCode, out StrategyConfigRecord? strategy)
                ? strategy.Name
                : channel.StrategyCode;
            AddInstrument(
                instruments,
                channel.OtcCode,
                "OTC",
                $"{strategyName} {channel.ClassType}".Trim(),
                channel.StrategyCode,
                1);
        }

        foreach (MarketQuoteRecord quote in quotes
                     .OrderBy(quote => quote.MarketType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(quote => quote.Source, StringComparer.OrdinalIgnoreCase))
        {
            AddInstrument(
                instruments,
                quote.Symbol,
                quote.MarketType,
                string.IsNullOrWhiteSpace(quote.DisplayName) ? quote.Symbol : quote.DisplayName!,
                null,
                3);
        }

        return instruments;
    }

    private static void AddInstrument(
        IDictionary<string, InstrumentDefinition> instruments,
        string? symbol,
        string? marketType,
        string? name,
        string? strategyCode,
        int namePriority,
        int fixedOrder = int.MaxValue)
    {
        string key = BuildMarketKey(symbol, marketType);
        if (key.Length == 0)
        {
            return;
        }

        string normalizedMarketType = marketType!.Trim().ToUpperInvariant();
        string normalizedSymbol = key[(key.IndexOf('|') + 1)..];
        if (!instruments.TryGetValue(key, out InstrumentDefinition? definition))
        {
            definition = new InstrumentDefinition(
                key,
                normalizedSymbol,
                normalizedMarketType,
                string.IsNullOrWhiteSpace(name) ? normalizedSymbol : name.Trim(),
                namePriority,
                fixedOrder);
            instruments.Add(key, definition);
        }
        else
        {
            definition.FixedOrder = Math.Min(definition.FixedOrder, fixedOrder);
            if (namePriority < definition.NamePriority && !string.IsNullOrWhiteSpace(name))
            {
                definition.Name = name.Trim();
                definition.NamePriority = namePriority;
            }
        }

        if (!string.IsNullOrWhiteSpace(strategyCode))
        {
            definition.StrategyCodes.Add(strategyCode.Trim());
        }
    }

    private static MarketQuoteRecord? SelectQuote(
        IReadOnlyList<MarketQuoteRecord> candidates,
        string marketType,
        TimeSpan nowOffset)
    {
        string preferredSource = PreferredSource(marketType);
        return candidates
            .OrderBy(candidate => string.Equals(candidate.Source, preferredSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(candidate => TryParseTimestamp(candidate.ReceivedAt, nowOffset, out _))
            .ThenByDescending(candidate => ParseTimestampOrMinimum(candidate.ReceivedAt, nowOffset))
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();
    }

    private static MarketMonitorQuoteRow BuildQuoteRow(
        InstrumentDefinition instrument,
        MarketQuoteRecord? quote,
        DateTimeOffset now)
    {
        string source = quote?.Source ?? PreferredSource(instrument.MarketType);
        string name = instrument.Name;
        if ((instrument.MarketType == "OTC"
             || instrument.NamePriority >= 3
             || string.Equals(name, instrument.Symbol, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(quote?.DisplayName))
        {
            name = quote.DisplayName!.Trim();
        }

        return new MarketMonitorQuoteRow
        {
            Category = ResolveCategory(instrument.MarketType),
            FilterGroup = ResolveFilterGroup(instrument.MarketType),
            Name = name,
            Code = instrument.Symbol,
            MarketType = instrument.MarketType,
            StrategyCodes = string.Join(" / ", instrument.StrategyCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase)),
            Source = source,
            SourceName = ResolveSourceName(source),
            Price = quote?.Price,
            ChangeValue = quote?.ChangeValue,
            ChangePercent = quote?.ChangePercent,
            LastClose = quote?.LastClose,
            OpenValue = quote?.OpenValue,
            HighValue = quote?.HighValue,
            LowValue = quote?.LowValue,
            Volume = quote?.Volume,
            Amount = quote?.Amount,
            Iopv = quote?.Iopv,
            PriceText = FormatPrice(quote?.Price, instrument.MarketType),
            ChangeValueText = FormatSignedValue(quote?.ChangeValue, instrument.MarketType),
            ChangePercentText = FormatSignedPercent(quote?.ChangePercent),
            LastCloseText = FormatPrice(quote?.LastClose, instrument.MarketType),
            OpenText = FormatPrice(quote?.OpenValue, instrument.MarketType),
            HighText = FormatPrice(quote?.HighValue, instrument.MarketType),
            LowText = FormatPrice(quote?.LowValue, instrument.MarketType),
            VolumeText = FormatCompactNumber(quote?.Volume),
            VolumeFullText = FormatFullNumber(quote?.Volume),
            AmountText = FormatCompactNumber(quote?.Amount),
            AmountFullText = FormatFullNumber(quote?.Amount),
            IopvText = FormatPrice(quote?.Iopv, "ETF"),
            QuoteTime = DisplayText(quote?.QuoteTime),
            ReceivedAt = DisplayText(quote?.ReceivedAt),
            FreshnessStatus = ResolveFreshnessStatus(quote, now),
            CacheAge = FormatCacheAge(quote, now),
            TrendStatus = ResolveTrend(quote)
        };
    }

    private static MarketMonitorSourceRow[] BuildSourceRows(
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        TimeSpan nowOffset)
    {
        var latestBySource = sourceStatuses
            .Where(status => !string.IsNullOrWhiteSpace(status.Source))
            .GroupBy(status => status.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(status => ParseTimestampOrMinimum(status.UpdatedAt, nowOffset))
                    .ThenByDescending(status => status.Id)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        string[] sources = CoreSources
            .Concat(latestBySource.Keys.Where(source => !CoreSources.Contains(source, StringComparer.OrdinalIgnoreCase))
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return sources.Select(source =>
        {
            latestBySource.TryGetValue(source, out MarketSourceStatusRecord? record);
            string status = ResolveSourceStatus(record?.Status);
            return new MarketMonitorSourceRow
            {
                Source = source,
                SourceName = ResolveSourceName(source),
                RawStatus = record?.Status ?? string.Empty,
                Status = status,
                LastSuccessAt = DisplayText(record?.LastSuccessAt),
                LastFailureAt = DisplayText(record?.LastFailureAt),
                FailureCount = record?.FailureCount,
                FailureCountText = record is null ? "--" : record.FailureCount.ToString(CultureInfo.InvariantCulture),
                CooldownUntil = DisplayText(record?.CooldownUntil),
                LastError = DisplayText(record?.LastError),
                UpdatedAt = DisplayText(record?.UpdatedAt),
                IsNormal = string.Equals(status, "正常", StringComparison.Ordinal)
            };
        }).ToArray();
    }

    private static (TimeSpan Normal, TimeSpan Delayed) FreshnessThresholds(string? source)
        => source?.Trim().ToUpperInvariant() switch
        {
            MarketSources.Tencent => (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)),
            MarketSources.EastMoney => (TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180)),
            MarketSources.SinaFund => (TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(900)),
            _ => (TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300))
        };

    private static string PreferredSource(string marketType)
        => marketType.Trim().ToUpperInvariant() switch
        {
            "ETF" => MarketSources.Tencent,
            "INDEX" or "FX" => MarketSources.EastMoney,
            "OTC" => MarketSources.SinaFund,
            _ => string.Empty
        };

    private static string ResolveCategory(string marketType)
        => marketType.Trim().ToUpperInvariant() switch
        {
            "INDEX" => "指数",
            "FX" => "汇率",
            "ETF" => "场内ETF",
            "OTC" => "场外基金",
            _ => "其它"
        };

    private static string ResolveFilterGroup(string marketType)
        => marketType.Trim().ToUpperInvariant() switch
        {
            "INDEX" or "FX" => IndexAndFxFilter,
            "ETF" => EtfFilter,
            "OTC" => OtcFilter,
            _ => "其它"
        };

    private static int CategoryRank(string marketType)
        => marketType.Trim().ToUpperInvariant() switch
        {
            "INDEX" => 0,
            "FX" => 1,
            "ETF" => 2,
            "OTC" => 3,
            _ => 4
        };

    private static string ResolveTrend(MarketQuoteRecord? quote)
    {
        double? value = quote?.ChangePercent ?? quote?.ChangeValue;
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return "未知";
        }

        if (value.Value > 0)
        {
            return "上涨";
        }

        return value.Value < 0 ? "下跌" : "持平";
    }

    private static string FormatPrice(double? value, string marketType)
    {
        if (!IsFinite(value))
        {
            return "--";
        }

        return string.Equals(marketType, "INDEX", StringComparison.OrdinalIgnoreCase)
            ? value!.Value.ToString("N2", CultureInfo.CurrentCulture)
            : value!.Value.ToString("#,##0.####", CultureInfo.CurrentCulture);
    }

    private static string FormatSignedValue(double? value, string marketType)
    {
        if (!IsFinite(value))
        {
            return "--";
        }

        string formatted = FormatPrice(Math.Abs(value!.Value), marketType);
        return value.Value > 0 ? "+" + formatted : value.Value < 0 ? "-" + formatted : formatted;
    }

    private static string FormatSignedPercent(double? ratio)
    {
        if (!IsFinite(ratio))
        {
            return "--";
        }

        double percent = ratio!.Value * 100.0;
        string sign = percent > 0 ? "+" : string.Empty;
        return sign + percent.ToString("0.00", CultureInfo.CurrentCulture) + "%";
    }

    private static string FormatCompactNumber(double? value)
    {
        if (!IsFinite(value))
        {
            return "--";
        }

        double number = value!.Value;
        double absolute = Math.Abs(number);
        if (absolute >= 100_000_000)
        {
            return (number / 100_000_000).ToString("0.##", CultureInfo.CurrentCulture) + "亿";
        }

        if (absolute >= 10_000)
        {
            return (number / 10_000).ToString("0.##", CultureInfo.CurrentCulture) + "万";
        }

        return number.ToString("#,##0.####", CultureInfo.CurrentCulture);
    }

    private static string FormatFullNumber(double? value)
        => IsFinite(value) ? value!.Value.ToString("#,##0.#################", CultureInfo.CurrentCulture) : "--";

    private static bool IsFinite(double? value)
        => value.HasValue && double.IsFinite(value.Value);

    private static string DisplayText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private static bool Contains(string value, string search)
        => value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseTimestamp(string? value, TimeSpan offset, out DateTimeOffset parsed)
    {
        if (HasExplicitOffset(value)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset withOffset))
        {
            parsed = withOffset;
            return true;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime local))
        {
            parsed = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
            return true;
        }

        parsed = default;
        return false;
    }

    private static bool HasExplicitOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string timestamp = value.Trim();
        if (timestamp.EndsWith('Z'))
        {
            return true;
        }

        int timeSeparator = Math.Max(timestamp.IndexOf('T'), timestamp.IndexOf(' '));
        return timeSeparator >= 0
               && (timestamp.LastIndexOf('+') > timeSeparator || timestamp.LastIndexOf('-') > timeSeparator);
    }

    private static DateTimeOffset ParseTimestampOrMinimum(string? value, TimeSpan offset)
        => TryParseTimestamp(value, offset, out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;

    private static string NormalizeEtfSymbol(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : MarketSymbolNormalizer.DigitsOnly(value);

    private sealed class InstrumentDefinition(
        string key,
        string symbol,
        string marketType,
        string name,
        int namePriority,
        int fixedOrder)
    {
        public string Key { get; } = key;
        public string Symbol { get; } = symbol;
        public string MarketType { get; } = marketType;
        public string Name { get; set; } = name;
        public int NamePriority { get; set; } = namePriority;
        public int FixedOrder { get; set; } = fixedOrder;
        public HashSet<string> StrategyCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
