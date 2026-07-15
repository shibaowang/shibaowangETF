using System.Globalization;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class CapitalPositionSnapshotBuilder
{
    public const string ExchangeSource = "场内ETF";
    private const string Missing = "--";

    public CapitalPositionSnapshot Build(CapitalPositionReadModel readModel, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(readModel);

        AccountReplayStateRecord? account = readModel.Account;
        if (account is null)
        {
            return BuildEmpty(readModel.ReadAt);
        }

        bool valuationComplete = account.MarketValueComplete
                                 && !ContainsIncompleteStatus(account.ReplayStatus);
        PositionReplayStateRecord[] exchangePositions = readModel.Positions
            .Where(position => string.Equals(position.Source, ExchangeSource, StringComparison.Ordinal))
            .OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.Id)
            .ToArray();
        OtcPositionReplayStateRecord[] otcPositions = readModel.OtcPositions
            .OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.Id)
            .ToArray();

        Dictionary<string, string> strategyNames = readModel.Strategies
            .Where(strategy => NormalizeKey(strategy.Code).Length > 0)
            .GroupBy(strategy => NormalizeKey(strategy.Code), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(strategy => strategy.Enabled).ThenBy(strategy => strategy.Id).First().Name,
                StringComparer.OrdinalIgnoreCase);

        CapitalPositionEtfRow[] etfRows = exchangePositions
            .Select(position => BuildEtfRow(position, strategyNames, readModel.Quotes, account, valuationComplete, now))
            .ToArray();
        CapitalPositionOtcRow[] otcRows = otcPositions
            .Select(position => BuildOtcRow(position, readModel.OtcChannels, readModel.Quotes, account, valuationComplete, now))
            .ToArray();

        double? etfMarketValue = SumMarketValues(exchangePositions, position => position.MarketValue);
        double? otcMarketValue = SumMarketValues(otcPositions, position => position.MarketValue);
        double? totalAssets = FiniteOrNull(account.TotalAssets);
        bool canCalculateRatios = valuationComplete && totalAssets is > 0;
        double? cashRatio = canCalculateRatios ? FiniteOrNull(account.CashRatio) : null;
        double? etfRatio = canCalculateRatios && IsFinite(etfMarketValue)
            ? etfMarketValue!.Value / totalAssets!.Value
            : null;
        double? otcRatio = canCalculateRatios && IsFinite(otcMarketValue)
            ? otcMarketValue!.Value / totalAssets!.Value
            : null;
        double? positionRatio = canCalculateRatios ? FiniteOrNull(account.PositionRatio) : null;
        double? baseCompletion = FiniteOrNull(readModel.LatestDecision?.BaseCompletionRate);
        double? realSniperPool = FiniteOrNull(readModel.LatestDecision?.RealSniperPool);
        string statusText = BuildAccountStatus(account, valuationComplete);
        string statusColor = ResolveStatusColor(account, valuationComplete);

        var summary = new CapitalPositionAccountSummary
        {
            TotalAssets = totalAssets,
            CashBalance = FiniteOrNull(account.CashBalance),
            Principal = FiniteOrNull(account.Principal),
            KnownMarketValue = FiniteOrNull(account.KnownMarketValue),
            EtfMarketValue = etfMarketValue,
            OtcMarketValue = otcMarketValue,
            TotalUnrealizedPnl = FiniteOrNull(account.TotalUnrealizedPnl),
            TotalRealizedPnl = FiniteOrNull(account.TotalRealizedPnl),
            PositionRatio = positionRatio,
            RealSniperPool = realSniperPool,
            BaseCompletionRate = baseCompletion,
            CashRatio = cashRatio,
            EtfRatio = etfRatio,
            OtcRatio = otcRatio,
            PositionRatioProgress = ClampProgress(positionRatio),
            BaseCompletionProgress = ClampProgress(baseCompletion),
            CashRatioProgress = ClampProgress(cashRatio),
            EtfRatioProgress = ClampProgress(etfRatio),
            OtcRatioProgress = ClampProgress(otcRatio),
            TotalAssetsText = FormatMoney(totalAssets),
            CashBalanceText = FormatMoney(account.CashBalance),
            PrincipalText = FormatMoney(account.Principal),
            KnownMarketValueText = FormatMoney(account.KnownMarketValue),
            EtfMarketValueText = FormatMoney(etfMarketValue),
            OtcMarketValueText = FormatMoney(otcMarketValue),
            TotalUnrealizedPnlText = FormatMoney(account.TotalUnrealizedPnl),
            TotalRealizedPnlText = FormatMoney(account.TotalRealizedPnl),
            PositionRatioText = FormatRatio(positionRatio),
            RealSniperPoolText = FormatMoney(realSniperPool),
            BaseCompletionRateText = FormatRatio(baseCompletion),
            CashRatioText = FormatRatio(cashRatio),
            EtfRatioText = FormatRatio(etfRatio),
            OtcRatioText = FormatRatio(otcRatio),
            ReplayStatusText = statusText,
            TotalAssetsColor = ResolveFinancialColor(totalAssets),
            CashBalanceColor = account.CashBalance is < 0 ? "#F5A623" : "#EAF6FF",
            TotalUnrealizedPnlColor = ResolveFinancialColor(account.TotalUnrealizedPnl),
            TotalRealizedPnlColor = ResolveFinancialColor(account.TotalRealizedPnl),
            ReplayStatusColor = statusColor
        };

        CapitalPositionStrategyAllocationRow[] strategyRows = BuildStrategyRows(
            exchangePositions,
            otcPositions,
            strategyNames,
            totalAssets,
            valuationComplete);
        string replayError = account.ReplayError?.Trim() ?? string.Empty;
        var snapshot = new CapitalPositionSnapshot
        {
            HasAccount = true,
            IsValuationComplete = valuationComplete,
            Summary = summary,
            EtfRows = etfRows,
            OtcRows = otcRows,
            StrategyRows = strategyRows,
            ReadAt = readModel.ReadAt,
            ReadAtText = FormatTimestamp(readModel.ReadAt),
            AccountStatusText = statusText,
            AccountStatusColor = statusColor,
            CalculatedAtText = FormatTimestamp(account.CalculatedAt, now.Offset),
            ReplayError = replayError,
            ReplayErrorSummary = SummarizeError(replayError),
            SnapshotKey = BuildSnapshotKey(account, summary, etfRows, otcRows, strategyRows)
        };
        return snapshot;
    }

    private static CapitalPositionSnapshot BuildEmpty(DateTimeOffset readAt)
        => new()
        {
            HasAccount = false,
            IsValuationComplete = false,
            ReadAt = readAt,
            ReadAtText = FormatTimestamp(readAt),
            AccountStatusText = "暂无账户回放结果",
            AccountStatusColor = "#F5A623",
            SnapshotKey = "EMPTY"
        };

    private static CapitalPositionEtfRow BuildEtfRow(
        PositionReplayStateRecord position,
        IReadOnlyDictionary<string, string> strategyNames,
        IReadOnlyList<MarketQuoteRecord> quotes,
        AccountReplayStateRecord account,
        bool valuationComplete,
        DateTimeOffset now)
    {
        MarketQuoteRecord? quote = SelectMatchingQuote(position.ActualCode, "ETF", position.MarketPrice, quotes, now.Offset);
        double? assetRatio = CanCalculateRowRatio(account, valuationComplete, position.MarketValue)
            ? position.MarketValue!.Value / account.TotalAssets!.Value
            : null;
        string strategyKey = NormalizeKey(position.StrategyCode);
        string name = strategyNames.TryGetValue(strategyKey, out string? configuredName)
                      && !string.IsNullOrWhiteSpace(configuredName)
            ? configuredName.Trim()
            : Missing;
        CapitalPositionQuoteMetadata metadata = BuildQuoteMetadata(quote, now);

        return new CapitalPositionEtfRow
        {
            ReplayId = position.Id,
            StrategyCode = Display(position.StrategyCode),
            EtfName = name,
            ActualCode = Display(position.ActualCode),
            Source = position.Source,
            Quantity = position.Quantity,
            AverageCost = position.AverageCost,
            MarketPrice = FiniteOrNull(position.MarketPrice),
            MarketValue = FiniteOrNull(position.MarketValue),
            UnrealizedPnl = FiniteOrNull(position.UnrealizedPnl),
            ReturnRate = FiniteOrNull(position.ReturnRate),
            AssetRatio = assetRatio,
            QuantityText = FormatQuantity(position.Quantity),
            AverageCostText = FormatPrice(position.AverageCost),
            MarketPriceText = FormatPrice(position.MarketPrice),
            MarketValueText = FormatMoney(position.MarketValue),
            UnrealizedPnlText = FormatMoney(position.UnrealizedPnl),
            ReturnRateText = FormatRatio(position.ReturnRate),
            AssetRatioText = FormatRatio(assetRatio),
            QuoteSource = metadata.Source,
            QuoteSourceText = metadata.SourceText,
            QuoteTimeText = metadata.QuoteTimeText,
            ReceivedAtText = metadata.ReceivedAtText,
            CacheStatus = metadata.CacheStatus,
            CacheAgeText = metadata.CacheAgeText,
            CacheToolTip = metadata.ToolTip,
            UnrealizedPnlColor = ResolveFinancialColor(position.UnrealizedPnl)
        };
    }

    private static CapitalPositionOtcRow BuildOtcRow(
        OtcPositionReplayStateRecord position,
        IReadOnlyList<OtcChannelRecord> channels,
        IReadOnlyList<MarketQuoteRecord> quotes,
        AccountReplayStateRecord account,
        bool valuationComplete,
        DateTimeOffset now)
    {
        MarketQuoteRecord? quote = SelectMatchingQuote(position.ActualCode, "OTC", position.Nav, quotes, now.Offset);
        double? assetRatio = CanCalculateRowRatio(account, valuationComplete, position.MarketValue)
            ? position.MarketValue!.Value / account.TotalAssets!.Value
            : null;
        OtcChannelRecord? channel = channels
            .Where(candidate => string.Equals(NormalizeKey(candidate.StrategyCode), NormalizeKey(position.StrategyCode), StringComparison.OrdinalIgnoreCase)
                                && string.Equals(NormalizeSymbol(candidate.OtcCode, "OTC"), NormalizeSymbol(position.ActualCode, "OTC"), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Enabled)
            .ThenBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        CapitalPositionQuoteMetadata metadata = BuildQuoteMetadata(quote, now);

        return new CapitalPositionOtcRow
        {
            ReplayId = position.Id,
            StrategyCode = Display(position.StrategyCode),
            FundCode = Display(position.ActualCode),
            FundName = string.IsNullOrWhiteSpace(quote?.DisplayName) ? Missing : quote!.DisplayName!.Trim(),
            Quantity = position.Quantity,
            CostAmount = position.CostAmount,
            AverageCost = position.AverageCost,
            Nav = FiniteOrNull(position.Nav),
            MarketValue = FiniteOrNull(position.MarketValue),
            UnrealizedPnl = FiniteOrNull(position.UnrealizedPnl),
            ReturnRate = FiniteOrNull(position.ReturnRate),
            AssetRatio = assetRatio,
            ChannelPriority = channel?.Priority,
            QuantityText = FormatQuantity(position.Quantity),
            CostAmountText = FormatMoney(position.CostAmount),
            AverageCostText = FormatPrice(position.AverageCost),
            NavText = FormatPrice(position.Nav),
            MarketValueText = FormatMoney(position.MarketValue),
            UnrealizedPnlText = FormatMoney(position.UnrealizedPnl),
            ReturnRateText = FormatRatio(position.ReturnRate),
            AssetRatioText = FormatRatio(assetRatio),
            ChannelPriorityText = channel?.Priority.ToString(CultureInfo.InvariantCulture) ?? Missing,
            QuoteSource = metadata.Source,
            QuoteSourceText = metadata.SourceText,
            QuoteTimeText = metadata.QuoteTimeText,
            ReceivedAtText = metadata.ReceivedAtText,
            CacheStatus = metadata.CacheStatus,
            CacheAgeText = metadata.CacheAgeText,
            CacheToolTip = metadata.ToolTip,
            UnrealizedPnlColor = ResolveFinancialColor(position.UnrealizedPnl)
        };
    }

    private static CapitalPositionStrategyAllocationRow[] BuildStrategyRows(
        IReadOnlyList<PositionReplayStateRecord> etfPositions,
        IReadOnlyList<OtcPositionReplayStateRecord> otcPositions,
        IReadOnlyDictionary<string, string> strategyNames,
        double? totalAssets,
        bool valuationComplete)
    {
        string[] strategyCodes = etfPositions.Select(position => NormalizeKey(position.StrategyCode))
            .Concat(otcPositions.Select(position => NormalizeKey(position.StrategyCode)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return strategyCodes.Select(strategyCode =>
            {
                PositionReplayStateRecord[] etf = etfPositions
                    .Where(position => string.Equals(NormalizeKey(position.StrategyCode), strategyCode, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                OtcPositionReplayStateRecord[] otc = otcPositions
                    .Where(position => string.Equals(NormalizeKey(position.StrategyCode), strategyCode, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                double? etfValue = SumMarketValues(etf, position => position.MarketValue);
                double? otcValue = SumMarketValues(otc, position => position.MarketValue);
                double? total = AddKnownValues(etfValue, otcValue);
                double? ratio = valuationComplete && totalAssets is > 0 && IsFinite(total)
                    ? total!.Value / totalAssets.Value
                    : null;
                string displayCode = strategyCode.Length == 0 ? "未分配" : strategyCode;
                string name = strategyNames.TryGetValue(strategyCode, out string? configuredName)
                              && !string.IsNullOrWhiteSpace(configuredName)
                    ? configuredName.Trim()
                    : Missing;
                return new CapitalPositionStrategyAllocationRow
                {
                    StrategyCode = displayCode,
                    StrategyName = name,
                    EtfMarketValue = etfValue,
                    OtcMarketValue = otcValue,
                    TotalMarketValue = total,
                    AssetRatio = ratio,
                    AssetRatioProgress = ClampProgress(ratio),
                    EtfMarketValueText = FormatMoney(etfValue),
                    OtcMarketValueText = FormatMoney(otcValue),
                    TotalMarketValueText = FormatMoney(total),
                    AssetRatioText = FormatRatio(ratio)
                };
            })
            .OrderByDescending(row => row.TotalMarketValue ?? double.MinValue)
            .ThenBy(row => row.StrategyCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MarketQuoteRecord? SelectMatchingQuote(
        string symbol,
        string marketType,
        double? replayPrice,
        IReadOnlyList<MarketQuoteRecord> quotes,
        TimeSpan nowOffset)
    {
        if (!IsFinite(replayPrice) || replayPrice!.Value <= 0)
        {
            return null;
        }

        string normalizedSymbol = NormalizeSymbol(symbol, marketType);
        string preferredSource = string.Equals(marketType, "ETF", StringComparison.OrdinalIgnoreCase)
            ? MarketSources.Tencent
            : MarketSources.SinaFund;
        return quotes
            .Where(quote => string.Equals(quote.MarketType?.Trim(), marketType, StringComparison.OrdinalIgnoreCase))
            .Where(quote => string.Equals(NormalizeSymbol(quote.Symbol, marketType), normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(NormalizeSymbol(quote.RawCode, marketType), normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(IsRealQuote)
            .Where(quote => PricesMatch(quote.Price, replayPrice))
            .OrderBy(quote => string.Equals(quote.Source, preferredSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(quote => TryParseTimestamp(quote.ReceivedAt, nowOffset, out _))
            .ThenByDescending(quote => ParseTimestampOrMinimum(quote.ReceivedAt, nowOffset))
            .ThenByDescending(quote => quote.Id)
            .FirstOrDefault();
    }

    private static bool IsRealQuote(MarketQuoteRecord quote)
    {
        if (!IsFinite(quote.Price) || quote.Price!.Value <= 0 || string.IsNullOrWhiteSpace(quote.Source))
        {
            return false;
        }

        string source = quote.Source.Trim();
        return !source.Contains("MOCK", StringComparison.OrdinalIgnoreCase)
               && !source.Contains("FAKE", StringComparison.OrdinalIgnoreCase)
               && !source.Contains("SIMULATED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PricesMatch(double? candidate, double? replay)
    {
        if (!IsFinite(candidate) || !IsFinite(replay))
        {
            return false;
        }

        double tolerance = Math.Max(1e-8, Math.Abs(replay!.Value) * 1e-8);
        return Math.Abs(candidate!.Value - replay.Value) <= tolerance;
    }

    private static CapitalPositionQuoteMetadata BuildQuoteMetadata(MarketQuoteRecord? quote, DateTimeOffset now)
    {
        if (quote is null)
        {
            return new CapitalPositionQuoteMetadata();
        }

        string receivedAt = FormatTimestamp(quote.ReceivedAt, now.Offset);
        string age = MarketMonitorSnapshotBuilder.FormatCacheAge(quote, now);
        return new CapitalPositionQuoteMetadata
        {
            Source = quote.Source,
            SourceText = quote.Source,
            QuoteTimeText = FormatTimestamp(quote.QuoteTime, now.Offset),
            ReceivedAtText = receivedAt,
            CacheStatus = MarketMonitorSnapshotBuilder.ResolveFreshnessStatus(quote, now),
            CacheAgeText = age,
            ToolTip = $"数据源：{quote.Source}；本地接收：{receivedAt}；缓存年龄：{age}"
        };
    }

    private static bool CanCalculateRowRatio(
        AccountReplayStateRecord account,
        bool valuationComplete,
        double? marketValue)
        => valuationComplete
           && account.TotalAssets is > 0
           && IsFinite(account.TotalAssets)
           && IsFinite(marketValue);

    private static double? SumMarketValues<T>(IReadOnlyList<T> rows, Func<T, double?> selector)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        double[] values = rows.Select(selector).Where(IsFinite).Select(value => value!.Value).ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    private static double? AddKnownValues(double? first, double? second)
    {
        bool hasFirst = IsFinite(first);
        bool hasSecond = IsFinite(second);
        if (!hasFirst && !hasSecond)
        {
            return null;
        }

        return (hasFirst ? first!.Value : 0) + (hasSecond ? second!.Value : 0);
    }

    private static string BuildAccountStatus(AccountReplayStateRecord account, bool valuationComplete)
    {
        string replayStatus = Display(account.ReplayStatus);
        return valuationComplete ? $"{replayStatus} / 估值完整" : $"{replayStatus} / 估值不完整";
    }

    private static string ResolveStatusColor(AccountReplayStateRecord account, bool valuationComplete)
    {
        if (!valuationComplete)
        {
            return "#F5A623";
        }

        return account.ReplayStatus.Contains("异常", StringComparison.Ordinal)
               || !string.IsNullOrWhiteSpace(account.ReplayError)
            ? "#FF5D68"
            : "#63D95F";
    }

    private static bool ContainsIncompleteStatus(string? status)
        => !string.IsNullOrWhiteSpace(status)
           && status.Contains("不完整", StringComparison.Ordinal);

    private static string BuildSnapshotKey(
        AccountReplayStateRecord account,
        CapitalPositionAccountSummary summary,
        IReadOnlyList<CapitalPositionEtfRow> etfRows,
        IReadOnlyList<CapitalPositionOtcRow> otcRows,
        IReadOnlyList<CapitalPositionStrategyAllocationRow> strategyRows)
    {
        var builder = new StringBuilder();
        builder.Append(account.Id).Append('|').Append(account.CalculatedAt).Append('|')
            .Append(account.ReplayStatus).Append('|').Append(account.ReplayError).Append('|')
            .Append(summary.TotalAssetsText).Append('|').Append(summary.CashBalanceText).Append('|')
            .Append(summary.PrincipalText).Append('|').Append(summary.KnownMarketValueText).Append('|')
            .Append(summary.EtfMarketValueText).Append('|').Append(summary.OtcMarketValueText).Append('|')
            .Append(summary.TotalUnrealizedPnlText).Append('|').Append(summary.TotalRealizedPnlText).Append('|')
            .Append(summary.PositionRatioText).Append('|').Append(summary.CashRatioText).Append('|')
            .Append(summary.EtfRatioText).Append('|').Append(summary.OtcRatioText).Append('|')
            .Append(summary.RealSniperPoolText).Append('|').Append(summary.BaseCompletionRateText);
        foreach (CapitalPositionEtfRow row in etfRows)
        {
            builder.Append("|E:").Append(row.ReplayId).Append(':').Append(row.StrategyCode).Append(':')
                .Append(row.EtfName).Append(':').Append(row.ActualCode).Append(':')
                .Append(row.QuantityText).Append(':').Append(row.AverageCostText).Append(':')
                .Append(row.MarketPriceText).Append(':').Append(row.MarketValueText).Append(':')
                .Append(row.UnrealizedPnlText).Append(':').Append(row.ReturnRateText).Append(':')
                .Append(row.AssetRatioText).Append(':').Append(row.QuoteSource).Append(':')
                .Append(row.QuoteTimeText).Append(':').Append(row.ReceivedAtText).Append(':')
                .Append(row.CacheStatus);
        }

        foreach (CapitalPositionOtcRow row in otcRows)
        {
            builder.Append("|O:").Append(row.ReplayId).Append(':').Append(row.StrategyCode).Append(':')
                .Append(row.FundCode).Append(':').Append(row.FundName).Append(':')
                .Append(row.QuantityText).Append(':').Append(row.CostAmountText).Append(':')
                .Append(row.AverageCostText).Append(':').Append(row.NavText).Append(':')
                .Append(row.MarketValueText).Append(':').Append(row.UnrealizedPnlText).Append(':')
                .Append(row.ReturnRateText).Append(':').Append(row.AssetRatioText).Append(':')
                .Append(row.ChannelPriorityText).Append(':').Append(row.QuoteSource).Append(':')
                .Append(row.QuoteTimeText).Append(':').Append(row.ReceivedAtText).Append(':')
                .Append(row.CacheStatus);
        }

        foreach (CapitalPositionStrategyAllocationRow row in strategyRows)
        {
            builder.Append("|S:").Append(row.StrategyCode).Append(':').Append(row.StrategyName).Append(':')
                .Append(row.EtfMarketValueText).Append(':').Append(row.OtcMarketValueText).Append(':')
                .Append(row.TotalMarketValueText).Append(':').Append(row.AssetRatioText);
        }

        return builder.ToString();
    }

    private static string SummarizeError(string error)
    {
        if (error.Length == 0)
        {
            return Missing;
        }

        string firstLine = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? error;
        return firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...";
    }

    private static string FormatMoney(double? value)
        => IsFinite(value) ? value!.Value.ToString("N2", CultureInfo.InvariantCulture) : Missing;

    private static string FormatQuantity(double? value)
        => IsFinite(value) ? value!.Value.ToString("#,##0.####", CultureInfo.InvariantCulture) : Missing;

    private static string FormatPrice(double? value)
        => IsFinite(value) ? value!.Value.ToString("#,##0.######", CultureInfo.InvariantCulture) : Missing;

    private static string FormatRatio(double? value)
        => IsFinite(value) ? (value!.Value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%" : Missing;

    private static string FormatTimestamp(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(string? value, TimeSpan offset)
        => TryParseTimestamp(value, offset, out DateTimeOffset parsed) ? FormatTimestamp(parsed) : Missing;

    private static string ResolveFinancialColor(double? value)
        => !IsFinite(value) || value == 0
            ? "#EAF6FF"
            : value > 0 ? "#FF4D57" : "#84CC16";

    private static double ClampProgress(double? value)
        => IsFinite(value) ? Math.Clamp(value!.Value, 0, 1) : 0;

    private static double? FiniteOrNull(double? value)
        => IsFinite(value) ? value : null;

    private static bool IsFinite(double? value)
        => value.HasValue && double.IsFinite(value.Value);

    private static string NormalizeKey(string? value)
        => value?.Trim() ?? string.Empty;

    private static string NormalizeSymbol(string? value, string marketType)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : marketType is "ETF" or "OTC"
                ? MarketSymbolNormalizer.DigitsOnly(value)
                : value.Trim().ToUpperInvariant();

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? Missing : value.Trim();

    private static bool TryParseTimestamp(string? value, TimeSpan offset, out DateTimeOffset parsed)
    {
        if (HasExplicitOffset(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset withOffset))
        {
            parsed = withOffset;
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime local))
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

        int separator = Math.Max(timestamp.IndexOf('T'), timestamp.IndexOf(' '));
        return separator >= 0
               && (timestamp.LastIndexOf('+') > separator || timestamp.LastIndexOf('-') > separator);
    }

    private static DateTimeOffset ParseTimestampOrMinimum(string? value, TimeSpan offset)
        => TryParseTimestamp(value, offset, out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;

    private sealed class CapitalPositionQuoteMetadata
    {
        public string Source { get; init; } = string.Empty;
        public string SourceText { get; init; } = Missing;
        public string QuoteTimeText { get; init; } = Missing;
        public string ReceivedAtText { get; init; } = Missing;
        public string CacheStatus { get; init; } = "未关联";
        public string CacheAgeText { get; init; } = Missing;
        public string ToolTip { get; init; } = "未关联到本次回放估值价格对应的真实行情缓存";
    }
}
