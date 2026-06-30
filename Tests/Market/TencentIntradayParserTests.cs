using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class TencentIntradayParserTests
{
    [Fact]
    public void ParsePoints_ConvertsTencentCumulativeVolumeToMinuteVolume()
    {
        string payload = """
            {
              "code": 0,
              "data": {
                "sz159941": {
                  "data": {
                    "date": "20260622",
                    "data": [
                      "0930 1.620 158339 25650918.00",
                      "0931 1.621 628379 101805672.50",
                      "0932 1.622 700000 113000000.00"
                    ]
                  }
                }
              }
            }
            """;

        var points = TencentIntradayParser.ParsePoints(payload);

        Assert.Equal(3, points.Count);
        Assert.Equal(new DateTime(2026, 6, 22, 9, 30, 0), points[0].Time);
        Assert.Equal(1.620, points[0].Price, 3);
        Assert.Equal(158339, points[0].Volume);
        Assert.Equal(470040, points[1].Volume);
        Assert.Equal(76154754.50, points[1].Amount);
    }

    [Fact]
    public void ParsePoints_RejectsQuoteOnlyPayload()
    {
        string payload = """{"code":0,"data":{"sz159941":{"qt":{}}}}""";

        var points = TencentIntradayParser.ParsePoints(payload);

        Assert.Empty(points);
    }
}
