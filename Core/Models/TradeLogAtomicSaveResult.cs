namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed record TradeLogAtomicSaveResult(
    bool Committed,
    AccountReplayResult ReplayResult,
    IReadOnlyList<PersistedTradeLogIdentity> Identities);

public sealed record PersistedTradeLogIdentity(
    int SnapshotIndex,
    long OriginalId,
    long PersistedId);

public sealed class TradeLogFinancialReplayException : InvalidOperationException
{
    public TradeLogFinancialReplayException(string? replayError)
        : base(string.IsNullOrWhiteSpace(replayError)
            ? "账户回放检测到财务异常。"
            : $"账户回放检测到财务异常：{replayError}")
    {
        ReplayError = replayError;
    }

    public string? ReplayError { get; }
}
