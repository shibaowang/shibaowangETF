namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public sealed class MarketCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;

    public MarketCircuitBreaker(int failureThreshold = 3, TimeSpan? cooldown = null)
    {
        _failureThreshold = Math.Max(1, failureThreshold);
        _cooldown = cooldown ?? TimeSpan.FromMinutes(10);
    }

    public int FailureCount { get; private set; }
    public DateTimeOffset? CooldownUntil { get; private set; }
    public string? LastError { get; private set; }

    public bool CanRequest(DateTimeOffset now)
        => CooldownUntil is null || now >= CooldownUntil.Value;

    public void RecordSuccess()
    {
        FailureCount = 0;
        CooldownUntil = null;
        LastError = null;
    }

    public DateTimeOffset? RecordFailure(string error, DateTimeOffset now)
    {
        FailureCount++;
        LastError = error;
        if (FailureCount >= _failureThreshold)
        {
            CooldownUntil = now.Add(_cooldown);
        }

        return CooldownUntil;
    }
}
