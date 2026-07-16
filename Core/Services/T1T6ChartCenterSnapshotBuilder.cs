using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

/// <summary>
/// Mirrors the locked production tier thresholds for display only. It never participates in strategy calculation.
/// </summary>
public sealed class T1T6ChartCenterSnapshotBuilder
{
    private static readonly string[] TierNames =
    {
        "狙击一档", "狙击二档", "狙击三档", "狙击四档", "狙击五档", "狙击六档"
    };

    private static readonly double[] TriggerDrawdowns = { -0.05, -0.10, -0.15, -0.20, -0.25, -0.30 };
    private static readonly double[] DefaultWeights = { 1, 2, 4, 8, 16, 32 };

    public T1T6ChartCenterSnapshot Build(
        T1T6ChartCenterReadModel readModel,
        DateTimeOffset now,
        string? selectedStrategyCode = null,
        int? fallbackSelectionIndex = null)
    {
        ArgumentNullException.ThrowIfNull(readModel);

        StrategyConfigRecord[] strategies = readModel.EnabledStrategies
            .Where(strategy => strategy.Enabled)
            .OrderBy(strategy => strategy.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(strategy => strategy.Id)
            .ToArray();
        Dictionary<string, StrategyDecisionStateRecord> decisions = LatestDecisionsByExactStrategyCode(readModel.LatestDecisions);
        Dictionary<string, MarketQuoteRecord[]> quotesByEtf = readModel.RelatedQuotes
            .Where(quote => string.Equals(quote.MarketType, "ETF", StringComparison.OrdinalIgnoreCase))
            .GroupBy(quote => NormalizeEtfCode(quote.Symbol), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MarketSourceStatusRecord> statuses = LatestStatusesBySource(readModel.RelatedSourceStatuses);
        Dictionary<string, int> duplicateCounts = strategies
            .Select(strategy => NormalizeEtfCode(strategy.Code))
            .Where(code => IsValidEtfCode(code))
            .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        T1T6StrategyRow[] rows = strategies.Select(strategy =>
        {
            string normalizedCode = NormalizeEtfCode(strategy.Code);
            bool validCode = IsValidEtfCode(normalizedCode);
            decisions.TryGetValue(ExactStrategyKey(strategy.Code), out StrategyDecisionStateRecord? decision);
            MarketQuoteRecord? quote = validCode && quotesByEtf.TryGetValue(normalizedCode, out MarketQuoteRecord[]? candidates)
                ? SelectQuote(candidates, now.Offset)
                : null;
            string expectedSource = quote?.Source ?? MarketSources.Tencent;
            statuses.TryGetValue(expectedSource, out MarketSourceStatusRecord? sourceStatus);
            int duplicateCount = validCode && duplicateCounts.TryGetValue(normalizedCode, out int count) ? count : 0;
            return BuildRow(strategy, decision, quote, sourceStatus, normalizedCode, validCode, duplicateCount, now);
        }).ToArray();

        T1T6StrategyRow? selected = SelectRow(rows, selectedStrategyCode, fallbackSelectionIndex);
        string duplicateToolTip = BuildDuplicateToolTip(rows);
        return new T1T6ChartCenterSnapshot
        {
            Rows = rows,
            SelectedRow = selected,
            SelectedStrategyCode = selected?.StrategyCode,
            EnabledStrategyCount = rows.Length,
            HealthyQuoteCount = rows.Count(row => row.IsQuoteHealthy),
            MissingDecisionCount = rows.Count(row => !row.HasDecision),
            DuplicateEtfStrategyCount = rows.Count(row => row.IsDuplicateEtfCode),
            DuplicateEtfToolTip = duplicateToolTip,
            GeneratedAt = now,
            ReadAt = readModel.ReadAt,
            ReadError = readModel.ReadError,
            ReadStatusText = string.IsNullOrWhiteSpace(readModel.ReadError)
                ? $"本地只读快照 {DisplayTime(readModel.ReadAt)}"
                : "本地读取失败"
        };
    }

    public static IReadOnlyList<T1T6TierDisplayDefinition> BuildTierDefinitions(
        StrategyConfigRecord strategy,
        StrategyDecisionStateRecord? decision)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        double[] configured =
        {
            strategy.T1Weight ?? double.NaN,
            strategy.T2Weight ?? double.NaN,
            strategy.T3Weight ?? double.NaN,
            strategy.T4Weight ?? double.NaN,
            strategy.T5Weight ?? double.NaN,
            strategy.T6Weight ?? double.NaN
        };
        double[] weights = configured
            .Select((value, index) => IsPositiveFinite(value) ? value : DefaultWeights[index])
            .ToArray();
        double totalWeight = weights.Sum();
        double cumulative = 0;
        bool hasDrawdown = IsFinite(decision?.IndexDrawdown);

        return weights.Select((weight, index) =>
        {
            cumulative += weight;
            bool conditionMet = hasDrawdown && decision!.IndexDrawdown!.Value <= TriggerDrawdowns[index];
            bool currentSuggested = decision is not null
                                    && string.Equals(decision.TargetTier?.Trim(), TierNames[index], StringComparison.OrdinalIgnoreCase);
            return new T1T6TierDisplayDefinition
            {
                TierNumber = index + 1,
                TierCode = "T" + (index + 1).ToString(CultureInfo.InvariantCulture),
                TierName = TierNames[index],
                TriggerDrawdown = TriggerDrawdowns[index],
                ConfiguredWeight = weight,
                CumulativeWeight = cumulative,
                CumulativeWeightRatio = cumulative / totalWeight,
                IsConditionMet = conditionMet,
                IsCurrentSuggestedTier = currentSuggested,
                ConditionStatusText = !hasDrawdown ? "缺少决策数据" : conditionMet ? "回撤条件满足" : "尚未达到",
                WeightText = FormatWeight(weight),
                CumulativeWeightText = FormatWeight(cumulative),
                CumulativeWeightRatioText = (cumulative / totalWeight).ToString("0.00%", CultureInfo.CurrentCulture),
                TriggerText = TriggerDrawdowns[index].ToString("0%", CultureInfo.CurrentCulture)
            };
        }).ToArray();
    }

    public static T1T6ChartOpenRequest? BuildChartOpenRequest(T1T6StrategyRow? row)
        => row is null || !row.CanOpenChart
            ? null
            : new T1T6ChartOpenRequest("ETF", row.EtfCode, row.EtfName, row.StrategyCode);

    private static T1T6StrategyRow BuildRow(
        StrategyConfigRecord strategy,
        StrategyDecisionStateRecord? decision,
        MarketQuoteRecord? quote,
        MarketSourceStatusRecord? sourceStatus,
        string normalizedCode,
        bool validCode,
        int duplicateCount,
        DateTimeOffset now)
    {
        string freshness = MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, now);
        string sourceState = sourceStatus?.Status?.Trim().ToUpperInvariant() ?? string.Empty;
        bool validPrice = IsFinite(quote?.Price);
        bool quoteHealthy = validCode
                            && validPrice
                            && string.Equals(freshness, "正常", StringComparison.Ordinal)
                            && string.Equals(sourceState, "OK", StringComparison.OrdinalIgnoreCase);
        string dataStatus = ResolveDataStatus(validCode, quote, freshness, sourceState, decision, duplicateCount);
        string quoteStatusText = ResolveQuoteStatus(validCode, quote, freshness, sourceState);
        string etfName = !string.IsNullOrWhiteSpace(quote?.DisplayName)
            ? quote!.DisplayName!.Trim()
            : !string.IsNullOrWhiteSpace(strategy.Name) ? strategy.Name.Trim() : "--";
        string strategyCode = strategy.Code?.Trim() ?? string.Empty;
        string currentTier = decision?.TargetTier?.Trim() ?? string.Empty;
        string currentAction = decision is null
            ? "无决策"
            : string.IsNullOrWhiteSpace(decision.ActionInstruction) ? "--" : decision.ActionInstruction!.Trim();
        string decisionStatus = decision is null
            ? "无决策"
            : string.IsNullOrWhiteSpace(decision.StrategyStatus) ? "已读取派生决策" : decision.StrategyStatus!.Trim();
        string decisionToolTip = decision is null
            ? "未找到与 StrategyCode 精确匹配的持久化策略决策。"
            : string.Join(Environment.NewLine, new[]
            {
                $"计算时间：{DisplayText(decision.CalculatedAt)}",
                $"策略状态：{DisplayText(decision.StrategyStatus)}",
                $"前置状态：{DisplayText(decision.PrerequisiteStatus)}",
                $"说明：{DisplayText(decision.PrerequisiteMessage)}"
            });

        return new T1T6StrategyRow
        {
            StrategyConfigId = strategy.Id,
            StrategyCode = strategyCode,
            StrategyName = string.IsNullOrWhiteSpace(strategy.Name) ? "--" : strategy.Name.Trim(),
            EtfCode = validCode ? normalizedCode : DisplayText(strategy.Code),
            EtfName = etfName,
            IndexSecId = DisplayText(strategy.IndexSecId),
            IsDuplicateEtfCode = duplicateCount > 1,
            DuplicateEtfStrategyCount = duplicateCount,
            DuplicateHintText = duplicateCount > 1 ? "行情和图表共享，策略决策独立" : string.Empty,
            LatestPrice = validPrice ? quote!.Price : null,
            LatestPriceText = FormatPrice(quote?.Price),
            QuoteSource = quote is null ? "--" : MarketMonitorSnapshotBuilder.ResolveSourceName(quote.Source),
            QuoteTime = DisplayText(quote?.QuoteTime),
            ReceivedAt = DisplayText(quote?.ReceivedAt),
            QuoteStatus = freshness,
            QuoteStatusText = quoteStatusText,
            QuoteToolTip = BuildQuoteToolTip(quote, sourceStatus, freshness),
            IsQuoteHealthy = quoteHealthy,
            HasDecision = decision is not null,
            DecisionCalculatedAt = decision?.CalculatedAt ?? string.Empty,
            DecisionCalculatedAtText = DisplayText(decision?.CalculatedAt),
            CurrentIndexDrawdown = IsFinite(decision?.IndexDrawdown) ? decision!.IndexDrawdown : null,
            CurrentIndexDrawdownText = FormatPercent(decision?.IndexDrawdown),
            CurrentPremium = IsFinite(decision?.Premium) ? decision!.Premium : null,
            CurrentPremiumText = FormatPercent(decision?.Premium),
            CurrentAction = currentAction,
            CurrentSuggestedTier = currentTier,
            CurrentSuggestedTierText = decision is null ? "无决策" : DisplayText(currentTier),
            DecisionStatusText = decisionStatus,
            DecisionToolTip = decisionToolTip,
            DataStatusText = dataStatus,
            DataStatusToolTip = BuildDataStatusToolTip(validCode, freshness, sourceStatus, decision, duplicateCount),
            CanOpenChart = validCode,
            ChartToolTip = validCode ? "使用现有证券图表窗口打开" : "ETF代码无效，无法打开图表",
            Tiers = BuildTierDefinitions(strategy, decision)
        };
    }

    private static Dictionary<string, StrategyDecisionStateRecord> LatestDecisionsByExactStrategyCode(
        IEnumerable<StrategyDecisionStateRecord> decisions)
        => decisions
            .Where(decision => !string.IsNullOrWhiteSpace(decision.StrategyCode))
            .GroupBy(decision => ExactStrategyKey(decision.StrategyCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(decision => ParseTime(decision.CalculatedAt)).ThenByDescending(decision => decision.Id).First(),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, MarketSourceStatusRecord> LatestStatusesBySource(
        IEnumerable<MarketSourceStatusRecord> statuses)
        => statuses
            .Where(status => !string.IsNullOrWhiteSpace(status.Source))
            .GroupBy(status => status.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(status => ParseTime(status.UpdatedAt)).ThenByDescending(status => status.Id).First(),
                StringComparer.OrdinalIgnoreCase);

    private static MarketQuoteRecord? SelectQuote(IEnumerable<MarketQuoteRecord> candidates, TimeSpan offset)
        => candidates
            .OrderBy(candidate => string.Equals(candidate.Source, MarketSources.Tencent, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(candidate => ParseTime(candidate.ReceivedAt, offset))
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();

    private static T1T6StrategyRow? SelectRow(
        IReadOnlyList<T1T6StrategyRow> rows,
        string? selectedStrategyCode,
        int? fallbackSelectionIndex)
    {
        if (!string.IsNullOrWhiteSpace(selectedStrategyCode))
        {
            T1T6StrategyRow? exact = rows.FirstOrDefault(row =>
                string.Equals(row.StrategyCode, selectedStrategyCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp(fallbackSelectionIndex ?? 0, 0, rows.Count - 1);
        return rows[index];
    }

    private static string ResolveDataStatus(
        bool validCode,
        MarketQuoteRecord? quote,
        string freshness,
        string sourceState,
        StrategyDecisionStateRecord? decision,
        int duplicateCount)
    {
        if (!validCode) return "ETF代码无效";
        if (quote is null || !IsFinite(quote.Price)) return "实时行情缺失";
        if (string.Equals(sourceState, "ERROR", StringComparison.OrdinalIgnoreCase)) return "数据源异常";
        if (string.Equals(sourceState, "RATE_LIMIT", StringComparison.OrdinalIgnoreCase)) return "限频";
        if (string.Equals(sourceState, "COOLDOWN", StringComparison.OrdinalIgnoreCase)) return "熔断冷却";
        if (!string.Equals(freshness, "正常", StringComparison.Ordinal)) return "行情陈旧";
        if (!string.Equals(sourceState, "OK", StringComparison.OrdinalIgnoreCase)) return "数据源异常";
        if (decision is null) return "无决策";
        return duplicateCount > 1 ? "同标的多策略" : "正常";
    }

    private static string ResolveQuoteStatus(bool validCode, MarketQuoteRecord? quote, string freshness, string sourceState)
    {
        if (!validCode) return "ETF代码无效";
        if (quote is null || !IsFinite(quote.Price)) return "实时行情缺失";
        if (string.Equals(sourceState, "ERROR", StringComparison.OrdinalIgnoreCase)) return "数据源异常";
        if (string.Equals(sourceState, "RATE_LIMIT", StringComparison.OrdinalIgnoreCase)) return "限频";
        if (string.Equals(sourceState, "COOLDOWN", StringComparison.OrdinalIgnoreCase)) return "熔断冷却";
        return freshness;
    }

    private static string BuildQuoteToolTip(
        MarketQuoteRecord? quote,
        MarketSourceStatusRecord? status,
        string freshness)
        => string.Join(Environment.NewLine, new[]
        {
            $"行情来源：{(quote is null ? "--" : MarketMonitorSnapshotBuilder.ResolveSourceName(quote.Source))}",
            $"行情时间：{DisplayText(quote?.QuoteTime)}",
            $"接收时间：{DisplayText(quote?.ReceivedAt)}",
            $"新鲜度：{freshness}",
            $"数据源状态：{MarketMonitorSnapshotBuilder.ResolveSourceStatus(status?.Status)}",
            $"最近错误：{DisplayText(status?.LastError)}"
        });

    private static string BuildDataStatusToolTip(
        bool validCode,
        string freshness,
        MarketSourceStatusRecord? status,
        StrategyDecisionStateRecord? decision,
        int duplicateCount)
    {
        var messages = new List<string>();
        if (!validCode) messages.Add("ETF代码标准化后不是有效的6位证券代码。");
        if (!string.Equals(freshness, "正常", StringComparison.Ordinal)) messages.Add("行情状态：" + freshness);
        if (status is null) messages.Add("未找到相关行情源状态。");
        else if (!string.Equals(status.Status, "OK", StringComparison.OrdinalIgnoreCase)) messages.Add("数据源状态：" + MarketMonitorSnapshotBuilder.ResolveSourceStatus(status.Status));
        if (decision is null) messages.Add("未找到 StrategyCode 精确匹配的持久化决策。");
        if (duplicateCount > 1) messages.Add("行情和图表共享，策略决策独立。");
        return messages.Count == 0 ? "本地只读数据正常" : string.Join(Environment.NewLine, messages);
    }

    private static string BuildDuplicateToolTip(IEnumerable<T1T6StrategyRow> rows)
    {
        string[] groups = rows
            .Where(row => row.IsDuplicateEtfCode)
            .GroupBy(row => row.EtfCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key + "：" + string.Join(" / ", group.Select(row => row.StrategyCode)))
            .ToArray();
        return groups.Length == 0 ? "无同标的多策略" : string.Join(Environment.NewLine, groups);
    }

    private static string NormalizeEtfCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? string.Empty : MarketSymbolNormalizer.DigitsOnly(code.Trim());

    private static bool IsValidEtfCode(string code)
        => code.Length == 6 && code.All(char.IsDigit);

    private static string ExactStrategyKey(string? strategyCode)
        => strategyCode?.Trim() ?? string.Empty;

    private static bool IsFinite(double? value)
        => value.HasValue && double.IsFinite(value.Value);

    private static bool IsPositiveFinite(double value)
        => double.IsFinite(value) && value > 0;

    private static string FormatWeight(double value)
        => value.ToString("0.####", CultureInfo.CurrentCulture);

    private static string FormatPrice(double? value)
        => IsFinite(value) ? value!.Value.ToString("#,##0.####", CultureInfo.CurrentCulture) : "--";

    private static string FormatPercent(double? value)
        => IsFinite(value) ? value!.Value.ToString("0.00%", CultureInfo.CurrentCulture) : "--";

    private static string DisplayText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private static string DisplayTime(DateTimeOffset value)
        => value == default ? "--" : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    private static DateTimeOffset ParseTime(string? value, TimeSpan? offset = null)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsedOffset)
            || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsedOffset))
        {
            return parsedOffset;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return new DateTimeOffset(parsed, offset ?? TimeZoneInfo.Local.GetUtcOffset(parsed));
        }

        return DateTimeOffset.MinValue;
    }
}
