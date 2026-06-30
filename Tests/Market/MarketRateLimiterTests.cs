using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class MarketRateLimiterTests
{
    [Fact]
    public void SameSource_IsBlockedUntilIntervalPasses()
    {
        var limiter = new MarketRateLimiter();
        var random = new Random(1);
        DateTimeOffset start = new(2026, 6, 14, 10, 0, 0, TimeSpan.Zero);

        Assert.True(limiter.TryAcquire("TENCENT_QT", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(6), start, random));
        Assert.False(limiter.TryAcquire("TENCENT_QT", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(6), start.AddSeconds(5), random));
        Assert.True(limiter.TryAcquire("TENCENT_QT", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(6), start.AddSeconds(6), random));
    }
}
