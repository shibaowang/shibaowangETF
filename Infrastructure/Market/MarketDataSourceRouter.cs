using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public enum MarketDataPurpose
{
    RealtimeQuote,
    IntradayChart,
    DailyHistory,
    OtcFundNav
}

public enum MarketDataProvider
{
    None,
    Tencent,
    EastMoney,
    Sina
}

public sealed record MarketDataSourceRoute(
    MarketDataPurpose Purpose,
    ChartInstrumentType InstrumentType,
    MarketDataProvider Provider,
    string Source,
    bool IsImplemented,
    bool AllowNetworkRequest,
    string StatusMessage);

public static class MarketDataSourceRouter
{
    public const string EtfIntradayMessage = "ETF分时使用腾讯真实接口";
    public const string EtfDailyMessage = "ETF日K使用腾讯qfq真实接口";

    public static MarketDataSourceRoute Resolve(ChartInstrumentType instrumentType, MarketDataPurpose purpose)
    {
        if (instrumentType == ChartInstrumentType.Index)
        {
            return purpose switch
            {
                MarketDataPurpose.RealtimeQuote => EastMoney(purpose, instrumentType, MarketSources.EastMoney, "指数实时行情使用东方财富"),
                MarketDataPurpose.IntradayChart => EastMoney(purpose, instrumentType, MarketSources.EastMoneyIntraday, "指数分时使用东方财富"),
                MarketDataPurpose.DailyHistory => EastMoney(purpose, instrumentType, MarketSources.EastMoneyHistory, "指数日K使用东方财富"),
                MarketDataPurpose.OtcFundNav => Sina(purpose, instrumentType),
                _ => Unsupported(purpose, instrumentType)
            };
        }

        return purpose switch
        {
            MarketDataPurpose.RealtimeQuote => Tencent(purpose, instrumentType, MarketSources.Tencent, "ETF实时行情使用腾讯"),
            MarketDataPurpose.IntradayChart => Tencent(purpose, instrumentType, MarketSources.TencentIntraday, EtfIntradayMessage),
            MarketDataPurpose.DailyHistory => Tencent(purpose, instrumentType, MarketSources.TencentHistory, EtfDailyMessage),
            MarketDataPurpose.OtcFundNav => Sina(purpose, instrumentType),
            _ => Unsupported(purpose, instrumentType)
        };
    }

    public static bool ShouldUseEastMoneyChartApi(ChartInstrumentType instrumentType, MarketDataPurpose purpose)
    {
        MarketDataSourceRoute route = Resolve(instrumentType, purpose);
        return route.Provider == MarketDataProvider.EastMoney
               && route.IsImplemented
               && route.AllowNetworkRequest;
    }

    public static bool ShouldRefreshEtfHistoryWithEastMoney()
        => false;

    private static MarketDataSourceRoute Tencent(
        MarketDataPurpose purpose,
        ChartInstrumentType instrumentType,
        string source,
        string message)
        => new(purpose, instrumentType, MarketDataProvider.Tencent, source, true, true, message);

    private static MarketDataSourceRoute EastMoney(
        MarketDataPurpose purpose,
        ChartInstrumentType instrumentType,
        string source,
        string message)
        => new(purpose, instrumentType, MarketDataProvider.EastMoney, source, true, true, message);

    private static MarketDataSourceRoute Sina(
        MarketDataPurpose purpose,
        ChartInstrumentType instrumentType)
        => new(purpose, instrumentType, MarketDataProvider.Sina, MarketSources.SinaFund, true, true, "场外基金净值使用新浪");

    private static MarketDataSourceRoute Unsupported(
        MarketDataPurpose purpose,
        ChartInstrumentType instrumentType)
        => new(purpose, instrumentType, MarketDataProvider.None, string.Empty, false, false, "行情源未配置");
}
