using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class MarketDataSourceRouterTests
{
    [Theory]
    [InlineData("159941")]
    [InlineData("513100")]
    public void EtfRealtimeQuote_UsesTencentFirst(string _)
    {
        MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(
            ChartInstrumentType.Etf,
            MarketDataPurpose.RealtimeQuote);

        Assert.Equal(MarketDataProvider.Tencent, route.Provider);
        Assert.Equal(MarketSources.Tencent, route.Source);
        Assert.True(route.AllowNetworkRequest);
    }

    [Theory]
    [InlineData(MarketDataPurpose.RealtimeQuote, MarketSources.EastMoney)]
    [InlineData(MarketDataPurpose.IntradayChart, MarketSources.EastMoneyIntraday)]
    [InlineData(MarketDataPurpose.DailyHistory, MarketSources.EastMoneyHistory)]
    public void IndexRoutes_KeepEastMoney(MarketDataPurpose purpose, string source)
    {
        MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(ChartInstrumentType.Index, purpose);

        Assert.Equal(MarketDataProvider.EastMoney, route.Provider);
        Assert.Equal(source, route.Source);
        Assert.True(route.AllowNetworkRequest);
    }

    [Theory]
    [InlineData(MarketDataPurpose.IntradayChart, MarketSources.TencentIntraday)]
    [InlineData(MarketDataPurpose.DailyHistory, MarketSources.TencentHistory)]
    public void EtfChartRoutes_UseTencentRealApi(
        MarketDataPurpose purpose,
        string source)
    {
        MarketDataSourceRoute route = MarketDataSourceRouter.Resolve(ChartInstrumentType.Etf, purpose);

        Assert.Equal(MarketDataProvider.Tencent, route.Provider);
        Assert.Equal(source, route.Source);
        Assert.True(route.IsImplemented);
        Assert.True(route.AllowNetworkRequest);
        Assert.False(MarketDataSourceRouter.ShouldUseEastMoneyChartApi(ChartInstrumentType.Etf, purpose));
    }

    [Fact]
    public void OfficialPath_DoesNotAllowSystemProxyFallbackForEtfHistory()
    {
        Assert.False(MarketDataSourceRouter.ShouldRefreshEtfHistoryWithEastMoney());
    }
}
