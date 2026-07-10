namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record NaturalDayPnlEvaluationItem(
    string StrategyCode,
    string ActualCode,
    string InstrumentName,
    string Source,
    string MarketType,
    double Quantity,
    double? CandidateDailyPnl,
    string? QuoteTime,
    string? ReceivedAt,
    string? CalculatedAt,
    bool IncludedToday,
    double? IncludedAmount,
    string ReasonCode,
    string ReasonText);

public sealed record MarketDiagnosticsSnapshot(
    DiagnosticsOverview Overview,
    IReadOnlyList<DiagnosticsMarketRow> MarketRows,
    IReadOnlyList<NaturalDayPnlEvaluationItem> PnlItems,
    DiagnosticsPnlSummary PnlSummary,
    IReadOnlyList<DiagnosticsRuntimeLogRow> RuntimeLogs,
    DiagnosticsEnvironmentInfo Environment);

public sealed record DiagnosticsOverview(
    string OverallStatus,
    int NormalSourceCount,
    int AbnormalSourceCount,
    int StaleQuoteCount,
    int RecentErrorCount,
    int RecentWarningCount,
    int ProcessErrorCount,
    int HistoricalRuntimeLogCount,
    int IncludedPnlItemCount,
    double? IncludedPnlTotal,
    string AppVersion,
    string DatabaseStatus);

public sealed record DiagnosticsMarketRow(
    string Source,
    string Code,
    string Name,
    string MarketType,
    double? Price,
    double? LastClose,
    string? QuoteTime,
    string? ReceivedAt,
    string Age,
    string QuoteStatus,
    string SourceStatus,
    int FailureCount,
    string? CooldownUntil,
    string? LastError);

public sealed record DiagnosticsPnlSummary(
    double? DiagnosticsIncludedTotal,
    double? MainWindowPathTotal,
    string ConsistencyStatus);

public sealed record DiagnosticsRuntimeLogRow(
    string Time,
    string Level,
    string Source,
    string Message,
    string ExceptionSummary);

public sealed record DiagnosticsEnvironmentInfo(
    string AppVersion,
    string AssemblyInformationalVersion,
    string ExePath,
    string DatabasePath,
    string CurrentTime,
    string ProcessStartTime,
    string ProcessUptime,
    string RefreshInterval,
    string DotNetVersion,
    string OperatingSystem,
    string ProcessArchitecture);
