namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record ChartHistoryDepthInfo(
    int DailyCount,
    int WeeklyCount,
    int MonthlyCount,
    DateTime? EarliestDate,
    DateTime? LatestDate,
    bool IsSufficientForDaily,
    bool IsSufficientForWeekly,
    bool IsSufficientForMonthly,
    string Reason);

public sealed record ChartHistoryDepthCheckpoint(
    string Source,
    int DailyCount,
    DateTime? EarliestDate,
    DateTime? LatestDate,
    bool SourceExhausted,
    DateTimeOffset CheckedAt);

public sealed record ChartHistoryReplacementDecision(
    bool ShouldReplace,
    string Reason);
