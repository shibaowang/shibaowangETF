using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class IndexDrawdownQuoteRefreshHelperTests
{
    [Fact]
    public void HasQuoteChanged_ReturnsTrueWhenLatestIndexQuotePriceChanges()
    {
        MarketQuoteRecord[] previous =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29400.12, "2026-06-29 21:30:00")
        };
        MarketQuoteRecord[] current =
        {
            Quote("251.NDXTMC", 2846.58, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29400.12, "2026-06-29 21:30:00")
        };

        Assert.True(IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(previous, current));
    }

    [Fact]
    public void HasQuoteChanged_ReturnsTrueWhenLatestIndexQuoteTimeChanges()
    {
        MarketQuoteRecord[] previous =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00")
        };
        MarketQuoteRecord[] current =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:31:00")
        };

        Assert.True(IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(previous, current));
    }

    [Fact]
    public void HasQuoteChanged_ReturnsFalseWhenIndexQuotesAreUnchanged()
    {
        MarketQuoteRecord[] previous =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29400.12, "2026-06-29 21:30:00")
        };
        MarketQuoteRecord[] current =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29400.12, "2026-06-29 21:30:00")
        };

        Assert.False(IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(previous, current));
    }

    [Fact]
    public void HasQuoteChanged_TracksTwoIndexSymbolsIndependently()
    {
        MarketQuoteRecord[] previous =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29400.12, "2026-06-29 21:30:00")
        };
        MarketQuoteRecord[] current =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("100.NDX100", 29510.88, "2026-06-29 21:30:00")
        };

        Assert.True(IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(previous, current));
    }

    [Fact]
    public void HasQuoteChanged_IgnoresEtfQuotes()
    {
        MarketQuoteRecord[] previous =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("159941", 1.66, "2026-06-29 14:00:00", "ETF")
        };
        MarketQuoteRecord[] current =
        {
            Quote("251.NDXTMC", 2824.76, "2026-06-29 21:30:00"),
            Quote("159941", 1.68, "2026-06-29 14:01:00", "ETF")
        };

        Assert.False(IndexDrawdownQuoteRefreshHelper.HasQuoteChanged(previous, current));
    }

    private static MarketQuoteRecord Quote(string symbol, double price, string quoteTime, string marketType = "INDEX")
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = "TEST",
            Price = price,
            QuoteTime = quoteTime,
            ReceivedAt = quoteTime
        };
}
