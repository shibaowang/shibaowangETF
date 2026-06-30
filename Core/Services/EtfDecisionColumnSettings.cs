namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class EtfDecisionColumnSettings
{
    public const string SettingKey = "etf_decision_visible_columns";
    public const string PinnedSymbolsSettingKey = "etf_decision_pinned_symbols";

    public static readonly IReadOnlyList<EtfDecisionColumnDefinition> AllColumns =
    [
        new(0, "code", "代码", true, 70, 74),
        new(1, "name", "名称", true, 110, 118),
        new(2, "action_instruction", "操作指令", true, 92, 100),
        new(3, "strategy_status", "操作策略", true, 96, 104),
        new(4, "order_summary", "委托价格", true, 84, 90),
        new(5, "premium", "溢价率", true, 74, 78),
        new(6, "price", "现价", true, 66, 70),
        new(7, "change", "涨跌", true, 68, 72),
        new(8, "daily_pnl", "当日盈亏", true, 86, 94),
        new(9, "total_pnl", "持仓盈亏", true, 96, 104),
        new(10, "return_rate", "盈亏率", true, 74, 78),
        new(11, "etf_high", "ETF高点", false, 78, 82),
        new(12, "etf_drawdown", "ETF回撤", true, 78, 82),
        new(13, "index_drawdown", "指数回撤", true, 84, 90),
        new(14, "index_price", "指数点位", false, 92, 96),
        new(15, "index_high", "指数高点", false, 92, 96),
        new(16, "index_change", "指数涨跌", false, 76, 80),
        new(17, "sell_ratio", "收益止盈", true, 76, 82),
        new(18, "take_profit_price", "溢价止盈", true, 76, 82),
        new(19, "iopv", "实时估值", false, 84, 88),
        new(20, "total_quantity", "总持仓数量", true, 108, 116),
        new(21, "average_cost", "综合持仓成本", true, 120, 130),
        new(22, "principal_ratio", "本金占比", true, 78, 84),
        new(23, "adj_factor", "折算系数", false, 80, 86),
        new(24, "equivalent_quantity", "等效持仓", false, 96, 102)
    ];

    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "code",
        "name",
        "action_instruction",
        "strategy_status"
    ];

    private static readonly HashSet<int> SignedNumberColorSourceIndexes =
    [
        5,  // 溢价率
        7,  // 涨跌
        8,  // 当日盈亏
        9,  // 持仓盈亏
        10, // 盈亏率
        12, // ETF回撤
        13, // 指数回撤
        16  // 指数涨跌
    ];

    public static IReadOnlyList<string> DefaultVisibleKeys
        => AllColumns
            .Where(column => column.DefaultVisible)
            .Select(column => column.Key)
            .ToArray();

    public static IReadOnlyList<string> SignedNumberColorKeys
        => AllColumns
            .Where(column => SignedNumberColorSourceIndexes.Contains(column.SourceIndex))
            .Select(column => column.Key)
            .ToArray();

    public static bool UsesSignedNumberColorRule(int sourceIndex)
        => SignedNumberColorSourceIndexes.Contains(sourceIndex);

    public static EtfDecisionColumnParseResult ParseVisibleColumns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new EtfDecisionColumnParseResult(DefaultVisibleKeys, true, false, false);
        }

        string[] rawKeys = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        HashSet<string> knownKeys = AllColumns.Select(column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedKeys = new(StringComparer.OrdinalIgnoreCase);
        bool ignoredUnknown = false;
        foreach (string rawKey in rawKeys)
        {
            if (knownKeys.Contains(rawKey))
            {
                selectedKeys.Add(rawKey);
            }
            else
            {
                ignoredUnknown = true;
            }
        }

        if (selectedKeys.Count == 0)
        {
            return new EtfDecisionColumnParseResult(DefaultVisibleKeys, true, ignoredUnknown, false);
        }

        bool restoredRequired = EnsureRequiredKeys(selectedKeys);
        return new EtfDecisionColumnParseResult(OrderKeys(selectedKeys), false, ignoredUnknown, restoredRequired);
    }

    public static IReadOnlyList<string> NormalizeVisibleKeys(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            return DefaultVisibleKeys;
        }

        HashSet<string> knownKeys = AllColumns.Select(column => column.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedKeys = keys
            .Where(key => !string.IsNullOrWhiteSpace(key) && knownKeys.Contains(key.Trim()))
            .Select(key => key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedKeys.Count == 0)
        {
            return DefaultVisibleKeys;
        }

        EnsureRequiredKeys(selectedKeys);
        return OrderKeys(selectedKeys);
    }

    public static string SerializeVisibleColumns(IEnumerable<string> keys)
        => string.Join(",", NormalizeVisibleKeys(keys));

    public static IReadOnlyList<EtfDecisionColumnDefinition> ResolveVisibleColumns(IEnumerable<string> keys)
    {
        HashSet<string> normalizedKeys = NormalizeVisibleKeys(keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AllColumns.Where(column => normalizedKeys.Contains(column.Key)).ToArray();
    }

    public static string[] ProjectRow(IReadOnlyList<string> row, IEnumerable<string> keys)
        => ResolveVisibleColumns(keys)
            .Select(column => column.SourceIndex < row.Count ? row[column.SourceIndex] : string.Empty)
            .ToArray();

    public static IReadOnlyList<string> ParsePinnedSymbols(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawSymbol in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string symbol = NormalizePinnedSymbol(rawSymbol);
            if (symbol.Length > 0 && seen.Add(symbol))
            {
                result.Add(symbol);
            }
        }

        return result;
    }

    public static string SerializePinnedSymbols(IEnumerable<string> symbols)
        => string.Join(",", NormalizePinnedSymbols(symbols));

    public static IReadOnlyList<string> NormalizePinnedSymbols(IEnumerable<string>? symbols)
    {
        if (symbols is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in symbols)
        {
            string normalized = NormalizePinnedSymbol(symbol);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    public static IReadOnlyList<T> ApplyPinnedSort<T>(
        IEnumerable<T> rows,
        IEnumerable<string> pinnedSymbols,
        Func<T, string> codeSelector)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(pinnedSymbols);
        ArgumentNullException.ThrowIfNull(codeSelector);

        List<T> rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            return rowList;
        }

        Dictionary<string, int> pinnedOrder = NormalizePinnedSymbols(pinnedSymbols)
            .Select((symbol, index) => new { Symbol = symbol, Index = index })
            .ToDictionary(item => item.Symbol, item => item.Index, StringComparer.OrdinalIgnoreCase);
        if (pinnedOrder.Count == 0)
        {
            return rowList;
        }

        return rowList
            .Select((row, index) => new
            {
                Row = row,
                OriginalIndex = index,
                PinnedIndex = pinnedOrder.TryGetValue(NormalizePinnedSymbol(codeSelector(row)), out int pinnedIndex)
                    ? pinnedIndex
                    : int.MaxValue
            })
            .OrderBy(item => item.PinnedIndex)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Row)
            .ToArray();
    }

    private static bool EnsureRequiredKeys(HashSet<string> selectedKeys)
    {
        bool restored = false;
        foreach (string requiredKey in RequiredKeys)
        {
            if (selectedKeys.Add(requiredKey))
            {
                restored = true;
            }
        }

        return restored;
    }

    private static IReadOnlyList<string> OrderKeys(ISet<string> keys)
        => AllColumns
            .Where(column => keys.Contains(column.Key))
            .Select(column => column.Key)
            .ToArray();

    private static string NormalizePinnedSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim();
}

public sealed record EtfDecisionColumnDefinition(
    int SourceIndex,
    string Key,
    string HeaderText,
    bool DefaultVisible,
    double MinWidth,
    double PreferredWidth);

public sealed record EtfDecisionColumnParseResult(
    IReadOnlyList<string> VisibleKeys,
    bool UsedDefault,
    bool IgnoredUnknown,
    bool RestoredRequired);
