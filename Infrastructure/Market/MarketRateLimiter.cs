namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public sealed class MarketRateLimiter
{
    private readonly Dictionary<string, DateTimeOffset> _nextAllowedAt = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string source, TimeSpan minInterval, TimeSpan maxInterval, DateTimeOffset now, Random random)
    {
        if (_nextAllowedAt.TryGetValue(source, out DateTimeOffset nextAllowedAt) && now < nextAllowedAt)
        {
            return false;
        }

        TimeSpan interval = RandomInterval(minInterval, maxInterval, random);
        _nextAllowedAt[source] = now.Add(interval);
        return true;
    }

    public DateTimeOffset? NextAllowedAt(string source)
        => _nextAllowedAt.TryGetValue(source, out DateTimeOffset value) ? value : null;

    private static TimeSpan RandomInterval(TimeSpan minInterval, TimeSpan maxInterval, Random random)
    {
        if (maxInterval <= minInterval)
        {
            return minInterval;
        }

        int minMs = (int)Math.Min(int.MaxValue, minInterval.TotalMilliseconds);
        int maxMs = (int)Math.Min(int.MaxValue, maxInterval.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(random.Next(minMs, maxMs + 1));
    }
}
