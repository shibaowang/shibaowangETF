namespace CrossETF.Terminal.UiShell.Reference;

[Flags]
public enum MainWindowDirtyFlags
{
    None = 0,
    TopQuotes = 1 << 0,
    Account = 1 << 1,
    EtfTable = 1 << 2,
    TradeLog = 1 << 3,
    OrderDraft = 1 << 4,
    Sparklines = 1 << 5,
    Drawdown = 1 << 6,
    Ring = 1 << 7,
    Pool = 1 << 8,
    Runtime = 1 << 9,
    Otc = 1 << 10,
    All = TopQuotes | Account | EtfTable | TradeLog | OrderDraft | Sparklines
          | Drawdown | Ring | Pool | Runtime | Otc
}

public sealed class MainWindowUiRefreshCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _surfaceSignatures = new(StringComparer.Ordinal);
    private MainWindowDirtyFlags _pendingFlags;
    private bool _dispatchQueued;

    public long RefreshCycleCount { get; private set; }
    public long SurfaceUpdateAttemptCount { get; private set; }
    public long SurfaceRenderCount { get; private set; }
    public long SkippedUnchangedSurfaceCount { get; private set; }

    public bool Request(MainWindowDirtyFlags flags)
    {
        lock (_gate)
        {
            _pendingFlags |= flags;
            if (_dispatchQueued)
            {
                return false;
            }

            _dispatchQueued = true;
            return true;
        }
    }

    public MainWindowDirtyFlags TakePending()
    {
        lock (_gate)
        {
            MainWindowDirtyFlags flags = _pendingFlags;
            _pendingFlags = MainWindowDirtyFlags.None;
            _dispatchQueued = false;
            RefreshCycleCount++;
            return flags;
        }
    }

    public bool ShouldRender(string surface, string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        signature ??= string.Empty;

        lock (_gate)
        {
            SurfaceUpdateAttemptCount++;
            if (_surfaceSignatures.TryGetValue(surface, out string? previous)
                && string.Equals(previous, signature, StringComparison.Ordinal))
            {
                SkippedUnchangedSurfaceCount++;
                return false;
            }

            _surfaceSignatures[surface] = signature;
            SurfaceRenderCount++;
            return true;
        }
    }

    public void Invalidate(string surface)
    {
        lock (_gate)
        {
            _surfaceSignatures.Remove(surface);
        }
    }
}
