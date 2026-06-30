using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class EastMoneyHistoryParserTests
{
    [Fact]
    public void ParseHigh_UsesF54HighField()
    {
        const string json = """
            {"data":{"klines":["2026-01-31,10.00,11.00,12.50,9.80,100,1000,0,0,0,0","2026-02-28,11.00,10.50,13.75,10.10,100,1000,0,0,0,0"]}}
            """;

        Assert.Equal(13.75, EastMoneyHistoryParser.ParseHigh(json));
    }

    [Fact]
    public void CalculateLatestDrawdown_UsesRollingHighAndClose()
    {
        const string json = """
            {"data":{"klines":["2026-01-31,10.00,11.00,12.50,9.80,100,1000,0,0,0,0","2026-02-28,11.00,10.50,13.75,10.10,100,1000,0,0,0,0","2026-03-31,10.40,12.375,13.00,10.20,100,1000,0,0,0,0"]}}
            """;

        var points = EastMoneyHistoryParser.ParsePoints(json);
        double? latestDrawdown = EastMoneyHistoryParser.CalculateLatestDrawdown(points);

        Assert.NotNull(latestDrawdown);
        Assert.Equal(-0.1, latestDrawdown.Value, 6);
    }

    [Fact]
    public void ParsePoints_AllowsJsonpWrapper()
    {
        const string jsonp = """
            jQuery351({"data":{"klines":["2026-01-31,10.00,11.00,12.50,9.80,100,1000,0,0,0,0"]}});
            """;

        var point = Assert.Single(EastMoneyHistoryParser.ParsePoints(jsonp));

        Assert.Equal(12.5, point.High);
    }
}
