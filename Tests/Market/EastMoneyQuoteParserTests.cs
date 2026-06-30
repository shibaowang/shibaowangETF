using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class EastMoneyQuoteParserTests
{
    [Fact]
    public void Parse_ScalesIndexQuoteOhlcFields()
    {
        const string json = """
            {"data":{"diff":[{"f2":286503,"f3":41,"f12":"NDXTMC","f15":291050,"f16":280853,"f17":291050,"f18":285345}]}}
            """;
        var requested = new Dictionary<string, MarketWatchItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["251.NDXTMC"] = new MarketWatchItem
            {
                Symbol = "251.NDXTMC",
                DisplayName = "纳指科技指数",
                MarketType = "INDEX",
                RawCode = "251.NDXTMC"
            }
        };

        var record = Assert.Single(EastMoneyQuoteParser.Parse(
            json,
            requested,
            DateTimeOffset.Parse("2026-06-26T00:10:00+08:00")));

        Assert.Equal(2865.03, record.Price);
        Assert.Equal(2910.50, record.HighValue);
        Assert.Equal(2808.53, record.LowValue);
        Assert.Equal(2910.50, record.OpenValue);
        Assert.Equal(2853.45, record.LastClose);
        Assert.Equal(0.0041, record.ChangePercent);
    }
}
