using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class TencentHistoryParserTests
{
    [Fact]
    public void ParsePoints_MapsTencentQfqDayFields()
    {
        string payload = """
            {
              "code": 0,
              "data": {
                "sz159941": {
                  "qfqday": [
                    ["2025-02-28","1.151","1.134","1.153","1.133","17168177.000"]
                  ]
                }
              }
            }
            """;

        var points = TencentHistoryParser.ParsePoints(payload);

        Assert.Single(points);
        Assert.Equal(new DateTime(2025, 2, 28), points[0].Date);
        Assert.Equal(1.151, points[0].Open, 3);
        Assert.Equal(1.134, points[0].Close, 3);
        Assert.Equal(1.153, points[0].High, 3);
        Assert.Equal(1.133, points[0].Low, 3);
        Assert.Equal(17168177, points[0].Volume);
        Assert.Null(points[0].Amount);
    }

    [Fact]
    public void ToEastMoneyCompatiblePayload_CanBeClassifiedAsDailyLike()
    {
        string payload = BuildTencentQfqPayload(220);

        string normalized = TencentHistoryParser.ToEastMoneyCompatiblePayload(payload);
        MarketHistoryQualityInfo quality = MarketHistoryQuality.Analyze(normalized);

        Assert.Equal(MarketHistoryFrequency.DailyLike, quality.Frequency);
        Assert.Equal(220, quality.KLineCount);
    }

    [Fact]
    public void ParsePoints_FallsBackToDayKeyWhenQfqDayIsMissing()
    {
        const string payload = """
            {
              "code": 0,
              "data": {
                "sz159509": {
                  "day": [
                    ["2026-06-25","2.650","2.669","2.700","2.620","123456.000"]
                  ]
                }
              }
            }
            """;

        var points = TencentHistoryParser.ParsePoints(payload);
        string normalized = TencentHistoryParser.ToEastMoneyCompatiblePayload(payload);

        Assert.Single(points);
        Assert.Equal(new DateTime(2026, 6, 25), points[0].Date);
        Assert.Equal(2.650, points[0].Open, 3);
        Assert.Equal(2.669, points[0].Close, 3);
        Assert.Equal(2.700, points[0].High, 3);
        Assert.Equal(2.620, points[0].Low, 3);
        Assert.Equal(123456, points[0].Volume);
        Assert.Contains("2026-06-25,2.65,2.669,2.7,2.62,123456,", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePoints_RejectsBadParamsPayload()
    {
        string payload = """{"code":1,"msg":"bad params"}""";

        var points = TencentHistoryParser.ParsePoints(payload);
        string normalized = TencentHistoryParser.ToEastMoneyCompatiblePayload(payload);

        Assert.Empty(points);
        Assert.Equal(MarketHistoryFrequency.Invalid, MarketHistoryQuality.Analyze(normalized).Frequency);
    }

    private static string BuildTencentQfqPayload(int count)
    {
        string[] rows = Enumerable.Range(0, count)
            .Select(index =>
            {
                DateTime date = new DateTime(2025, 1, 1).AddDays(index);
                double close = 1.0 + index * 0.001;
                return $"""["{date:yyyy-MM-dd}","{close:0.000}","{close + 0.001:0.000}","{close + 0.002:0.000}","{close - 0.002:0.000}","1000"]""";
            })
            .ToArray();

        return "{\"code\":0,\"data\":{\"sz159941\":{\"qfqday\":[" + string.Join(",", rows) + "]}}}";
    }
}
