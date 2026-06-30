using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public interface IChartIntradayCacheStore
{
    ChartIntradayCacheEntry? ReadLatestChartIntradayCache(string strategyCode);

    void SaveChartIntradayCache(
        string strategyCode,
        string? actualCode,
        string rawPayload,
        DateTimeOffset fetchedAt,
        string source = "EASTMONEY_INTRADAY",
        string quality = "REAL_TRENDS2");
}
