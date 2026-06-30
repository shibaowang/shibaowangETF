using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class MarketCircuitBreakerTests
{
    [Fact]
    public void ConsecutiveFailures_EnterCooldownAndSuccessResets()
    {
        var breaker = new MarketCircuitBreaker(2, TimeSpan.FromMinutes(10));
        DateTimeOffset start = new(2026, 6, 14, 10, 0, 0, TimeSpan.Zero);

        Assert.True(breaker.CanRequest(start));
        Assert.Null(breaker.RecordFailure("first", start));
        Assert.True(breaker.CanRequest(start.AddMinutes(1)));
        Assert.NotNull(breaker.RecordFailure("second", start.AddMinutes(1)));
        Assert.False(breaker.CanRequest(start.AddMinutes(2)));
        Assert.True(breaker.CanRequest(start.AddMinutes(12)));

        breaker.RecordSuccess();

        Assert.Equal(0, breaker.FailureCount);
        Assert.Null(breaker.CooldownUntil);
        Assert.True(breaker.CanRequest(start.AddMinutes(12)));
    }
}
