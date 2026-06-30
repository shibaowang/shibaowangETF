using System.Globalization;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class AlertRuleEvaluator
{
    private const string ClearCash = "\u5168\u6e05\u6362\u73b0\u91d1";
    private const string TakeProfitReduce = "\u6b62\u76c8\u51cf\u4ed3";
    private const string PremiumReduce = "\u6ea2\u4ef7\u8fbe\u6807\u51cf\u4ed3";
    private const string BuyDip = "\u9022\u4f4e\u5438\u7b79";
    private const string StrategicBase = "\u6218\u7565\u5e95\u4ed3";
    private const string SniperTier1 = "\u72d9\u51fb\u4e00\u6863";
    private const string SniperTier2 = "\u72d9\u51fb\u4e8c\u6863";
    private const string SniperTier3 = "\u72d9\u51fb\u4e09\u6863";
    private const string SniperTier4 = "\u72d9\u51fb\u56db\u6863";
    private const string SniperTier5 = "\u72d9\u51fb\u4e94\u6863";
    private const string SniperTier6 = "\u72d9\u51fb\u516d\u6863";
    private const string ExtremePremium = "\u6781\u7aef\u6ea2\u4ef7";
    private const string ProhibitBuild = "\u7981\u6b62\u5efa\u4ed3";
    private const string CashInsufficient = "\u73b0\u91d1\u4e0d\u8db3";
    private const string NoSellable = "\u65e0\u53ef\u5356";
    private const string BaseProtection = "\u5e95\u4ed3\u4fdd\u62a4";
    private const string MarketUnavailable = "\u884c\u60c5\u4e0d\u53ef\u7528";
    private const string MarketMissing = "\u884c\u60c5\u7f3a\u5931";
    private const string NavUnavailable = "\u51c0\u503c\u4e0d\u53ef\u7528";
    private const string NavMissing = "\u51c0\u503c\u7f3a\u5931";
    private const string MinTradeUnitInsufficient = "\u6700\u5c0f\u4ea4\u6613\u5355\u4f4d\u4e0d\u8db3";
    private const string Insufficient = "\u4e0d\u8db3";
    private const string Missing = "\u7f3a\u5c11";
    private const string Unable = "\u65e0\u6cd5";
    private const string NotExecutable = "\u4e0d\u53ef\u6267\u884c";
    private const string FinancialAbnormal = "\u8d22\u52a1\u5f02\u5e38";
    private const string Market = "\u884c\u60c5";
    private const string KLine = "K\u7ebf";
    private const string AccountReplay = "\u8d26\u6237\u56de\u653e";

    public static readonly string[] StrategyTriggerKeywords =
    {
        ClearCash,
        TakeProfitReduce,
        PremiumReduce,
        BuyDip,
        StrategicBase,
        SniperTier1,
        SniperTier2,
        SniperTier3,
        SniperTier4,
        SniperTier5,
        SniperTier6,
        ExtremePremium,
        ProhibitBuild
    };

    private static readonly string[] OrderReasonKeywords =
    {
        CashInsufficient,
        NoSellable,
        BaseProtection,
        MarketUnavailable,
        MarketMissing,
        NavUnavailable,
        NavMissing,
        MinTradeUnitInsufficient,
        Insufficient,
        Missing,
        Unable,
        NotExecutable
    };

    public IReadOnlyList<AlertEvent> Evaluate(AlertRuleEvaluationInput input, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        DateTimeOffset createdAt = now ?? DateTimeOffset.Now;
        var events = new List<AlertEvent>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (StrategyDecisionStateRecord decision in input.StrategyDecisions)
        {
            AlertEvent? alert = EvaluateStrategyDecision(decision, createdAt);
            if (alert is not null && seen.Add(alert.DedupeKey))
            {
                events.Add(alert);
            }
        }

        foreach (OrderDraftStateRecord draft in input.OrderDrafts)
        {
            AlertEvent? alert = EvaluateOrderDraft(draft, createdAt);
            if (alert is not null && seen.Add(alert.DedupeKey))
            {
                events.Add(alert);
            }
        }

        foreach (MarketSourceStatusRecord status in input.MarketStatuses)
        {
            AlertEvent? alert = EvaluateMarketStatus(status, createdAt);
            if (alert is not null && seen.Add(alert.DedupeKey))
            {
                events.Add(alert);
            }
        }

        AccountReplayStateRecord? replay = input.AccountReplayState;
        if (replay is not null && string.Equals(replay.ReplayStatus, FinancialAbnormal, StringComparison.Ordinal))
        {
            AlertEvent alert = CreateAccountReplayAlert(replay, createdAt);
            if (seen.Add(alert.DedupeKey))
            {
                events.Add(alert);
            }
        }

        Dictionary<string, MarketSourceStatusRecord> statusesBySource = input.MarketStatuses
            .GroupBy(status => NormalizeDisplay(status.Source), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (RuntimeLogRecord log in input.RuntimeLogs.OrderBy(log => log.Id))
        {
            AlertEvent? alert = EvaluateRuntimeLog(log, createdAt, statusesBySource);
            if (alert is not null && seen.Add(alert.DedupeKey))
            {
                events.Add(alert);
            }
        }

        return events;
    }

    public static AlertEvent CreateTestWechatEvent(DateTimeOffset? now = null)
    {
        DateTimeOffset createdAt = now ?? DateTimeOffset.Now;
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.Test,
            Severity = AlertSeverity.Normal,
            Title = "\u3010\u6d4b\u8bd5\u9884\u8b66\u3011PushPlus \u5fae\u4fe1\u9884\u8b66\u6d4b\u8bd5",
            Content = $"\u89e6\u53d1\u65f6\u95f4\uff1a{FormatTime(createdAt)}<br>\u6765\u6e90\uff1a\u8de8\u5883ETF\u667a\u80fd\u6295\u8d44\u51b3\u7b56\u7cfb\u7edf<br>\u8bf4\u660e\uff1a\u8fd9\u662f\u4e00\u6761\u6d4b\u8bd5\u5fae\u4fe1\u6d88\u606f\uff0c\u4e0d\u4ee3\u8868\u4ea4\u6613\u6216\u6210\u4ea4\u3002",
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.Test, null, "\u6d4b\u8bd5\u5fae\u4fe1", "\u7528\u6237\u624b\u52a8\u6d4b\u8bd5", "SystemSettings"),
            Source = "SystemSettings",
            Action = "\u6d4b\u8bd5\u5fae\u4fe1",
            Reason = "\u7528\u6237\u624b\u52a8\u6d4b\u8bd5"
        };
        return alert.WithStableHash();
    }

    public static AlertEvent CreateTestVoiceEvent(DateTimeOffset? now = null)
    {
        DateTimeOffset createdAt = now ?? DateTimeOffset.Now;
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.Test,
            Severity = AlertSeverity.Normal,
            Title = "\u3010\u6d4b\u8bd5\u9884\u8b66\u3011\u7cfb\u7edf\u8bed\u97f3\u6d4b\u8bd5",
            Content = $"\u89e6\u53d1\u65f6\u95f4\uff1a{FormatTime(createdAt)}<br>\u6765\u6e90\uff1a\u8de8\u5883ETF\u667a\u80fd\u6295\u8d44\u51b3\u7b56\u7cfb\u7edf<br>\u8bf4\u660e\uff1a\u8fd9\u662f\u4e00\u6761\u6d4b\u8bd5\u8bed\u97f3\uff0c\u4e0d\u4ee3\u8868\u4ea4\u6613\u6216\u6210\u4ea4\u3002",
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.Test, null, "\u6d4b\u8bd5\u8bed\u97f3", "\u7528\u6237\u624b\u52a8\u6d4b\u8bd5", "SystemSettings"),
            Source = "SystemSettings",
            Action = "\u6d4b\u8bd5\u8bed\u97f3",
            Reason = "\u7528\u6237\u624b\u52a8\u6d4b\u8bd5"
        };
        return alert.WithStableHash();
    }

    private static AlertEvent? EvaluateStrategyDecision(StrategyDecisionStateRecord decision, DateTimeOffset createdAt)
    {
        string action = NormalizeDisplay(decision.ActionInstruction);
        string reason = NormalizeDisplay(decision.StrategyStatus);
        string text = action + " " + reason;
        if (!StrategyTriggerKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
        {
            return null;
        }

        string strategyCode = NormalizeDisplay(decision.StrategyCode);
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.StrategyDecision,
            Severity = IsSevereStrategy(action, reason) ? AlertSeverity.Severe : AlertSeverity.Normal,
            StrategyCode = strategyCode,
            Title = $"\u3010\u4f5c\u6218\u6307\u4ee4\u3011{strategyCode} {action}",
            Content = BuildStrategyContent(createdAt, strategyCode, decision, action, reason),
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, strategyCode, action, reason),
            Source = "strategy_decision_state",
            Action = action,
            Reason = reason,
            Price = decision.SuggestedPrice,
            PremiumRate = decision.Premium
        };
        return alert.WithStableHash();
    }

    private static AlertEvent? EvaluateOrderDraft(OrderDraftStateRecord draft, DateTimeOffset createdAt)
    {
        if (draft.IsExecutable || string.IsNullOrWhiteSpace(draft.Reason))
        {
            return null;
        }

        string reason = draft.Reason.Trim();
        if (!OrderReasonKeywords.Any(keyword => reason.Contains(keyword, StringComparison.Ordinal)))
        {
            return null;
        }

        string strategyCode = NormalizeDisplay(draft.StrategyCode);
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.OrderNotExecutable,
            Severity = AlertSeverity.Normal,
            StrategyCode = strategyCode,
            Title = $"\u3010\u59d4\u6258\u4e0d\u53ef\u6267\u884c\u3011{strategyCode} {reason}",
            Content = BuildOrderContent(createdAt, draft, reason),
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.OrderNotExecutable, strategyCode, draft.ActionInstruction ?? draft.Side, reason),
            Source = "order_draft_state",
            Action = NormalizeDisplay(draft.ActionInstruction ?? draft.Side),
            Reason = reason,
            Price = draft.Price
        };
        return alert.WithStableHash();
    }

    private static AlertEvent? EvaluateMarketStatus(MarketSourceStatusRecord status, DateTimeOffset createdAt)
    {
        if (IsSchedulerRateLimitProtection($"{status.Source} {status.Status} {status.LastError}"))
        {
            return null;
        }

        if (IsTransientSinaFundFailureWithCache(status))
        {
            return null;
        }

        if (!IsMarketFailure(status))
        {
            return null;
        }

        string source = NormalizeDisplay(status.Source);
        string detail = NormalizeDisplay(status.LastError ?? status.Status);
        string reason = NormalizeMarketReason(source, status.Status, detail);
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.MarketRuntime,
            Severity = AlertSeverity.Market,
            Title = $"\u3010\u884c\u60c5\u5f02\u5e38\u3011{source}",
            Content = BuildMarketContent(createdAt, status, detail),
            DedupeKey = BuildMarketDedupeKey(source, reason),
            Source = source,
            Action = NormalizeDisplay(status.Status),
            Reason = reason
        };
        return alert.WithStableHash();
    }

    private static AlertEvent CreateAccountReplayAlert(AccountReplayStateRecord replay, DateTimeOffset createdAt)
    {
        string reason = NormalizeDisplay(replay.ReplayError ?? replay.ReplayStatus);
        var alert = new AlertEvent
        {
            CreatedAt = createdAt,
            AlertType = AlertTypes.AccountReplay,
            Severity = AlertSeverity.Severe,
            Title = "\u3010\u8d26\u6237\u56de\u653e\u5f02\u5e38\u3011TradeLog \u8d22\u52a1\u5f02\u5e38",
            Content = $"\u89e6\u53d1\u65f6\u95f4\uff1a{FormatTime(createdAt)}<br>\u72b6\u6001\uff1a{Html(replay.ReplayStatus)}<br>\u539f\u56e0\uff1a{Html(reason)}<br>\u6765\u6e90\uff1aaccount_replay_state",
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.AccountReplay, null, replay.ReplayStatus, reason, "account_replay_state"),
            Source = "account_replay_state",
            Action = replay.ReplayStatus,
            Reason = reason
        };
        return alert.WithStableHash();
    }

    private static AlertEvent? EvaluateRuntimeLog(
        RuntimeLogRecord log,
        DateTimeOffset fallbackCreatedAt,
        IReadOnlyDictionary<string, MarketSourceStatusRecord> statusesBySource)
    {
        if (!string.Equals(log.Level, "ERROR", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(log.Level, "WARN", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string text = $"{log.Module} {log.Message} {log.Detail}";
        bool market = ContainsAny(text, Market, KLine, "EASTMONEY", "TENCENT", "SINA", "push2his", "qt.gtimg", "hq.sinajs");
        bool account = ContainsAny(text, AccountReplay, FinancialAbnormal, "TradeLog");
        if (!market && !account)
        {
            return null;
        }

        DateTimeOffset logCreatedAt = ParseRuntimeLogTime(log.Time, fallbackCreatedAt);
        string alertType = market ? AlertTypes.MarketRuntime : AlertTypes.AccountReplay;
        string source = market ? NormalizeDisplay(log.Module) : "runtime_log";
        if (market && ShouldSuppressRuntimeMarketLog(log, source, logCreatedAt, statusesBySource))
        {
            return null;
        }

        string reason = market
            ? NormalizeMarketReason(source, log.Level, text)
            : NormalizeDisplay(log.Message);
        var alert = new AlertEvent
        {
            CreatedAt = logCreatedAt,
            AlertType = alertType,
            Severity = market ? AlertSeverity.Market : AlertSeverity.Severe,
            Title = market ? $"\u3010\u884c\u60c5\u5f02\u5e38\u3011{log.Module}" : $"\u3010\u8fd0\u884c\u5f02\u5e38\u3011{log.Module}",
            Content = BuildRuntimeLogContent(logCreatedAt, log),
            DedupeKey = market
                ? BuildMarketDedupeKey(source, reason)
                : AlertEvent.BuildDedupeKey(alertType, null, log.Module, reason, "runtime_log"),
            Source = source,
            Action = market ? NormalizeDisplay(log.Level) : log.Module,
            Reason = reason
        };
        return alert.WithStableHash();
    }

    private static bool ShouldSuppressRuntimeMarketLog(
        RuntimeLogRecord log,
        string source,
        DateTimeOffset logCreatedAt,
        IReadOnlyDictionary<string, MarketSourceStatusRecord> statusesBySource)
    {
        if (string.Equals(source, "SecurityChart", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string text = $"{log.Module} {log.Message} {log.Detail}";
        if (IsSchedulerRateLimitProtection(text)
            || text.Contains("SKIP_HISTORY_DOWNGRADE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsTransientSinaFundRuntimeLogWithCache(text, source, statusesBySource))
        {
            return true;
        }

        if (!statusesBySource.TryGetValue(source, out MarketSourceStatusRecord? status)
            || !string.Equals(status.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseStoredTime(status.LastSuccessAt, logCreatedAt.Offset, out DateTimeOffset lastSuccessAt))
        {
            return false;
        }

        return lastSuccessAt >= logCreatedAt;
    }

    private static DateTimeOffset ParseRuntimeLogTime(string? value, DateTimeOffset fallback)
        => TryParseStoredTime(value, fallback.Offset, out DateTimeOffset parsed) ? parsed : fallback;

    private static bool TryParseStoredTime(string? value, TimeSpan offset, out DateTimeOffset parsed)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime localTime))
        {
            parsed = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), offset);
            return true;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out parsed);
    }

    private static bool IsMarketFailure(MarketSourceStatusRecord status)
        => string.Equals(status.Status, "ERROR", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "COOLDOWN", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientSinaFundFailureWithCache(MarketSourceStatusRecord status)
        => string.Equals(status.Source, "SINA_FUND", StringComparison.OrdinalIgnoreCase)
           && status.FailureCount < 3
           && !string.IsNullOrWhiteSpace(status.LastSuccessAt)
           && IsTransientRequestFailure(status.LastError ?? status.Status);

    private static bool IsTransientSinaFundRuntimeLogWithCache(
        string text,
        string source,
        IReadOnlyDictionary<string, MarketSourceStatusRecord> statusesBySource)
        => string.Equals(source, "SINA_FUND", StringComparison.OrdinalIgnoreCase)
           && IsTransientRequestFailure(text)
           && statusesBySource.TryGetValue(source, out MarketSourceStatusRecord? status)
           && status.FailureCount < 3
           && !string.IsNullOrWhiteSpace(status.LastSuccessAt);

    private static bool IsTransientRequestFailure(string text)
        => ContainsAny(
            text,
            "HttpClient.Timeout",
            "The request was canceled",
            "The operation was canceled",
            "Unable to read data from the transport connection",
            "\u7531\u4e8e\u7ebf\u7a0b\u9000\u51fa\u6216\u5e94\u7528\u7a0b\u5e8f\u8bf7\u6c42",
            "\u5df2\u4e2d\u6b62 I/O \u64cd\u4f5c");

    private static bool IsSchedulerRateLimitProtection(string text)
    {
        if (!ContainsAny(
                text,
                "scheduler rate_limited",
                "scheduler cooldown",
                "rate_limited",
                "cooldown",
                "请求被全局调度器限频",
                "请求冷却中",
                "预算不足",
                "budget"))
        {
            return false;
        }

        return !ContainsAny(
            text,
            "HTTP 429",
            "HTTP 403",
            "status=429",
            "status=403",
            "http=429",
            "http=403",
            "ResponseEnded",
            "RemoteDisconnected",
            "curl_exit=56",
            "server closed abruptly",
            "invalid JSON",
            "invalid start",
            "empty klines");
    }

    private static string BuildMarketDedupeKey(string source, string reason)
        => string.Join("|", new[]
        {
            NormalizeDisplay(AlertTypes.MarketRuntime),
            NormalizeDisplay(source),
            NormalizeDisplay(reason)
        });

    private static string NormalizeMarketReason(string source, string? status, string? detail)
    {
        string sourceText = NormalizeDisplay(source);
        string all = $"{sourceText} {status} {detail}";
        if (ContainsAny(all, "EASTMONEY_HISTORY", "EastMoneyHistory", "push2his", "kline", "K-line", "history"))
        {
            return "HISTORY_KLINE_UNAVAILABLE";
        }

        if (ContainsAny(all, "TENCENT", "Tencent", "qt.gtimg"))
        {
            return "TENCENT_REALTIME_UNAVAILABLE";
        }

        if (ContainsAny(all, "SINA", "Sina", "hq.sinajs"))
        {
            return "SINA_FUND_NAV_UNAVAILABLE";
        }

        if (ContainsAny(all, "EASTMONEY_PUSH2", "EastMoneyPush2", "push2.eastmoney"))
        {
            return "EASTMONEY_REALTIME_INDEX_UNAVAILABLE";
        }

        string normalizedStatus = NormalizeDisplay(status);
        return normalizedStatus == "--" ? "MARKET_UNAVAILABLE" : normalizedStatus;
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool IsSevereStrategy(string action, string reason)
    {
        string text = action + " " + reason;
        return text.Contains(ClearCash, StringComparison.Ordinal)
               || text.Contains(ExtremePremium, StringComparison.Ordinal)
               || text.Contains(ProhibitBuild, StringComparison.Ordinal)
               || text.Contains(BaseProtection, StringComparison.Ordinal)
               || text.Contains(FinancialAbnormal, StringComparison.Ordinal);
    }

    private static string BuildStrategyContent(DateTimeOffset createdAt, string strategyCode, StrategyDecisionStateRecord decision, string action, string reason)
    {
        var builder = new StringBuilder();
        builder.Append("\u89e6\u53d1\u65f6\u95f4\uff1a").Append(FormatTime(createdAt)).Append("<br>");
        builder.Append("\u6807\u7684\u4ee3\u7801\uff1a").Append(Html(strategyCode)).Append("<br>");
        builder.Append("\u64cd\u4f5c\u5efa\u8bae\uff1a").Append(Html(action)).Append("<br>");
        builder.Append("\u7b56\u7565\u72b6\u6001\uff1a").Append(Html(reason)).Append("<br>");
        builder.Append("\u5f53\u524d\u4ef7\u683c\uff1a").Append(FormatNullable(decision.SuggestedPrice)).Append("<br>");
        builder.Append("\u6ea2\u4ef7\u7387\uff1a").Append(FormatPercent(decision.Premium)).Append("<br>");
        builder.Append("\u6765\u6e90\uff1astrategy_decision_state");
        return builder.ToString();
    }

    private static string BuildOrderContent(DateTimeOffset createdAt, OrderDraftStateRecord draft, string reason)
        => $"\u89e6\u53d1\u65f6\u95f4\uff1a{FormatTime(createdAt)}<br>\u6807\u7684\u4ee3\u7801\uff1a{Html(draft.StrategyCode)}<br>\u65b9\u5411\uff1a{Html(draft.Side)}<br>\u6765\u6e90\uff1a{Html(draft.Source)}<br>\u91d1\u989d\uff1a{FormatNullable(draft.TargetAmount)}<br>\u539f\u56e0\uff1a{Html(reason)}";

    private static string BuildMarketContent(DateTimeOffset createdAt, MarketSourceStatusRecord status, string detail)
        => $"\u89e6\u53d1\u65f6\u95f4\uff1a{FormatTime(createdAt)}<br>\u6570\u636e\u6e90\uff1a{Html(status.Source)}<br>\u72b6\u6001\uff1a{Html(status.Status)}<br>\u5931\u8d25\u6b21\u6570\uff1a{status.FailureCount}<br>\u539f\u56e0\uff1a{Html(detail)}";

    private static string BuildRuntimeLogContent(DateTimeOffset createdAt, RuntimeLogRecord log)
    {
        var builder = new StringBuilder();
        builder.Append("\u89e6\u53d1\u65f6\u95f4\uff1a").Append(FormatTime(createdAt)).Append("<br>");
        builder.Append("\u6a21\u5757\uff1a").Append(Html(log.Module)).Append("<br>");
        builder.Append("\u7ea7\u522b\uff1a").Append(Html(log.Level)).Append("<br>");
        builder.Append("\u6d88\u606f\uff1a").Append(Html(log.Message));
        if (!string.IsNullOrWhiteSpace(log.Detail))
        {
            builder.Append("<br>\u8be6\u60c5\uff1a").Append(Html(log.Detail));
        }

        builder.Append("<br>\u6765\u6e90\uff1aruntime_log");
        return builder.ToString();
    }

    private static string NormalizeDisplay(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private static string FormatTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatNullable(double? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "--";

    private static string FormatPercent(double? value)
        => value.HasValue ? value.Value.ToString("+0.##%;-0.##%;0%", CultureInfo.InvariantCulture) : "--";

    private static string Html(string? value)
        => NormalizeDisplay(value)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}

public sealed class AlertRuleEvaluationInput
{
    public IReadOnlyList<StrategyDecisionStateRecord> StrategyDecisions { get; init; } = Array.Empty<StrategyDecisionStateRecord>();
    public IReadOnlyList<OrderDraftStateRecord> OrderDrafts { get; init; } = Array.Empty<OrderDraftStateRecord>();
    public IReadOnlyList<OrderDraftLegStateRecord> OrderDraftLegs { get; init; } = Array.Empty<OrderDraftLegStateRecord>();
    public IReadOnlyList<MarketSourceStatusRecord> MarketStatuses { get; init; } = Array.Empty<MarketSourceStatusRecord>();
    public IReadOnlyList<RuntimeLogRecord> RuntimeLogs { get; init; } = Array.Empty<RuntimeLogRecord>();
    public AccountReplayStateRecord? AccountReplayState { get; init; }
}
