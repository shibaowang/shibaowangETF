using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public interface IChartMarketDataClient : IDisposable
{
    Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken);

    Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken);

    Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
        string secId,
        bool isEtf,
        bool preferDaily,
        CancellationToken cancellationToken);

    Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken);
}
