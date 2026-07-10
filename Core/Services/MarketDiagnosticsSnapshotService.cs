using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class MarketDiagnosticsSnapshotService
{
    private static readonly TimeSpan DelayedQuoteAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StaleQuoteAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan RecentLogWindow = TimeSpan.FromMinutes(30);

    private readonly LocalDataRepository _repository;
    private readonly Func<DateTime> _nowProvider;
    private readonly Func<DateTime> _processStartProvider;

    public MarketDiagnosticsSnapshotService(
        LocalDataRepository repository,
        Func<DateTime>? nowProvider = null,
        Func<DateTime>? processStartProvider = null)
    {
        _repository = repository;
        _nowProvider = nowProvider ?? (() => DateTime.Now);
        _processStartProvider = processStartProvider ?? ResolveCurrentProcessStartTime;
    }

    public MarketDiagnosticsSnapshot BuildSnapshot()
    {
        DateTime now = _nowProvider();
        DateTime processStartTime = ResolveProcessStartTime(now);
        DiagnosticsEnvironmentInfo environment = BuildEnvironmentInfo(now, processStartTime);

        try
        {
            IReadOnlyList<MarketQuoteRecord> quotes = _repository.ReadMarketQuoteCache();
            IReadOnlyList<MarketSourceStatusRecord> sourceStatuses = _repository.ReadMarketSourceStatuses();
            IReadOnlyList<RuntimeLogRecord> runtimeLogs = _repository.ReadRecentRuntimeLogs(500);
            IReadOnlyList<PositionReplayStateRecord> replayPositions = _repository.ReadPositionReplayStates();
            IReadOnlyList<OtcPositionReplayStateRecord> otcPositions = _repository.ReadOtcPositionReplayStates();

            IReadOnlyList<NaturalDayPnlEvaluationItem> pnlItems = EtfDecisionTableMetrics.EvaluateNaturalDayValuationItems(
                replayPositions,
                otcPositions,
                quotes,
                now);
            double? includedTotal = SumIncludedPnl(pnlItems);
            double? mainWindowPathTotal = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
                replayPositions,
                otcPositions,
                quotes,
                now);

            IReadOnlyList<DiagnosticsMarketRow> marketRows = BuildMarketRows(quotes, sourceStatuses, now);
            IReadOnlyList<DiagnosticsRuntimeLogRow> warningRows = runtimeLogs
                .Where(IsWarningOrError)
                .Take(100)
                .Select(ToRuntimeRow)
                .ToArray();
            DiagnosticsPnlSummary pnlSummary = new(
                includedTotal,
                mainWindowPathTotal,
                SameNullableAmount(includedTotal, mainWindowPathTotal) ? "一致" : "不一致");

            DateTime recentCutoff = now - RecentLogWindow;
            int recentErrorCount = runtimeLogs.Count(record => IsError(record) && IsWithinRange(record.Time, recentCutoff, now));
            int recentWarningCount = runtimeLogs.Count(record => IsWarning(record) && IsWithinRange(record.Time, recentCutoff, now));
            int processErrorCount = runtimeLogs.Count(record => IsError(record) && IsWithinRange(record.Time, processStartTime, now));
            int staleQuoteCount = marketRows.Count(row => string.Equals(row.QuoteStatus, "过期", StringComparison.Ordinal));
            int normalSourceCount = sourceStatuses.Count(IsNormalSource);
            int abnormalSourceCount = sourceStatuses.Count(status => !IsNormalSource(status));
            string databaseStatus = File.Exists(_repository.DatabasePath) ? "正常" : "未找到数据库文件";

            DiagnosticsOverview overview = new(
                BuildOverallStatus(
                    sourceStatuses,
                    staleQuoteCount,
                    recentErrorCount,
                    recentWarningCount,
                    pnlSummary,
                    databaseStatus),
                normalSourceCount,
                abnormalSourceCount,
                staleQuoteCount,
                recentErrorCount,
                recentWarningCount,
                processErrorCount,
                runtimeLogs.Count,
                pnlItems.Count(item => item.IncludedToday),
                includedTotal,
                environment.AppVersion,
                databaseStatus);

            return new MarketDiagnosticsSnapshot(
                overview,
                marketRows,
                pnlItems,
                pnlSummary,
                warningRows,
                environment);
        }
        catch (Exception ex)
        {
            string databaseStatus = $"读取失败：{ex.GetType().Name}";
            var failureLog = new DiagnosticsRuntimeLogRow(
                FormatTime(now),
                "ERROR",
                nameof(MarketDiagnosticsSnapshotService),
                "读取本地诊断数据失败",
                Truncate(ex.Message, 220));
            var overview = new DiagnosticsOverview(
                "异常",
                0,
                0,
                0,
                1,
                0,
                1,
                0,
                0,
                null,
                environment.AppVersion,
                databaseStatus);

            return new MarketDiagnosticsSnapshot(
                overview,
                Array.Empty<DiagnosticsMarketRow>(),
                Array.Empty<NaturalDayPnlEvaluationItem>(),
                new DiagnosticsPnlSummary(null, null, "无法校验"),
                new[] { failureLog },
                environment);
        }
    }

    private IReadOnlyList<DiagnosticsMarketRow> BuildMarketRows(
        IReadOnlyList<MarketQuoteRecord> quotes,
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        DateTime now)
    {
        var sourceByName = sourceStatuses
            .GroupBy(status => status.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return quotes
            .OrderBy(quote => quote.MarketType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(quote =>
            {
                sourceByName.TryGetValue(quote.Source, out MarketSourceStatusRecord? sourceStatus);
                DateTime? effectiveTime = ParseTime(quote.ReceivedAt) ?? ParseTime(quote.QuoteTime);
                TimeSpan? age = effectiveTime.HasValue ? now - effectiveTime.Value : null;
                string quoteStatus = ResolveQuoteStatus(quote, sourceStatus, age);
                return new DiagnosticsMarketRow(
                    quote.Source,
                    quote.Symbol,
                    string.IsNullOrWhiteSpace(quote.DisplayName) ? quote.Symbol : quote.DisplayName!,
                    quote.MarketType,
                    quote.Price,
                    quote.LastClose,
                    quote.QuoteTime,
                    quote.ReceivedAt,
                    FormatAge(age),
                    quoteStatus,
                    sourceStatus?.Status ?? "无数据",
                    sourceStatus?.FailureCount ?? 0,
                    sourceStatus?.CooldownUntil,
                    sourceStatus?.LastError);
            })
            .ToArray();
    }

    private DiagnosticsEnvironmentInfo BuildEnvironmentInfo(DateTime now, DateTime processStartTime)
    {
        Assembly assembly = typeof(MarketDiagnosticsSnapshotService).Assembly;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "--";
        string displayVersion = ResolveDisplayVersion(informationalVersion, assembly.GetName().Version);

        return new DiagnosticsEnvironmentInfo(
            "V" + displayVersion,
            informationalVersion,
            Environment.ProcessPath ?? assembly.Location,
            _repository.DatabasePath,
            FormatTime(now),
            FormatTime(processStartTime),
            FormatAge(now - processStartTime),
            "随机 2-4 秒自动刷新",
            Environment.Version.ToString(),
            Environment.OSVersion.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    private DateTime ResolveProcessStartTime(DateTime now)
    {
        try
        {
            DateTime processStartTime = _processStartProvider();
            return processStartTime <= now ? processStartTime : now;
        }
        catch
        {
            return now;
        }
    }

    private static DateTime ResolveCurrentProcessStartTime()
    {
        using Process process = Process.GetCurrentProcess();
        return process.StartTime;
    }

    private static DiagnosticsRuntimeLogRow ToRuntimeRow(RuntimeLogRecord record)
        => new(
            record.Time,
            record.Level,
            record.Module,
            record.Message,
            string.IsNullOrWhiteSpace(record.Detail) ? "--" : Truncate(record.Detail!, 220));

    private static bool IsWarningOrError(RuntimeLogRecord record)
        => IsWarning(record) || IsError(record);

    private static bool IsWarning(RuntimeLogRecord record)
        => string.Equals(record.Level, "WARN", StringComparison.OrdinalIgnoreCase)
           || string.Equals(record.Level, "WARNING", StringComparison.OrdinalIgnoreCase);

    private static bool IsError(RuntimeLogRecord record)
        => string.Equals(record.Level, "ERROR", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRange(string value, DateTime startInclusive, DateTime endInclusive)
    {
        DateTime? parsed = ParseTime(value);
        return parsed.HasValue && parsed.Value >= startInclusive && parsed.Value <= endInclusive;
    }

    private static string ResolveQuoteStatus(
        MarketQuoteRecord quote,
        MarketSourceStatusRecord? sourceStatus,
        TimeSpan? age)
    {
        if (quote.Price is null)
        {
            return "无数据";
        }

        if (sourceStatus is not null && !IsNormalSource(sourceStatus))
        {
            return "错误";
        }

        if (!age.HasValue)
        {
            return "无数据";
        }

        if (age.Value > StaleQuoteAge)
        {
            return "过期";
        }

        return age.Value > DelayedQuoteAge ? "延迟" : "正常";
    }

    private static bool IsNormalSource(MarketSourceStatusRecord status)
        => string.Equals(status.Status, "OK", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "正常", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "CONNECTED", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorSource(MarketSourceStatusRecord status)
        => string.Equals(status.Status, "ERROR", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status.Status, "DOWN", StringComparison.OrdinalIgnoreCase);

    private static string BuildOverallStatus(
        IReadOnlyList<MarketSourceStatusRecord> sourceStatuses,
        int staleQuoteCount,
        int recentErrorCount,
        int recentWarningCount,
        DiagnosticsPnlSummary pnlSummary,
        string databaseStatus)
    {
        if (!string.Equals(databaseStatus, "正常", StringComparison.Ordinal)
            || !string.Equals(pnlSummary.ConsistencyStatus, "一致", StringComparison.Ordinal)
            || sourceStatuses.Any(IsErrorSource))
        {
            return "异常";
        }

        return sourceStatuses.Any(status => !IsNormalSource(status))
               || staleQuoteCount > 0
               || recentErrorCount > 0
               || recentWarningCount > 0
            ? "警告"
            : "正常";
    }

    private static double? SumIncludedPnl(IReadOnlyList<NaturalDayPnlEvaluationItem> items)
    {
        NaturalDayPnlEvaluationItem[] included = items
            .Where(item => item.IncludedToday && HasFiniteValue(item.IncludedAmount))
            .ToArray();
        return included.Length == 0 ? null : included.Sum(item => item.IncludedAmount!.Value);
    }

    private static bool SameNullableAmount(double? left, double? right)
        => (!left.HasValue && !right.HasValue)
           || (left.HasValue && right.HasValue && Math.Abs(left.Value - right.Value) < 0.000001);

    private static DateTime? ParseTime(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : DateTime.TryParse(value, out parsed) ? parsed : null;

    private static string ResolveDisplayVersion(string informationalVersion, Version? assemblyVersion)
    {
        string version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assemblyVersion?.ToString(3) ?? "0.0.0"
            : informationalVersion;
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        int prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);
        return prereleaseIndex >= 0 ? version[..prereleaseIndex] : version;
    }

    private static string FormatTime(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatAge(TimeSpan? age)
        => age.HasValue ? FormatAge(age.Value) : "--";

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalDays >= 1)
        {
            return $"{(int)age.TotalDays}天 {age.Hours}小时";
        }

        if (age.TotalHours >= 1)
        {
            return $"{(int)age.TotalHours}小时 {age.Minutes}分钟";
        }

        return age.TotalMinutes >= 1
            ? $"{(int)age.TotalMinutes}分钟"
            : $"{Math.Max(0, (int)age.TotalSeconds)}秒";
    }

    private static bool HasFiniteValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
