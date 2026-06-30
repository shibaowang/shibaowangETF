using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class MarketRuntimeStatusEvaluatorTests
{
    [Fact]
    public void RealtimeOkAndHistoryErrorWithValidCoreCache_IsConnected()
    {
        MarketRuntimeStatusEvaluation result = MarketRuntimeStatusEvaluator.Evaluate(
            new[]
            {
                Ok(MarketSources.Tencent),
                Ok(MarketSources.EastMoney),
                Ok(MarketSources.SinaFund),
                Error(MarketSources.EastMoneyHistory)
            },
            localConfigured: true,
            hasValidCoreHistoryCache: true);

        Assert.Equal(MarketRuntimeConnectionState.Connected, result.State);
        Assert.True(result.HistoryFailureIgnoredByValidCache);
    }

    [Fact]
    public void RealtimeOkAndHistoryErrorWithoutValidCoreCache_IsPartial()
    {
        MarketRuntimeStatusEvaluation result = MarketRuntimeStatusEvaluator.Evaluate(
            new[]
            {
                Ok(MarketSources.Tencent),
                Ok(MarketSources.EastMoney),
                Ok(MarketSources.SinaFund),
                Error(MarketSources.EastMoneyHistory)
            },
            localConfigured: true,
            hasValidCoreHistoryCache: false);

        Assert.Equal(MarketRuntimeConnectionState.Partial, result.State);
        Assert.False(result.HistoryFailureIgnoredByValidCache);
    }

    private static MarketSourceStatusRecord Ok(string source)
        => new()
        {
            Source = source,
            Status = "OK",
            LastSuccessAt = "2026-06-16 09:30:00",
            UpdatedAt = "2026-06-16 09:30:00"
        };

    private static MarketSourceStatusRecord Error(string source)
        => new()
        {
            Source = source,
            Status = "ERROR",
            LastFailureAt = "2026-06-16 09:31:00",
            LastError = "The response ended prematurely",
            UpdatedAt = "2026-06-16 09:31:00"
        };
}
