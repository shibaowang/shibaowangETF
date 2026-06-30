using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public sealed class SecurityChartServiceTests
{
    [Fact]
    public void CreateSecurityInfo_ParsesStrategyCodeAndEastMoneySecId()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");

        Assert.Equal("159941", info.StrategyCode);
        Assert.Equal("159941", info.ActualCode);
        Assert.Equal("0.159941", info.EastMoneySecId);
        Assert.Equal("纳指ETF广发", info.Name);
        Assert.Equal(ChartInstrumentType.Etf, info.InstrumentType);
    }

    [Fact]
    public void CreateIndexSecurityInfo_UsesIndexSymbolAndEastMoneySecId()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");

        Assert.Equal("251.NDXTMC", info.StrategyCode);
        Assert.Equal("251.NDXTMC", info.ActualCode);
        Assert.Equal("251.NDXTMC", info.EastMoneySecId);
        Assert.Equal("纳指科技指数", info.Name);
        Assert.Equal(ChartInstrumentType.Index, info.InstrumentType);
    }

    [Fact]
    public void Subscribe_ReplacesSameSymbolAndDoesNotDuplicate()
    {
        var service = new ChartSubscriptionService();
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");

        service.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        service.Subscribe(info, SecurityChartPeriod.Daily, SecurityChartSubPanel.Macd);

        Assert.Equal(1, service.ActiveSymbolCount);
        ChartSubscription subscription = Assert.Single(service.ActiveSubscriptions);
        Assert.Equal(SecurityChartPeriod.Daily, subscription.Period);
        Assert.Equal(SecurityChartSubPanel.Macd, subscription.SubPanel);
    }

    [Fact]
    public void EastMoneyIntradayParser_UsesRealTrendFields()
    {
        const string json = """
            {"data":{"trends":["2026-06-18 10:31,1.689,1.684,1.691,1.683,340307,57425767.000,1.6875","2026-06-18 10:32,1.684,1.687,1.687,1.684,250214,42181464.000,1.6868"]}}
            """;

        IReadOnlyList<IntradayPoint> points = EastMoneyIntradayParser.ParsePoints(json);

        Assert.Equal(2, points.Count);
        Assert.Equal(new DateTime(2026, 6, 18, 10, 31, 0), points[0].Time);
        Assert.Equal(1.689, points[0].Price, 6);
        Assert.Equal(1.6875, points[0].AveragePrice);
        Assert.Equal(340307, points[0].Volume);
        Assert.Equal(250214, points[1].Volume);
        Assert.Equal(57425767, points[0].Amount);
    }

    [Fact]
    public void EastMoneyIntradayParser_PreservesRealMinuteVolumeWithoutSecondDiff()
    {
        const string json = """
            {"data":{"trends":["2026-06-18 09:30,2.389,2.389,2.389,2.389,13345,3188120.000,2.3890","2026-06-18 09:31,2.388,2.387,2.390,2.385,72429,17291377.000,2.3876","2026-06-18 09:32,2.387,2.385,2.387,2.382,36216,8637579.000,2.3868","2026-06-18 09:33,2.385,2.385,2.387,2.383,42452,10127308.000,2.3865"]}}
            """;

        IReadOnlyList<IntradayPoint> points = EastMoneyIntradayParser.ParsePoints(json);

        Assert.Equal(new double?[] { 13345, 72429, 36216, 42452 }, points.Select(point => point.Volume).ToArray());
        Assert.All(points, point => Assert.True(point.Volume > 0));
    }

    [Fact]
    public void IntradayVolumeNormalizer_ConvertsCumulativeVolumeToMinuteVolume()
    {
        IntradayPoint[] points =
        [
            Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.60, 100),
            Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.61, 160),
            Intraday(new DateTime(2026, 6, 19, 9, 32, 0), 1.62, 400),
            Intraday(new DateTime(2026, 6, 19, 9, 33, 0), 1.63, 450)
        ];

        IReadOnlyList<IntradayPoint> normalized = IntradayVolumeNormalizer.Normalize(points, IntradayVolumeFieldKind.Cumulative);

        Assert.Equal(new double?[] { 100, 60, 240, 50 }, normalized.Select(point => point.Volume).ToArray());
    }

    [Fact]
    public void IntradayVolumeNormalizer_MinuteFieldKeepsOriginalValues()
    {
        IntradayPoint[] points =
        [
            Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.60, 100),
            Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.61, 60),
            Intraday(new DateTime(2026, 6, 19, 9, 32, 0), 1.62, 240),
            Intraday(new DateTime(2026, 6, 19, 9, 33, 0), 1.63, 50)
        ];

        IReadOnlyList<IntradayPoint> normalized = IntradayVolumeNormalizer.Normalize(points, IntradayVolumeFieldKind.Minute);

        Assert.Equal(new double?[] { 100, 60, 240, 50 }, normalized.Select(point => point.Volume).ToArray());
    }

    [Fact]
    public void IntradayVolumeNormalizer_ScalesDifferentVolumesToDifferentHeights()
    {
        double h100 = IntradayVolumeNormalizer.ScaleBarHeight(100, 1000, 120);
        double h500 = IntradayVolumeNormalizer.ScaleBarHeight(500, 1000, 120);
        double h1000 = IntradayVolumeNormalizer.ScaleBarHeight(1000, 1000, 120);

        Assert.True(h1000 > h500);
        Assert.True(h500 > h100);
        Assert.Equal(h1000 / 2, h500, 6);
    }

    [Fact]
    public void IntradayVolumeNormalizer_VariedVolumesDoNotAllScaleToSameHeight()
    {
        double[] heights = new[] { 100d, 500d, 1000d }
            .Select(volume => IntradayVolumeNormalizer.ScaleBarHeight(volume, 1000, 120))
            .ToArray();

        Assert.True(heights.Distinct().Count() > 1);
    }

    [Fact]
    public void IntradayVolumeNormalizer_ZeroAndMissingVolumeDoNotCreateFakeHeight()
    {
        IntradayPoint[] points =
        [
            Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.60, 0),
            Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.61, null)
        ];

        IReadOnlyList<IntradayPoint> normalized = IntradayVolumeNormalizer.Normalize(points, IntradayVolumeFieldKind.Cumulative);

        Assert.Equal(0, normalized[0].Volume);
        Assert.Null(normalized[1].Volume);
        Assert.Equal(0, IntradayVolumeNormalizer.ScaleBarHeight(0, 1000, 120));
    }

    [Fact]
    public void IntradayVolumeNormalizer_CumulativeResetUsesCurrentSafeValue()
    {
        IntradayPoint[] points =
        [
            Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.60, 100),
            Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.61, 160),
            Intraday(new DateTime(2026, 6, 19, 9, 32, 0), 1.62, 30)
        ];

        IReadOnlyList<IntradayPoint> normalized = IntradayVolumeNormalizer.Normalize(points, IntradayVolumeFieldKind.Cumulative);

        Assert.Equal(new double?[] { 100, 60, 30 }, normalized.Select(point => point.Volume).ToArray());
    }

    [Fact]
    public void IntradayAxis_UsesFixedStandardTradingBounds()
    {
        Assert.Equal(new[] { "09:30", "10:30", "11:30", "13:00", "14:00", "15:00" }, IntradayTradingTimeAxis.StandardTicks.Select(tick => tick.Label).ToArray());
        Assert.Equal("09:30", IntradayTradingTimeAxis.StandardTicks[0].Label);
        Assert.Equal(0d, IntradayTradingTimeAxis.StandardTicks[0].Ratio);
        Assert.Equal("15:00", IntradayTradingTimeAxis.StandardTicks[^1].Label);
        Assert.Equal(1d, IntradayTradingTimeAxis.StandardTicks[^1].Ratio);

        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 10, 32, 0), out double ratio));
        Assert.InRange(ratio, 0d, 1d);
        Assert.False(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 16, 14, 0), out _));
    }

    [Fact]
    public void IntradayAxis_CompressesLunchBreak()
    {
        Assert.True(IntradayTradingTimeAxis.TryGetTradingMinuteIndex(new DateTime(2026, 6, 19, 10, 30, 0), out double tenThirty));
        Assert.True(IntradayTradingTimeAxis.TryGetTradingMinuteIndex(new DateTime(2026, 6, 19, 11, 30, 0), out double morningClose));
        Assert.True(IntradayTradingTimeAxis.TryGetTradingMinuteIndex(new DateTime(2026, 6, 19, 13, 0, 0), out double afternoonOpen));

        Assert.Equal(60d, morningClose - tenThirty);
        Assert.True(Math.Abs(afternoonOpen - morningClose) < 0.0001);
    }

    [Fact]
    public void IntradayAxis_NoLargeLunchGapBetweenMorningCloseAndAfternoonOpen()
    {
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 11, 30, 0), out double morningClose));
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 13, 0, 0), out double afternoonOpen));

        Assert.InRange(Math.Abs(afternoonOpen - morningClose), 0d, 1d / IntradayTradingTimeAxis.TotalTradingMinutes);
    }

    [Fact]
    public void IntradayAxis_RejectsOutOfTradingSessionTimes()
    {
        Assert.False(IntradayTradingTimeAxis.IsTradingTime(new DateTime(2026, 6, 19, 9, 0, 0)));
        Assert.False(IntradayTradingTimeAxis.IsTradingTime(new DateTime(2026, 6, 19, 12, 0, 0)));
        Assert.False(IntradayTradingTimeAxis.IsTradingTime(new DateTime(2026, 6, 19, 16, 14, 0)));
    }

    [Fact]
    public void IntradayPriceAxis_UsesRealPreviousCloseAndSymmetricRange()
    {
        bool created = IntradayPriceAxisCalculator.TryCreate(
            1.698,
            new[] { 1.734, 1.710, 1.698, 1.662 },
            out IntradayPriceAxis axis);

        Assert.True(created);
        Assert.Equal(1.698, axis.PreviousClose, 6);
        Assert.Equal(axis.DisplayMax - axis.PreviousClose, axis.PreviousClose - axis.DisplayMin, 10);
        Assert.Equal(1.734, axis.DisplayMax, 6);
        Assert.Equal(1.662, axis.DisplayMin, 6);
    }

    [Fact]
    public void IntradayPriceAxis_MapsPricesAboveAndBelowPreviousClose()
    {
        Assert.True(IntradayPriceAxisCalculator.TryCreate(
            1.698,
            new[] { 1.710, 1.698, 1.662 },
            out IntradayPriceAxis axis));

        double zero = axis.GetVerticalRatio(1.698);
        Assert.Equal(0.5, zero, 10);
        Assert.True(axis.GetVerticalRatio(1.710) < zero);
        Assert.True(axis.GetVerticalRatio(1.662) > zero);
        Assert.Equal(zero, axis.GetVerticalRatio(1.698), 10);
    }

    [Fact]
    public void IntradayPriceAxis_AddsSafeRangeWhenPriceIsFlat()
    {
        Assert.True(IntradayPriceAxisCalculator.TryCreate(
            1.698,
            new[] { 1.698, 1.698, 1.698 },
            out IntradayPriceAxis axis));

        Assert.True(axis.DisplayMax > axis.PreviousClose);
        Assert.True(axis.DisplayMin < axis.PreviousClose);
        Assert.Equal(0.5, axis.ZeroLineRatio, 10);
    }

    [Fact]
    public void IntradayPriceAxis_DoesNotCreateFakeLineWithoutPreviousClose()
    {
        bool created = IntradayPriceAxisCalculator.TryCreate(null, new[] { 1.710, 1.662 }, out _);

        Assert.False(created);
    }

    [Fact]
    public void IntradayTradingTimeAxis_ConvertsChinaTimeToUsEasternSummerSession()
    {
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(
            new DateTime(2026, 6, 25, 21, 30, 0),
            out DateTime open));
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(
            new DateTime(2026, 6, 25, 22, 15, 0),
            out DateTime later));

        Assert.Equal(new TimeSpan(9, 30, 0), open.TimeOfDay);
        Assert.Equal(new TimeSpan(10, 15, 0), later.TimeOfDay);
    }

    [Fact]
    public void IntradayTradingTimeAxis_UsEasternRatioDoesNotStretchPartialSessionToRightEdge()
    {
        Assert.True(IntradayTradingTimeAxis.TryGetUsEasternXRatio(
            new DateTime(2026, 6, 25, 22, 15, 0),
            out double ratio));

        Assert.Equal(45d / 390d, ratio, 10);
        Assert.True(ratio < 0.2);
    }

    [Fact]
    public void BuildSnapshot_FiltersOutOfTradingIntradayTimes()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 19, 9, 0, 0), 1.60),
                Intraday(new DateTime(2026, 6, 19, 10, 32, 0), 1.67),
                Intraday(new DateTime(2026, 6, 19, 12, 0, 0), 1.68),
                Intraday(new DateTime(2026, 6, 19, 14, 20, 0), 1.69),
                Intraday(new DateTime(2026, 6, 19, 16, 14, 0), 1.70)
            },
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("159941", 1.71, "2026-06-19 16:14:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(2, snapshot.IntradayPoints.Count);
        Assert.All(snapshot.IntradayPoints, point => Assert.True(IntradayTradingTimeAxis.IsTradingTime(point.Time)));
        Assert.DoesNotContain(snapshot.IntradayPoints, point => point.Time.Hour == 16);
        Assert.False(snapshot.HasQuoteTail);
    }

    [Fact]
    public void BuildSnapshot_IndexIntradayKeepsEastMoneyPointsOutsideAshareSession()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "NDXTMC");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 22, 21, 31, 0), 2880.1, null),
                Intraday(new DateTime(2026, 6, 22, 21, 32, 0), 2882.4, null),
                Intraday(new DateTime(2026, 6, 22, 21, 33, 0), 2881.8, null)
            },
            new ChartDataStatus(true, "EastMoney index intraday", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(3, snapshot.IntradayPoints.Count);
        Assert.All(snapshot.IntradayPoints, point => Assert.False(IntradayTradingTimeAxis.IsTradingTime(point.Time)));
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.Equal("成交量数据不可用", snapshot.VolumeStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_IndexQuoteTailUsesUsEasternTimeInsteadOfRightEdge()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "NDXTMC");
        var cache = new ChartIntradayCacheEntry(
            IndexIntradaySeries(10),
            new ChartDataStatus(true, "EastMoney index intraday", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2833.8, "2026-06-25 22:15:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.True(snapshot.HasQuoteTail);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteTail);
        Assert.Equal(new DateTime(2026, 6, 25, 22, 15, 0), snapshot.IntradayPoints[^1].Time);
        Assert.True(IntradayTradingTimeAxis.TryGetUsEasternXRatio(snapshot.IntradayPoints[^1].Time, out double ratio));
        Assert.Equal(45d / 390d, ratio, 10);
        Assert.NotEqual(1d, ratio);
    }

    [Fact]
    public void BuildSnapshot_IndexIntradayMacdUsesEastMoneyOverseasTimeSeries()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "NDX100");
        IntradayPoint[] points = IndexIntradaySeries(MacdCalculator.MinimumInputCount + 3);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "EastMoney index intraday", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(points.Length, snapshot.IntradayPoints.Count);
        Assert.Equal(points.Length, snapshot.Macd.Count);
        Assert.True(snapshot.MacdStatus.IsReady);
        Assert.Equal(points[^1].Time, snapshot.Macd[^1].Date);
    }

    [Fact]
    public void BuildSnapshot_IntradayPreviousCloseUsesQuoteLastClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.710),
                Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.662)
            },
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { Quote("159941", 1.710, "2026-06-19 09:31:00", 1.698) },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(1.698, snapshot.PreviousClose);
        Assert.NotEqual(snapshot.Quote!.Price, snapshot.PreviousClose);
        Assert.NotEqual(snapshot.IntradayPoints[0].Price, snapshot.PreviousClose);
    }

    [Fact]
    public void BuildSnapshot_IntradayPreviousCloseFallsBackToDailyKLinePreviousClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var intradayCache = new ChartIntradayCacheEntry(
            new[] { Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.710) },
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);
        var dailyCache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 18), 1.60, 1.72, 1.58, 1.680, 1000),
                K(new DateTime(2026, 6, 19), 1.68, 1.73, 1.66, 1.710, 1000)
            },
            new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { Quote("159941", 1.710, "2026-06-19 09:31:00") },
            Array.Empty<MarketQuoteRecord>(),
            intradayCache,
            dailyCache);

        Assert.Equal(1.680, snapshot.PreviousClose);
    }

    [Fact]
    public void BuildSnapshot_KeepsFullRealIntradayTradingDay()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        IntradayPoint[] fullDay = FullTradingDayPoints(new DateTime(2026, 6, 19));
        var cache = new ChartIntradayCacheEntry(
            fullDay,
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(fullDay.Length, snapshot.IntradayPoints.Count);
        Assert.Equal(new TimeSpan(9, 30, 0), snapshot.IntradayPoints[0].Time.TimeOfDay);
        Assert.Equal(new TimeSpan(15, 0, 0), snapshot.IntradayPoints[^1].Time.TimeOfDay);
    }

    [Fact]
    public void BuildSnapshot_IntradayVolumeUnavailableWhenAllRealVolumesMissing()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 19, 9, 30, 0), 1.60, null),
                Intraday(new DateTime(2026, 6, 19, 9, 31, 0), 1.61, null)
            },
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.Equal("成交量数据不可用", snapshot.VolumeStatus.Message);
        Assert.All(snapshot.IntradayPoints, point => Assert.Null(point.Volume));
    }

    [Fact]
    public void BuildSnapshot_IntradayMacdUsesRealPriceSeriesWhenEnoughPoints()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        IntradayPoint[] points = IntradaySeries(MacdCalculator.MinimumInputCount + 5);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(points.Length, snapshot.Macd.Count);
        Assert.True(snapshot.MacdStatus.IsReady);
        Assert.Equal("真实分时MACD", snapshot.MacdStatus.Message);
        Assert.Equal(points[^1].Time, snapshot.Macd[^1].Date);
    }

    [Fact]
    public void BuildSnapshot_IntradayMacdDataInsufficientWhenRealPricePointsAreFew()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var cache = new ChartIntradayCacheEntry(
            IntradaySeries(MacdCalculator.MinimumInputCount - 1),
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Empty(snapshot.Macd);
        Assert.False(snapshot.MacdStatus.IsReady);
        Assert.Equal("MACD数据不足", snapshot.MacdStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_IntradayMacdIgnoresVolumeAmountAndAveragePrice()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        IntradayPoint[] first = IntradaySeries(MacdCalculator.MinimumInputCount + 5)
            .Select((point, index) =>
            {
                point.Volume = 100 + index;
                point.Amount = 1000 + index;
                point.AveragePrice = 9 + index;
                return point;
            })
            .ToArray();
        IntradayPoint[] second = IntradaySeries(MacdCalculator.MinimumInputCount + 5)
            .Select((point, index) =>
            {
                point.Volume = 100000 + index * 10;
                point.Amount = 200000 + index * 10;
                point.AveragePrice = 20 + index;
                return point;
            })
            .ToArray();

        SecurityChartSnapshot a = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new ChartIntradayCacheEntry(first, new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true), DateTimeOffset.Now),
            null);
        SecurityChartSnapshot b = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new ChartIntradayCacheEntry(second, new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true), DateTimeOffset.Now),
            null);

        Assert.Equal(a.Macd.Count, b.Macd.Count);
        for (int i = 0; i < a.Macd.Count; i++)
        {
            Assert.Equal(a.Macd[i].Dif, b.Macd[i].Dif, 12);
            Assert.Equal(a.Macd[i].Dea, b.Macd[i].Dea, 12);
            Assert.Equal(a.Macd[i].Bar, b.Macd[i].Bar, 12);
        }
    }

    [Fact]
    public void BuildSnapshot_IntradayMacdUsesSingleQuoteTailWithoutRepeatingPoints()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        IntradayPoint[] points = IntradaySeries(MacdCalculator.MinimumInputCount - 1);
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.99,
            QuoteTime = "2026-06-19 10:04:00",
            ReceivedAt = "2026-06-19 10:04:00"
        };

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            new ChartIntradayCacheEntry(points, new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true), DateTimeOffset.Now),
            null);

        Assert.Equal(MacdCalculator.MinimumInputCount, snapshot.IntradayPoints.Count);
        Assert.Single(snapshot.IntradayPoints.Where(point => point.IsQuoteTail));
        Assert.Equal(MacdCalculator.MinimumInputCount, snapshot.Macd.Count);
        Assert.Equal(new DateTime(2026, 6, 19, 10, 4, 0), snapshot.Macd[^1].Date);
    }

    [Fact]
    public void BuildSnapshot_IntradayMacdKeepsRealTimesAndStopsAtLatestRealPoint()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        IntradayPoint[] points = FullTradingDayPoints(new DateTime(2026, 6, 19))
            .Where(point => point.Time.TimeOfDay <= new TimeSpan(14, 28, 0))
            .ToArray();
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.True(snapshot.MacdStatus.IsReady);
        Assert.Equal(points.Length, snapshot.Macd.Count);
        Assert.Equal(new DateTime(2026, 6, 19, 14, 28, 0), snapshot.Macd[^1].Date);
        Assert.DoesNotContain(snapshot.Macd, point => point.Date.TimeOfDay == new TimeSpan(15, 0, 0));
        foreach (TimeSpan expectedTime in new[]
        {
            new TimeSpan(9, 30, 0),
            new TimeSpan(10, 30, 0),
            new TimeSpan(11, 30, 0),
            new TimeSpan(13, 0, 0),
            new TimeSpan(14, 0, 0),
            new TimeSpan(14, 28, 0)
        })
        {
            Assert.Contains(snapshot.Macd, point => point.Date.TimeOfDay == expectedTime);
        }
    }

    [Fact]
    public void IntradayAxis_MacdLatestTimeBeforeCloseDoesNotReachRightBoundary()
    {
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 14, 28, 0), out double latestRatio));
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 15, 0, 0), out double closeRatio));

        Assert.True(latestRatio < 1d);
        Assert.Equal(1d, closeRatio);
        Assert.True(closeRatio > latestRatio);
    }

    [Fact]
    public void IntradayAxis_MacdLunchBreakUsesCompressedTradingMinutes()
    {
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 11, 30, 0), out double morningCloseRatio));
        Assert.True(IntradayTradingTimeAxis.TryGetXRatio(new DateTime(2026, 6, 19, 13, 0, 0), out double afternoonOpenRatio));

        Assert.Equal(morningCloseRatio, afternoonOpenRatio, 6);
    }

    [Fact]
    public void EastMoneyHistoryParser_PreservesRealVolumeAndAmount()
    {
        const string json = """
            {"data":{"klines":["2026-06-17,1.60,1.70,1.72,1.58,1000,1700,0,0,0,0"]}}
            """;

        MarketHistoryPoint point = Assert.Single(EastMoneyHistoryParser.ParsePoints(json));

        Assert.Equal(1000, point.Volume);
        Assert.Equal(1700, point.Amount);
    }

    [Fact]
    public void EastMoneyHistoryParser_UsesRealEastMoneyKLineFields()
    {
        const string json = """
            {"data":{"klines":["2026-06-18,1.705,1.693,1.773,1.546,227944057,37772246391.685,13.40,-0.06,-0.001,101.42"]}}
            """;

        MarketHistoryPoint point = Assert.Single(EastMoneyHistoryParser.ParsePoints(json));

        Assert.Equal(new DateTime(2026, 6, 18), point.Date);
        Assert.Equal(1.705, point.Open, 6);
        Assert.Equal(1.693, point.Close, 6);
        Assert.Equal(1.773, point.High, 6);
        Assert.Equal(1.546, point.Low, 6);
        Assert.Equal(227944057, point.Volume);
        Assert.Equal(37772246391.685, point.Amount);
    }

    [Fact]
    public void EastMoneyHistoryParser_DoesNotUsePriceFieldsAsVolume()
    {
        const string json = """
            {"data":{"klines":["2026-06-18,1.680,1.693,1.697,1.662,340307,57425767.000,2.12,-0.29,-0.005,1.05"]}}
            """;

        MarketHistoryPoint point = Assert.Single(EastMoneyHistoryParser.ParsePoints(json));

        Assert.Equal(340307, point.Volume);
        Assert.NotEqual(point.Open, point.Volume);
        Assert.NotEqual(point.Close, point.Volume);
        Assert.NotEqual(point.High, point.Volume);
        Assert.NotEqual(point.Low, point.Volume);
    }

    [Fact]
    public void KLineAggregator_AggregatesWeeklyFromRealDailyOhlcv()
    {
        KLinePoint[] daily =
        [
            K(new DateTime(2026, 6, 15), 10, 12, 9, 11, 100, 1000),
            K(new DateTime(2026, 6, 16), 11, 13, 10, 12, 200, 2000),
            K(new DateTime(2026, 6, 19), 12, 15, 11, 14, 300, 3000)
        ];

        KLinePoint week = Assert.Single(KLineAggregator.AggregateWeekly(daily));

        Assert.Equal(new DateTime(2026, 6, 19), week.Date);
        Assert.Equal(10, week.Open);
        Assert.Equal(15, week.High);
        Assert.Equal(9, week.Low);
        Assert.Equal(14, week.Close);
        Assert.Equal(600, week.Volume);
        Assert.Equal(6000, week.Amount);
        Assert.NotEqual(daily[^1].Volume, week.Volume);
    }

    [Fact]
    public void KLineAggregator_AggregatesMonthlyFromRealDailyOhlcv()
    {
        KLinePoint[] daily =
        [
            K(new DateTime(2026, 6, 3), 10, 11, 8, 9, 100, 1000),
            K(new DateTime(2026, 6, 30), 9, 14, 7, 13, 200, 2000)
        ];

        KLinePoint month = Assert.Single(KLineAggregator.AggregateMonthly(daily));

        Assert.Equal(new DateTime(2026, 6, 30), month.Date);
        Assert.Equal(10, month.Open);
        Assert.Equal(14, month.High);
        Assert.Equal(7, month.Low);
        Assert.Equal(13, month.Close);
        Assert.Equal(300, month.Volume);
        Assert.Equal(3000, month.Amount);
        Assert.NotEqual(daily[^1].Volume, month.Volume);
    }

    [Fact]
    public void KLineVolumeMetrics_ScalesRealKLineVolumesToDifferentHeights()
    {
        KLinePoint[] points =
        [
            K(new DateTime(2026, 6, 17), 1.6, 1.7, 1.5, 1.65, 100),
            K(new DateTime(2026, 6, 18), 1.6, 1.7, 1.5, 1.65, 500),
            K(new DateTime(2026, 6, 19), 1.6, 1.7, 1.5, 1.65, 1000)
        ];

        double max = KLineVolumeMetrics.MaxVisibleVolume(points);
        double h100 = KLineVolumeMetrics.ScaleBarHeight(points[0].Volume, max, 120);
        double h500 = KLineVolumeMetrics.ScaleBarHeight(points[1].Volume, max, 120);
        double h1000 = KLineVolumeMetrics.ScaleBarHeight(points[2].Volume, max, 120);

        Assert.Equal(1000, max);
        Assert.True(h1000 > h500);
        Assert.True(h500 > h100);
    }

    [Fact]
    public void KLineVolumeMetrics_MissingAndZeroVolumeDoNotCreateFakeHeight()
    {
        Assert.Equal(0, KLineVolumeMetrics.ScaleBarHeight(null, 1000, 120));
        Assert.Equal(0, KLineVolumeMetrics.ScaleBarHeight(0, 1000, 120));
    }

    [Theory]
    [InlineData(1.60, 1.70, KLineVolumeColorKind.Up)]
    [InlineData(1.60, 1.60, KLineVolumeColorKind.Up)]
    [InlineData(1.70, 1.60, KLineVolumeColorKind.Down)]
    public void KLineVolumeColorResolver_UsesCloseComparedWithOpen(double open, double close, KLineVolumeColorKind expected)
    {
        KLineVolumeColorKind color = KLineVolumeColorResolver.Resolve(open, close);

        Assert.Equal(expected, color);
    }

    [Fact]
    public void SecurityChartWindow_UsesRealKLineVolumeMetricsAndColorResolver()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("KLineVolumeMetrics.MaxVisibleVolume", source, StringComparison.Ordinal);
        Assert.Contains("KLineVolumeMetrics.ScaleBarHeight", source, StringComparison.Ordinal);
        Assert.Contains("KLineVolumeColorResolver.Resolve", source, StringComparison.Ordinal);
        Assert.Contains("KLineVolumeBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MacdCalculator_ReturnsEmptyWhenDataInsufficient()
    {
        IReadOnlyList<MacdPoint> macd = MacdCalculator.Calculate(Enumerable.Range(0, 10)
            .Select(i => K(new DateTime(2026, 1, 1).AddDays(i), 10, 11, 9, 10 + i * 0.1, 100)));

        Assert.Empty(macd);
    }

    [Fact]
    public void MacdCalculator_UsesRealClosePrices()
    {
        KLinePoint[] lines = Enumerable.Range(0, 60)
            .Select(i => K(new DateTime(2026, 1, 1).AddDays(i), 10 + i, 11 + i, 9 + i, 10 + i, 100))
            .ToArray();

        IReadOnlyList<MacdPoint> macd = MacdCalculator.Calculate(lines);

        Assert.Equal(60, macd.Count);
        Assert.True(macd[^1].Dif > 0);
        Assert.True(macd[^1].Dea > 0);
    }

    [Fact]
    public void BuildSnapshot_UsesDailyHistoryOnlyWhenCacheIsDailyLike()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord monthlyHistory = History("159941", """
            {"data":{"klines":["2026-04-30,1,1,1,1,10,10","2026-05-29,1,1,1,1,10,10"]}}
            """);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { monthlyHistory },
            null,
            null);

        Assert.Empty(snapshot.KLines);
        Assert.False(snapshot.MainStatus.IsReady);
        Assert.Equal("无可用DailyLike日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_UsesChartDailyKLineCacheBeforeDatabaseHistory()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var cache = new ChartKLineCacheEntry(
            new[] { K(new DateTime(2026, 6, 19), 1, 2, 0.8, 1.7, 1000) },
            new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        KLinePoint line = Assert.Single(snapshot.KLines);
        Assert.Equal(1, line.Open);
        Assert.Equal(2, line.High);
        Assert.Equal(0.8, line.Low);
        Assert.Equal(1.7, line.Close);
        Assert.Equal(1000, line.Volume);
    }

    [Fact]
    public void BuildSnapshot_UsesNewerSqliteDailyLikeOverStaleChartCache()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var staleCache = new ChartKLineCacheEntry(
            DailyKLines(220, new DateTime(2025, 10, 16), closeOffset: 0),
            new ChartDataStatus(true, "cached old daily", true),
            DateTimeOffset.Parse("2026-06-25T09:00:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord newerHistory = History(
            "159941",
            DailyHistoryPayload(220, new DateTime(2025, 11, 18), closeOffset: 10),
            "2026-06-25 10:00:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { newerHistory },
            null,
            staleCache);

        Assert.True(snapshot.MainStatus.IsReady);
        Assert.Equal(new DateTime(2026, 6, 25), snapshot.KLines[^1].Date);
        Assert.True(snapshot.KLines[^1].Close > 10);
    }

    [Fact]
    public void BuildSnapshot_UsesNewestReceivedDailyLikeWhenLastDateMatches()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        string olderPayload = DailyHistoryPayload(220, new DateTime(2025, 11, 18), closeOffset: 0);
        string newerPayload = DailyHistoryPayload(220, new DateTime(2025, 11, 18), closeOffset: 20);
        MarketQuoteRecord olderHistory = History("159941", olderPayload, "2026-06-25 09:00:00");
        MarketQuoteRecord newerHistory = History("159941", newerPayload, "2026-06-25 10:00:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { olderHistory, newerHistory },
            null,
            null);

        Assert.Equal(new DateTime(2026, 6, 25), snapshot.KLines[^1].Date);
        Assert.True(snapshot.KLines[^1].Close > 20);
    }

    [Fact]
    public void ChartCache_DoesNotReplaceExistingDailyKLinesWithUnavailableEmptyResult()
    {
        var cache = new ChartCache();
        KLinePoint[] daily = DailyKLines(220);

        cache.SaveDailyKLines(
            "159941",
            daily,
            new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true),
            DateTimeOffset.Parse("2026-06-22T09:30:00+08:00", CultureInfo.InvariantCulture));
        cache.SaveDailyKLines(
            "159941",
            Array.Empty<KLinePoint>(),
            new ChartDataStatus(false, "鏃鏁版嵁鏆備笉鍙敤"),
            DateTimeOffset.Parse("2026-06-22T13:30:00+08:00", CultureInfo.InvariantCulture));

        ChartKLineCacheEntry? entry = cache.GetDailyKLines("159941");
        Assert.NotNull(entry);
        Assert.Equal(220, entry.Points.Count);
        Assert.True(entry.Status.IsReady);
        Assert.True(entry.Status.IsUsingCache);
        Assert.Equal("使用最近真实日K缓存", entry.Status.Message);
    }

    [Fact]
    public void ChartCache_UpdatesDailyKLinesWhenNewDailyResultIsReady()
    {
        var cache = new ChartCache();
        KLinePoint[] oldDaily = DailyKLines(220, closeOffset: 0);
        KLinePoint[] newDaily = DailyKLines(230, closeOffset: 1);

        cache.SaveDailyKLines("159941", oldDaily, new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true), DateTimeOffset.Now);
        cache.SaveDailyKLines("159941", newDaily, new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true), DateTimeOffset.Now);

        ChartKLineCacheEntry? entry = cache.GetDailyKLines("159941");
        Assert.NotNull(entry);
        Assert.Equal(230, entry.Points.Count);
        Assert.Equal(newDaily[^1].Close, entry.Points[^1].Close);
        Assert.Equal("鐪熷疄鏃鎺ュ彛缂撳瓨", entry.Status.Message);
    }

    [Fact]
    public void BuildSnapshot_FallsBackToSqliteDailyLikeWhenChartCacheIsUnavailable()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord dailyHistory = History("159941", DailyHistoryPayload(220));
        var unavailableCache = new ChartKLineCacheEntry(
            Array.Empty<KLinePoint>(),
            new ChartDataStatus(false, "鏃鏁版嵁鏆備笉鍙敤"),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { dailyHistory },
            null,
            unavailableCache);

        Assert.NotEmpty(snapshot.KLines);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.Equal("使用最近真实日K缓存", snapshot.MainStatus.Message);
        Assert.True(snapshot.VolumeStatus.IsReady);
    }

    [Fact]
    public void BuildSnapshot_WeeklyAndMonthlyAggregateFromSqliteDailyLikeFallback()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord dailyHistory = History("159941", DailyHistoryPayload(220));

        SecurityChartSnapshot weekly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { dailyHistory },
            null,
            null);
        SecurityChartSnapshot monthly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { dailyHistory },
            null,
            null);

        Assert.NotEmpty(weekly.KLines);
        Assert.NotEmpty(monthly.KLines);
        Assert.True(weekly.VolumeStatus.IsReady);
        Assert.True(monthly.VolumeStatus.IsReady);
        Assert.All(weekly.KLines.Concat(monthly.KLines), point => Assert.True(point.Volume > 0));
    }

    [Fact]
    public void BuildSnapshot_WeeklyAndMonthlyUseNewerSqliteDailyLikeOverStaleChartCache()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var staleCache = new ChartKLineCacheEntry(
            DailyKLines(220, new DateTime(2025, 10, 16), closeOffset: 0),
            new ChartDataStatus(true, "cached old daily", true),
            DateTimeOffset.Parse("2026-06-25T09:00:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord newerHistory = History(
            "159941",
            DailyHistoryPayload(220, new DateTime(2025, 11, 18), closeOffset: 10),
            "2026-06-25 10:00:00");

        SecurityChartSnapshot weekly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { newerHistory },
            null,
            staleCache);
        SecurityChartSnapshot monthly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { newerHistory },
            null,
            staleCache);

        Assert.Equal(new DateTime(2026, 6, 25), weekly.KLines[^1].Date);
        Assert.Equal(new DateTime(2026, 6, 25), monthly.KLines[^1].Date);
        Assert.True(weekly.KLines[^1].Close > 10);
        Assert.True(monthly.KLines[^1].Close > 10);
    }

    [Fact]
    public void BuildSnapshot_MonthlyOnlyHistoryStillDoesNotDisplayDailyKLine()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord monthlyHistory = History("159941", MonthlyHistoryPayload());

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { monthlyHistory },
            null,
            null);

        Assert.Empty(snapshot.KLines);
        Assert.False(snapshot.MainStatus.IsReady);
        Assert.Equal("无可用DailyLike日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_QuoteTailDoesNotCreateHistoricalIntradayLine()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord quote = Quote("159941", 1.677, "2026-06-19 10:01:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            null);

        IntradayPoint point = Assert.Single(snapshot.IntradayPoints);
        Assert.True(point.IsQuoteTail);
        Assert.Null(point.Volume);
        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.True(snapshot.HasQuoteTail);
    }

    [Fact]
    public void BuildSnapshot_IntradayUnavailableDoesNotGenerateFakePoints()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            null,
            null);

        Assert.Empty(snapshot.IntradayPoints);
        Assert.Equal("分时数据暂不可用", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_DailyDoesNotCreateTemporaryBarWhenQuoteOhlcMissing()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var cache = new ChartKLineCacheEntry(
            new[] { K(new DateTime(2026, 6, 19), 1.6, 1.7, 1.5, 1.65, 1000) },
            new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("159941", 1.8, "2026-06-19 14:30:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        KLinePoint line = Assert.Single(snapshot.KLines);
        Assert.Equal(1.65, line.Close);
        Assert.Equal(1.7, line.High);
        Assert.Equal(1000, line.Volume);
        Assert.False(line.IsQuoteAdjusted);
        Assert.False(line.IsDisplayOnly);
        Assert.Contains("OHLC字段不足", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshot_VolumeUnavailableWhenRealVolumeMissing()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        var cache = new ChartKLineCacheEntry(
            new[] { K(new DateTime(2026, 6, 19), 1.6, 1.7, 1.5, 1.65, null) },
            new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.Equal("成交量数据不可用", snapshot.VolumeStatus.Message);
    }

    [Fact]
    public void ChartChangePercentCalculator_IntradayUsesLatestPriceAndPreviousCloseWhenQuotePercentMissing()
    {
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.693,
            LastClose = 1.698
        };

        double? change = ChartChangePercentCalculator.CalculateIntradayChange(quote, Array.Empty<IntradayPoint>());

        Assert.Equal((1.693 / 1.698) - 1.0, change!.Value, 8);
        Assert.Equal("-0.29%", ChartPercentFormatter.FormatRatio(change));
    }

    [Fact]
    public void ChartChangePercentCalculator_DailyUsesCurrentCloseAndPreviousDailyClose()
    {
        KLinePoint[] daily =
        [
            K(new DateTime(2026, 6, 18), 1.60, 1.70, 1.58, 1.680, 100),
            K(new DateTime(2026, 6, 19), 1.68, 1.72, 1.67, 1.700, 100)
        ];

        double? change = ChartChangePercentCalculator.CalculateKLineChange(daily);

        Assert.Equal((1.700 / 1.680) - 1.0, change!.Value, 8);
        Assert.Equal("+1.19%", ChartPercentFormatter.FormatRatio(change));
    }

    [Fact]
    public void BuildSnapshot_DailyChangeUsesQuoteAdjustedCloseAndPreviousDailyClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "chart");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 18), 1.60, 1.70, 1.58, 1.680, 100),
                K(new DateTime(2026, 6, 19), 1.68, 1.72, 1.67, 1.700, 100)
            },
            new ChartDataStatus(true, "daily", true),
            DateTimeOffset.Now);
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.693,
            LastClose = 1.698,
            ChangePercent = -0.0029,
            OpenValue = 1.698,
            HighValue = 1.710,
            LowValue = 1.690,
            QuoteTime = "2026-06-19 14:30:00",
            ReceivedAt = "2026-06-19 14:30:00"
        };

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal((1.693 / 1.680) - 1.0, snapshot.ChangePercent!.Value, 8);
        Assert.NotEqual(quote.ChangePercent!.Value, snapshot.ChangePercent.Value);
    }

    [Fact]
    public void BuildSnapshot_WeeklyChangeUsesCurrentWeekCloseAndPreviousWeekClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "chart");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 5), 1.55, 1.65, 1.50, 1.600, 100),
                K(new DateTime(2026, 6, 12), 1.62, 1.70, 1.60, 1.680, 100)
            },
            new ChartDataStatus(true, "daily", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(0.05, snapshot.ChangePercent!.Value, 8);
        Assert.Equal("+5.00%", ChartPercentFormatter.FormatRatio(snapshot.ChangePercent));
    }

    [Fact]
    public void BuildSnapshot_MonthlyChangeUsesCurrentMonthCloseAndPreviousMonthClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "chart");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 5, 29), 1.90, 2.05, 1.85, 2.000, 100),
                K(new DateTime(2026, 6, 30), 2.00, 2.05, 1.80, 1.900, 100)
            },
            new ChartDataStatus(true, "daily", true),
            DateTimeOffset.Now);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(-0.05, snapshot.ChangePercent!.Value, 8);
        Assert.Equal("-5.00%", ChartPercentFormatter.FormatRatio(snapshot.ChangePercent));
    }

    [Fact]
    public void ChartChangePercentCalculator_NoPreviousKLineCloseReturnsEmpty()
    {
        KLinePoint[] daily =
        [
            K(new DateTime(2026, 6, 19), 1.68, 1.72, 1.67, 1.700, 100)
        ];

        double? change = ChartChangePercentCalculator.CalculateKLineChange(daily);

        Assert.Null(change);
        Assert.Equal("--", ChartPercentFormatter.FormatRatio(change));
    }

    [Theory]
    [InlineData(0.01234, "+1.23%")]
    [InlineData(-0.0029446408, "-0.29%")]
    [InlineData(0, "0.00%")]
    [InlineData(-0.0000001, "0.00%")]
    public void ChartPercentFormatter_FormatsRatioWithTwoDecimalsAndNoNegativeZero(double value, string expected)
    {
        Assert.Equal(expected, ChartPercentFormatter.FormatRatio(value));
    }

    [Fact]
    public void SecurityChartWindow_UsesSnapshotPeriodChangePercentForHeader()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("snapshot.ChangePercent", source, StringComparison.Ordinal);
        Assert.Contains("ChartPercentFormatter.FormatRatio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeText.Text = FormatSignedPercent(snapshot.Quote?.ChangePercent)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_DoesNotUseOwnDispatcherTimer()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_UsesStandardIntradayAxisAndDarkChartPanels()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));
        string xaml = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml"));

        Assert.Contains("IntradayTradingTimeAxis.StandardTicks", source, StringComparison.Ordinal);
        Assert.Contains("XByTradingTime", source, StringComparison.Ordinal);
        Assert.Contains("UsesStandardIntradayAxis", source, StringComparison.Ordinal);
        Assert.Contains("DrawUsEasternIntradayAxisLabels", source, StringComparison.Ordinal);
        Assert.Contains("IntradayTradingTimeAxis.UsEasternTicks", source, StringComparison.Ordinal);
        Assert.Contains("美东时间", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawDynamicIntradayAxisLabels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTradingSession", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource ChartPanelBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_DrawMacdUsesIntradayTradingTimeInsteadOfIndexForIntraday()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));
        int drawMacdStart = source.IndexOf("private void DrawMacd", StringComparison.Ordinal);
        int drawGridStart = source.IndexOf("private static void DrawGrid", drawMacdStart, StringComparison.Ordinal);
        string drawMacdBody = source[drawMacdStart..drawGridStart];

        Assert.Contains("Period == SecurityChartPeriod.Intraday", drawMacdBody, StringComparison.Ordinal);
        Assert.Contains("TryGetMacdX(plot, snapshot, point, i, points.Length, isIntraday", drawMacdBody, StringComparison.Ordinal);
        Assert.Contains("IntradayTradingTimeAxis.TryGetXRatio(point.Date", source, StringComparison.Ordinal);
        Assert.Contains("IntradayTradingTimeAxis.TryGetUsEasternXRatio(point.Date", source, StringComparison.Ordinal);
        Assert.Contains("VisibleIntradayMacdPoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("plot.Left + step * i + step / 2", drawMacdBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_DrawsPreviousCloseLineOnlyForIntradayChart()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));
        int drawIntradayStart = source.IndexOf("private void DrawIntraday", StringComparison.Ordinal);
        int drawKLinesStart = source.IndexOf("private void DrawKLines", drawIntradayStart, StringComparison.Ordinal);
        int drawSubPanelStart = source.IndexOf("private void DrawSubPanel", drawKLinesStart, StringComparison.Ordinal);
        string drawIntradayBody = source[drawIntradayStart..drawKLinesStart];
        string drawKLinesBody = source[drawKLinesStart..drawSubPanelStart];

        Assert.Contains("IntradayPriceAxisCalculator.TryCreate(snapshot.PreviousClose", drawIntradayBody, StringComparison.Ordinal);
        Assert.Contains("DrawPreviousCloseLine", drawIntradayBody, StringComparison.Ordinal);
        Assert.Contains("昨收线不可用", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawPreviousCloseLine", drawKLinesBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_UsesRealIntradayVolumeColorResolver()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("IntradayVolumeColorResolver.Resolve", source, StringComparison.Ordinal);
        Assert.Contains("IntradayVolumeNormalizer.ScaleBarHeight", source, StringComparison.Ordinal);
        Assert.Contains("IntradayVolumeBrush", source, StringComparison.Ordinal);
        Assert.Contains("NeutralVolumeBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntradayVolumeColorResolver_UpPriceReturnsRedKind()
    {
        IntradayVolumeColorKind color = IntradayVolumeColorResolver.Resolve(1.61, 1.60, IntradayVolumeColorKind.Neutral);

        Assert.Equal(IntradayVolumeColorKind.Up, color);
    }

    [Fact]
    public void IntradayVolumeColorResolver_DownPriceReturnsGreenKind()
    {
        IntradayVolumeColorKind color = IntradayVolumeColorResolver.Resolve(1.60, 1.61, IntradayVolumeColorKind.Up);

        Assert.Equal(IntradayVolumeColorKind.Down, color);
    }

    [Theory]
    [InlineData(IntradayVolumeColorKind.Up)]
    [InlineData(IntradayVolumeColorKind.Down)]
    public void IntradayVolumeColorResolver_FlatPriceInheritsPreviousColor(IntradayVolumeColorKind previousColor)
    {
        IntradayVolumeColorKind color = IntradayVolumeColorResolver.Resolve(1.60, 1.60, previousColor);

        Assert.Equal(previousColor, color);
    }

    [Fact]
    public void IntradayVolumeColorResolver_FirstBarReturnsNeutral()
    {
        IntradayVolumeColorKind color = IntradayVolumeColorResolver.Resolve(1.60, null, null);

        Assert.Equal(IntradayVolumeColorKind.Neutral, color);
    }

    [Fact]
    public void SecurityChartWindow_UsesCustomChromeToAvoidSystemWhiteFrame()
    {
        string xaml = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml"));

        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<shell:WindowChrome.WindowChrome>", xaml, StringComparison.Ordinal);
        Assert.Contains("GlassFrameThickness=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UseAeroCaptionButtons=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeBorderThickness=\"8\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_UsesDarkOuterRootAndBorders()
    {
        string xaml = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml"));

        Assert.Contains("x:Name=\"ChartWindowRootBorder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"#17344B\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ChartWindowRootGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{StaticResource ChartBorderBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle\" Value=\"{x:Null}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush=\"White\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BorderBrush=\"LightGray\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Background=\"White\"", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecurityChartWindow_DoesNotWriteTradeLogOrAlertLog()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));
        string manager = File.ReadAllText(FindWorkspaceFile("Views", "ChartWindowManager.cs"));

        Assert.DoesNotContain("trade_log", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert_log", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trade_log", manager, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert_log", manager, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_IsolatesIntradayCircuitBreakerBySymbol()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo broken = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        ChartSecurityInfo healthy = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳斯达克科技指数");
        subscriptions.Subscribe(broken, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        subscriptions.Subscribe(healthy, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(broken.EastMoneySecId, "ResponseEnded");
        fakeClient.EnqueueIntradayFailure(broken.EastMoneySecId, "ResponseEnded");
        fakeClient.EnqueueIntradayFailure(broken.EastMoneySecId, "ResponseEnded");

        for (int i = 0; i < 3; i++)
        {
            now = now.AddSeconds(21);
            await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        }

        ChartIntradayCacheEntry? brokenEntry = cache.GetIntraday("100.NDX100");
        ChartIntradayCacheEntry? healthyEntry = cache.GetIntraday("251.NDXTMC");
        Assert.NotNull(brokenEntry);
        Assert.NotNull(healthyEntry);
        Assert.True(brokenEntry.Status.IsCircuitOpen);
        Assert.Equal("指数分时接口熔断中，无可用分时缓存", brokenEntry.Status.Message);
        Assert.True(healthyEntry.Status.IsReady);
        Assert.False(healthyEntry.Status.IsCircuitOpen);
        Assert.NotEmpty(healthyEntry.Points);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_HalfOpenClearsIntradayCircuitAfterCooldownSuccess()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");
        fakeClient.EnqueueIntradaySuccess(info.EastMoneySecId, FullTradingDayPoints(new DateTime(2026, 6, 22)).Take(3).ToArray());

        for (int i = 0; i < 3; i++)
        {
            now = now.AddSeconds(21);
            await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        }

        ChartIntradayCacheEntry? circuitEntry = cache.GetIntraday("100.NDX100");
        Assert.NotNull(circuitEntry);
        Assert.True(circuitEntry.Status.IsCircuitOpen);
        now = now.AddMinutes(10).AddSeconds(21);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        ChartIntradayCacheEntry? entry = cache.GetIntraday("100.NDX100");
        Assert.NotNull(entry);
        Assert.True(entry.Status.IsReady);
        Assert.False(entry.Status.IsCircuitOpen);
        Assert.Equal("指数真实分时数据", entry.Status.Message);
    }

    [Fact]
    public void ChartCache_KeepsIntradayCacheWhenUnavailableStatusArrives()
    {
        var cache = new ChartCache();
        IntradayPoint[] points = FullTradingDayPoints(new DateTime(2026, 6, 22)).Take(4).ToArray();

        cache.SaveIntraday("159941", points, new ChartDataStatus(true, "鐪熷疄鍒嗘椂鏁版嵁"), DateTimeOffset.Now);
        cache.SaveIntraday("159941", Array.Empty<IntradayPoint>(), new ChartDataStatus(false, "分时接口熔断中", IsCircuitOpen: true), DateTimeOffset.Now);

        ChartIntradayCacheEntry? entry = cache.GetIntraday("159941");
        Assert.NotNull(entry);
        Assert.Equal(4, entry.Points.Count);
        Assert.True(entry.Status.IsReady);
        Assert.True(entry.Status.IsUsingCache);
        Assert.True(entry.Status.IsCircuitOpen);
        Assert.Equal("真实分时缓存", entry.Status.Message);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_FallsBackToPersistedIntradayCacheWhenApiFails()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                FullTradingDayPoints(new DateTime(2026, 6, 22)).Take(4).ToArray(),
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:01:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        ChartIntradayCacheEntry? entry = cache.GetIntraday("159941");
        Assert.NotNull(entry);
        Assert.True(entry.Status.IsReady);
        Assert.True(entry.Status.IsUsingCache);
        Assert.Equal("使用最近真实分时缓存", entry.Status.Message);
        Assert.Equal(4, entry.Points.Count);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_NoIntradayCacheShowsExplicitFailureStatus()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:01:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        ChartIntradayCacheEntry? entry = cache.GetIntraday("159941");
        Assert.NotNull(entry);
        Assert.False(entry.Status.IsReady);
        Assert.Equal("腾讯分时接口失败，无可用分时缓存", entry.Status.Message);
        Assert.Empty(entry.Points);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_PersistsOnlyRealIntradayPayloadAfterSuccess()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:01:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        string payload = IntradayPayload(new DateTime(2026, 6, 22), 3);
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, payload);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(payload, store.SavedPayload);
        Assert.Equal("100.NDX100", store.SavedStrategyCode);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundRefreshesEnabledSymbolWithoutWindowSubscription()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        string payload = IntradayPayload(new DateTime(2026, 6, 22), 4);
        int snapshotEvents = 0;
        coordinator.SnapshotUpdated += (_, _) => snapshotEvents++;
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, payload);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { info },
            CancellationToken.None);

        Assert.Equal(1, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(payload, store.SavedPayload);
        Assert.Equal("后台指数真实分时缓存", cache.GetIntraday("100.NDX100")?.Status.Message);
        Assert.Equal(0, snapshotEvents);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundContinuesAfterDisplayUnsubscribe()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        string payload = IntradayPayload(new DateTime(2026, 6, 22), 4);
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        subscriptions.Unsubscribe(info.StrategyCode);
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, payload);

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { info },
            CancellationToken.None);

        Assert.Equal(1, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("后台指数真实分时缓存", cache.GetIntraday("100.NDX100")?.Status.Message);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundSkipsNonTradingSession()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T12:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { info },
            CancellationToken.None);

        Assert.Equal(0, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Null(cache.GetIntraday("159941"));
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundSkipsEveningNonTradingSession()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T20:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { info },
            CancellationToken.None);

        Assert.Equal(0, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Null(cache.GetIntraday("159941"));
    }

    [Fact]
    public void IndexIntradayCompleteness_PartialLatestCompletedSessionShouldCatchUp()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        IntradayPoint[] partial =
        [
            Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2910, null),
            Intraday(new DateTime(2026, 6, 30, 0, 30, 0), 2868, null)
        ];

        IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(partial, now);

        Assert.False(completeness.IsCompleteSession);
        Assert.True(completeness.ShouldCatchUp);
        Assert.Equal(new DateOnly(2026, 6, 29), completeness.LatestCompletedTradeDate);
        Assert.Equal(new TimeSpan(12, 30, 0), completeness.LastPointEasternTime);
    }

    [Fact]
    public void IndexIntradayCompleteness_CompleteLatestCompletedSessionDoesNotCatchUp()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        IntradayPoint[] complete =
        [
            Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2910, null),
            Intraday(new DateTime(2026, 6, 30, 3, 58, 0), 2890, null)
        ];

        IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(complete, now);

        Assert.True(completeness.IsCompleteSession);
        Assert.False(completeness.ShouldCatchUp);
        Assert.Equal(new TimeSpan(15, 58, 0), completeness.LastPointEasternTime);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_IndexNonTradingPartialCacheCatchUpUsesScheduler()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                new[]
                {
                    Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2910, null),
                    Intraday(new DateTime(2026, 6, 30, 0, 30, 0), 2868, null)
                },
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-30T00:30:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        var scheduler = new GlobalMarketRequestScheduler(new Random(1));
        scheduler.BeginTick(now);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, scheduler: scheduler, nowProvider: () => now);
        ChartSecurityInfo first = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        ChartSecurityInfo second = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(first, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        subscriptions.Subscribe(second, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        string completePayload = IndexIntradayPayload(
            new DateTime(2026, 6, 29, 21, 30, 0),
            new DateTime(2026, 6, 30, 3, 58, 0));
        fakeClient.EnqueueIntradaySuccessPayload(first.EastMoneySecId, completePayload);
        fakeClient.EnqueueIntradaySuccessPayload(second.EastMoneySecId, completePayload);

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        int requestCount = fakeClient.GetIntradayRequestCount(first.EastMoneySecId)
                           + fakeClient.GetIntradayRequestCount(second.EastMoneySecId);
        Assert.Equal(1, requestCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Contains(store.SavedStrategyCode, new[] { first.StrategyCode, second.StrategyCode });
    }

    [Fact]
    public async Task ChartRefreshCoordinator_IndexNonTradingCompleteCacheDoesNotCatchUp()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                new[]
                {
                    Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2910, null),
                    Intraday(new DateTime(2026, 6, 30, 3, 58, 0), 2890, null)
                },
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-30T04:00:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IndexIntradayPayload(
            new DateTime(2026, 6, 29, 21, 30, 0),
            new DateTime(2026, 6, 30, 3, 58, 0)));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(0, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(2, cache.GetIntraday(info.StrategyCode)?.Points.Count);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_IndexCatchUpFailureKeepsCacheAndDoesNotRetrySameDay()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                new[]
                {
                    Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2910, null),
                    Intraday(new DateTime(2026, 6, 30, 0, 30, 0), 2868, null)
                },
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-30T00:30:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        now = now.AddMinutes(10);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(1, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        ChartIntradayCacheEntry? entry = cache.GetIntraday(info.StrategyCode);
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Points.Count);
        Assert.True(entry.Status.IsUsingCache);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_EtfNonTradingDoesNotUseIndexCatchUpPath()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                FullTradingDayPoints(new DateTime(2026, 6, 29)).Take(2).ToArray(),
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 30), 4));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.Equal(0, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundRateLimitsPerSymbolAcrossFastTicks()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 5));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), new[] { info }, CancellationToken.None);
        now = now.AddSeconds(3);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), new[] { info }, CancellationToken.None);
        Assert.Equal(1, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));

        now = now.AddSeconds(58);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), new[] { info }, CancellationToken.None);

        Assert.Equal(2, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundDedupesEnabledSymbols()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo first = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        ChartSecurityInfo second = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳斯达克科技指数");
        fakeClient.EnqueueIntradaySuccessPayload(first.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));
        fakeClient.EnqueueIntradaySuccessPayload(second.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { first, first, second },
            CancellationToken.None);

        Assert.Equal(1, fakeClient.GetIntradayRequestCount(first.EastMoneySecId));
        Assert.Equal(0, fakeClient.GetIntradayRequestCount(second.EastMoneySecId));
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundFailureDoesNotPersistFakePayloadOrOverwriteOldCache()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        var store = new FakeChartIntradayCacheStore
        {
            Entry = new ChartIntradayCacheEntry(
                FullTradingDayPoints(new DateTime(2026, 6, 22)).Take(4).ToArray(),
                new ChartDataStatus(true, "使用最近真实分时缓存", true),
                DateTimeOffset.Parse("2026-06-22T09:40:00+08:00", CultureInfo.InvariantCulture))
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T22:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, intradayCacheStore: store, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "ResponseEnded");

        await coordinator.RefreshAsync(
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            new[] { info },
            CancellationToken.None);

        Assert.Equal(0, store.SaveCount);
        ChartIntradayCacheEntry? entry = cache.GetIntraday("100.NDX100");
        Assert.NotNull(entry);
        Assert.True(entry.Status.IsUsingCache);
        Assert.Equal(4, entry.Points.Count);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_BackgroundAfterCloseDoesNotRequestIntradayLive()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T15:05:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 4));
        fakeClient.EnqueueIntradaySuccessPayload(info.EastMoneySecId, IntradayPayload(new DateTime(2026, 6, 22), 5));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), new[] { info }, CancellationToken.None);
        now = now.AddMinutes(5);
        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), new[] { info }, CancellationToken.None);

        Assert.Equal(0, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
    }

    [Fact]
    public void BuildSnapshot_ShowsIntradayCacheWithQuoteTailDuringCircuit()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartIntradayCacheEntry(
            new[] { Intraday(new DateTime(2026, 6, 22, 10, 1, 0), 1.68) },
            new ChartDataStatus(true, "鐪熷疄鍒嗘椂缂撳瓨", true, IsCircuitOpen: true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("159941", 1.69, "2026-06-22 10:02:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(2, snapshot.IntradayPoints.Count);
        Assert.True(snapshot.HasQuoteTail);
        Assert.Equal("分时接口熔断中，使用最近真实分时缓存 + 实时 quote 尾点", snapshot.MainStatus.Message);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.True(snapshot.MainStatus.IsCircuitOpen);
    }

    [Fact]
    public void BuildSnapshot_IndexCircuitWithCacheExplainsCacheAndQuoteTail()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 25, 21, 30, 0), 2910, null),
                Intraday(new DateTime(2026, 6, 25, 21, 45, 0), 2850, null)
            },
            new ChartDataStatus(true, "指数分时接口熔断中，使用最近真实分时缓存", true, IsCircuitOpen: true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2833.8, "2026-06-25 22:15:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(3, snapshot.IntradayPoints.Count);
        Assert.True(snapshot.HasQuoteTail);
        Assert.Equal("指数分时接口熔断中，显示最近真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点", snapshot.MainStatus.Message);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.True(snapshot.MainStatus.IsCircuitOpen);
        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.Equal("成交量数据不可用", snapshot.VolumeStatus.Message);
        Assert.True(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, snapshot.IntradayPoints[0]));
        Assert.True(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, snapshot.IntradayPoints[1]));
        Assert.False(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, snapshot.IntradayPoints[^1]));
        Assert.Contains("缺少中间分钟分时点", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_IndexQuoteTailDoesNotJoinMainPolyline()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 25, 21, 30, 0), 2910, null),
                Intraday(new DateTime(2026, 6, 25, 21, 31, 0), 2890, null),
                Intraday(new DateTime(2026, 6, 25, 21, 32, 0), 2840, null)
            },
            new ChartDataStatus(true, "指数分时接口熔断中，使用最近真实分时缓存", true, IsCircuitOpen: true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2848, "2026-06-25 22:15:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(4, snapshot.IntradayPoints.Count);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteTail);
        IntradayPoint[] linePoints = snapshot.IntradayPoints
            .Where(point => CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, point))
            .ToArray();
        Assert.Equal(3, linePoints.Length);
        Assert.DoesNotContain(linePoints, point => point.IsQuoteTail);
        Assert.Equal(new DateTime(2026, 6, 25, 21, 32, 0), linePoints[^1].Time);
        Assert.Contains("缺少中间分钟分时点", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityChartWindow_EtfQuoteTailStillUsesExistingPolylineRule()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartIntradayCacheEntry(
            new[] { Intraday(new DateTime(2026, 6, 25, 10, 1, 0), 1.68) },
            new ChartDataStatus(true, "真实分时缓存", true, IsCircuitOpen: true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("159941", 1.69, "2026-06-25 10:02:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(2, snapshot.IntradayPoints.Count);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteTail);
        Assert.All(snapshot.IntradayPoints, point =>
            Assert.True(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, point)));
    }

    [Fact]
    public void BuildSnapshot_IndexAfterCloseCompleteCacheUsesQuoteAsDisplayClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2846.54, null),
                Intraday(new DateTime(2026, 6, 30, 3, 59, 0), 2889.64, null)
            },
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2890.64, "2026-06-30 09:39:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        IntradayPoint last = snapshot.IntradayPoints[^1];
        Assert.False(snapshot.HasQuoteTail);
        Assert.False(last.IsQuoteTail);
        Assert.True(last.IsQuoteCloseDisplayPoint);
        Assert.Equal("QUOTE_CLOSE_DISPLAY", last.PointSource);
        Assert.Equal(2890.64, last.Price);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(last.Time, out DateTime easternClose));
        Assert.Equal(new TimeSpan(16, 0, 0), easternClose.TimeOfDay);
        Assert.True(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, last));
    }

    [Fact]
    public void BuildSnapshot_Ndx100AfterCloseCompleteCacheUsesQuoteAsDisplayClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 29310.26, null),
                Intraday(new DateTime(2026, 6, 30, 4, 0, 0), 29765.66, null)
            },
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("100.NDX100", 29774.75, "2026-06-30 09:39:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        IntradayPoint last = snapshot.IntradayPoints[^1];
        Assert.False(snapshot.HasQuoteTail);
        Assert.True(last.IsQuoteCloseDisplayPoint);
        Assert.Equal("QUOTE_CLOSE_DISPLAY", last.PointSource);
        Assert.Equal(29774.75, last.Price);
    }

    [Fact]
    public void BuildSnapshot_IndexDuringTradingKeepsQuoteAsIndependentTail()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2846.54, null),
                Intraday(new DateTime(2026, 6, 29, 21, 45, 0), 2850.00, null)
            },
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-29T22:00:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2860.00, "2026-06-29 22:00:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.True(snapshot.HasQuoteTail);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteTail);
        Assert.False(snapshot.IntradayPoints[^1].IsQuoteCloseDisplayPoint);
        Assert.False(CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.ShouldConnectIntradayPoint(snapshot, snapshot.IntradayPoints[^1]));
    }

    [Fact]
    public void BuildSnapshot_IndexPartialCacheDoesNotConnectAfterCloseQuote()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2846.54, null),
                Intraday(new DateTime(2026, 6, 30, 0, 30, 0), 2850.00, null)
            },
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2890.64, "2026-06-30 09:39:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        IntradayPoint last = snapshot.IntradayPoints[^1];
        Assert.False(snapshot.HasQuoteTail);
        Assert.False(last.IsQuoteCloseDisplayPoint);
        Assert.Equal(2850.00, last.Price);
    }

    [Fact]
    public void BuildSnapshot_IndexCompleteCacheWithoutQuoteDoesNotCreateDisplayClose()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartIntradayCacheEntry(
            new[]
            {
                Intraday(new DateTime(2026, 6, 29, 21, 30, 0), 2846.54, null),
                Intraday(new DateTime(2026, 6, 30, 3, 59, 0), 2889.64, null)
            },
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.False(snapshot.HasQuoteTail);
        Assert.DoesNotContain(snapshot.IntradayPoints, point => point.IsQuoteCloseDisplayPoint);
        Assert.Equal(2889.64, snapshot.IntradayPoints[^1].Price);
    }

    [Fact]
    public void BuildSnapshot_IndexFullUsSessionAcrossBeijingMidnightKeepsMorningPoints()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 2800);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.IntradayPoints.Count);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.IntradayPoints[0].Time, out DateTime firstEastern));
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.IntradayPoints[^1].Time, out DateTime lastEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
        Assert.Equal(new TimeSpan(16, 0, 0), lastEastern.TimeOfDay);
        Assert.Contains(snapshot.IntradayPoints, point =>
            IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(point.Time, out DateTime eastern)
            && eastern.TimeOfDay == new TimeSpan(10, 0, 0));
    }

    [Fact]
    public void IndexIntradayCacheCompleteness_NoMorningSessionIsNotComplete()
    {
        IntradayPoint[] afternoonOnly =
        {
            Intraday(new DateTime(2026, 6, 30, 0, 0, 0), 2860, null),
            Intraday(new DateTime(2026, 6, 30, 4, 0, 0), 2890, null)
        };
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T10:11:00+08:00", CultureInfo.InvariantCulture);

        IndexIntradayCacheCompleteness completeness = IndexIntradayCacheCompletenessService.Analyze(afternoonOnly, now);

        Assert.False(completeness.IsCompleteSession);
        Assert.Contains("morning", completeness.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new TimeSpan(12, 0, 0), completeness.FirstPointEasternTime);
    }

    [Fact]
    public void BuildSnapshot_QuoteCloseDisplayDoesNotDropIndexMorningSession()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 2800);
        points[^1].Price = 2889.64;
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2890.64, "2026-06-30 10:11:14", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.IntradayPoints.Count);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteCloseDisplayPoint);
        Assert.Equal(2890.64, snapshot.IntradayPoints[^1].Price);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.IntradayPoints[0].Time, out DateTime firstEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
    }

    [Fact]
    public void BuildSnapshot_Ndx100FullUsSessionAcrossBeijingMidnightKeepsMorningPoints()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 29300);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.IntradayPoints.Count);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.IntradayPoints[0].Time, out DateTime firstEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
    }

    [Fact]
    public void BuildSnapshot_IndexIntradayMacdKeepsFullUsSessionAcrossBeijingMidnight()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 2800);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.IntradayPoints.Count);
        Assert.Equal(391, snapshot.Macd.Count);
        Assert.True(snapshot.MacdStatus.IsReady);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[0].Date, out DateTime firstEastern));
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[^1].Date, out DateTime lastEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
        Assert.Equal(new TimeSpan(16, 0, 0), lastEastern.TimeOfDay);
        Assert.Contains(snapshot.Macd, point =>
            IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(point.Date, out DateTime eastern)
            && eastern.TimeOfDay == new TimeSpan(10, 0, 0));
    }

    [Fact]
    public void BuildSnapshot_Ndx100IntradayMacdKeepsFullUsSessionAcrossBeijingMidnight()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 29300);
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.Macd.Count);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[0].Date, out DateTime firstEastern));
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[^1].Date, out DateTime lastEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
        Assert.Equal(new TimeSpan(16, 0, 0), lastEastern.TimeOfDay);
    }

    [Fact]
    public void BuildSnapshot_IndexQuoteCloseDisplayKeepsMacdFullSession()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 2800);
        points[^1].Price = 2889.64;
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2890.64, "2026-06-30 10:11:14", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Macd,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.Equal(391, snapshot.Macd.Count);
        Assert.True(snapshot.IntradayPoints[^1].IsQuoteCloseDisplayPoint);
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[0].Date, out DateTime firstEastern));
        Assert.True(IntradayTradingTimeAxis.TryConvertChinaTimeToUsEastern(snapshot.Macd[^1].Date, out DateTime lastEastern));
        Assert.Equal(new TimeSpan(9, 30, 0), firstEastern.TimeOfDay);
        Assert.Equal(new TimeSpan(16, 0, 0), lastEastern.TimeOfDay);
    }

    [Fact]
    public void BuildSnapshot_IndexIntradayKeepsVolumeUnavailableWhenSourceHasOnlyZeroVolume()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 2800)
            .Select(point =>
            {
                point.Volume = 0;
                return point;
            })
            .ToArray();
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.False(snapshot.VolumeStatus.IsReady);
        Assert.Equal("成交量数据不可用", snapshot.VolumeStatus.Message);
        Assert.All(snapshot.IntradayPoints, point => Assert.Equal(0, point.Volume));
    }

    [Fact]
    public void BuildSnapshot_IndexIntradayShowsVolumeWhenSourceHasRealVolume()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        IntradayPoint[] points = FullUsEasternSessionPoints(new DateTime(2026, 6, 29), 29300)
            .Select((point, index) =>
            {
                point.Volume = index == 0 ? 0 : 1000 + index;
                return point;
            })
            .ToArray();
        var cache = new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "真实指数分时缓存", true),
            DateTimeOffset.Parse("2026-06-30T09:30:00+08:00", CultureInfo.InvariantCulture));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            Array.Empty<MarketQuoteRecord>(),
            cache,
            null);

        Assert.True(snapshot.VolumeStatus.IsReady);
        Assert.Contains(snapshot.IntradayPoints, point => point.Volume > 0);
    }

    [Fact]
    public void BuildSnapshot_IndexQuoteNormalButNoIntradayCacheDoesNotPretendRealIntraday()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var failedCache = new ChartIntradayCacheEntry(
            Array.Empty<IntradayPoint>(),
            new ChartDataStatus(false, "指数分时接口熔断中，无可用分时缓存", IsCircuitOpen: true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2833.8, "2026-06-25 22:15:00", marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Intraday,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            failedCache,
            null);

        Assert.Single(snapshot.IntradayPoints);
        Assert.True(snapshot.HasQuoteTail);
        Assert.Equal(2833.8, snapshot.Quote?.Price);
        Assert.Equal("指数分时接口熔断中，无真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点", snapshot.MainStatus.Message);
        Assert.Equal("无分时", CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText(snapshot.MainStatus.Message));
        Assert.True(snapshot.MainStatus.IsCircuitOpen);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_NonTradingNoDataCatchUpDoesNotOpenCircuit()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-30T08:58:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(info, SecurityChartPeriod.Intraday, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueIntradayFailure(info.EastMoneySecId, "EastMoney intraday failed. secid=100.NDX100; no data.trends");

        for (int i = 0; i < 3; i++)
        {
            now = now.AddSeconds(21);
            await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);
        }

        ChartIntradayCacheEntry? entry = cache.GetIntraday("100.NDX100");
        Assert.NotNull(entry);
        Assert.False(entry.Status.IsCircuitOpen);
        Assert.False(entry.Status.IsReady);
        Assert.Contains("无可用", entry.Status.Message, StringComparison.Ordinal);
        Assert.Equal(1, fakeClient.GetIntradayRequestCount(info.EastMoneySecId));
    }

    [Fact]
    public void ChartCache_DoesNotReplaceDailyLikeWithFailedKLineStatus()
    {
        var cache = new ChartCache();
        KLinePoint[] daily = DailyKLines(220);

        cache.SaveDailyKLines("159941", daily, new ChartDataStatus(true, "鐪熷疄鏃鎺ュ彛缂撳瓨", true), DateTimeOffset.Now);
        cache.SaveDailyKLines("159941", Array.Empty<KLinePoint>(), new ChartDataStatus(false, "鎺ュ彛澶辫触锛屾棤鍙敤鏃缂撳瓨", IsCircuitOpen: true), DateTimeOffset.Now);

        ChartKLineCacheEntry? entry = cache.GetDailyKLines("159941");
        Assert.NotNull(entry);
        Assert.Equal(220, entry.Points.Count);
        Assert.True(entry.Status.IsReady);
        Assert.True(entry.Status.IsUsingCache);
        Assert.True(entry.Status.IsCircuitOpen);
        Assert.Equal("使用最近真实日K缓存", entry.Status.Message);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_RejectsMonthlyLikeApiResultForDailyKLine()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(info, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueHistorySuccess(info.EastMoneySecId, MonthlyHistoryPayload());

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        ChartKLineCacheEntry? entry = cache.GetDailyKLines("100.NDX100");
        Assert.NotNull(entry);
        Assert.Empty(entry.Points);
        Assert.False(entry.Status.IsReady);
        Assert.Equal("接口返回月线数据，未作为DailyLike日K使用", entry.Status.Message);
    }

    [Fact]
    public async Task ChartRefreshCoordinator_IndexDailyKLineRequestsIndexHistory()
    {
        var subscriptions = new ChartSubscriptionService();
        var cache = new ChartCache();
        var fakeClient = new FakeChartMarketDataClient();
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture);
        var coordinator = new ChartDataRefreshCoordinator(subscriptions, cache, fakeClient, nowProvider: () => now);
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        subscriptions.Subscribe(info, SecurityChartPeriod.Daily, SecurityChartSubPanel.Volume);
        fakeClient.EnqueueHistorySuccess(info.EastMoneySecId, DailyHistoryPayload(220));

        await coordinator.RefreshAsync(Array.Empty<MarketQuoteRecord>(), Array.Empty<MarketQuoteRecord>(), CancellationToken.None);

        Assert.False(fakeClient.GetLastHistoryIsEtf(info.EastMoneySecId));
        ChartKLineCacheEntry? entry = cache.GetDailyKLines("100.NDX100");
        Assert.NotNull(entry);
        Assert.NotEmpty(entry.Points);
    }

    [Fact]
    public void BuildSnapshot_MonthlyOnlyHistoryShowsNoDailyCacheStatus()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "绾虫寚ETF骞垮彂");
        MarketQuoteRecord monthlyHistory = History("159941", MonthlyHistoryPayload());

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { monthlyHistory },
            null,
            null);

        Assert.Empty(snapshot.KLines);
        Assert.False(snapshot.MainStatus.IsReady);
        Assert.Equal("无可用DailyLike日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_FallsBackToSqliteDailyLikeWithExplicitDailyCacheStatus()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        MarketQuoteRecord dailyHistory = History("159941", DailyHistoryPayload(220));

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { dailyHistory },
            null,
            null);

        Assert.NotEmpty(snapshot.KLines);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.Equal("使用最近真实日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_IndexUsesIndexQuoteAndDailyLikeHistory()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2883.45, "2026-06-22 10:01:00", marketType: "INDEX");
        MarketQuoteRecord dailyHistory = History("251.NDXTMC", DailyHistoryPayload(220), marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            new[] { dailyHistory },
            null,
            null);

        Assert.Equal(ChartInstrumentType.Index, snapshot.Security.InstrumentType);
        Assert.NotNull(snapshot.Quote);
        Assert.Equal(2883.45, snapshot.Quote!.Price);
        Assert.NotEmpty(snapshot.KLines);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.Equal("使用最近真实日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_IndexQuoteTimeUsesUsEasternTradingDateForDailyDisplayBar()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 23), 2800, 2860, 2790, 2850, 100),
                K(new DateTime(2026, 6, 24), 2850, 2870, 2820, 2853.45, 100)
            },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote(
            "251.NDXTMC",
            2851.11,
            "2026-06-26 00:01:00",
            lastClose: 2853.45,
            marketType: "INDEX",
            open: 2910.50,
            high: 2910.50,
            low: 2808.53);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(new DateTime(2026, 6, 25), snapshot.KLines[^1].Date);
        Assert.True(snapshot.KLines[^1].IsDisplayOnly);
        Assert.Equal("QUOTE_INTRADAY_BAR", snapshot.KLines[^1].PointSource);
        Assert.Equal(2910.50, snapshot.KLines[^1].Open);
        Assert.Equal(2910.50, snapshot.KLines[^1].High);
        Assert.Equal(2808.53, snapshot.KLines[^1].Low);
        Assert.Equal(2851.11, snapshot.KLines[^1].Close);
        Assert.Null(snapshot.KLines[^1].Volume);
        Assert.Contains("仅显示，不写缓存", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshot_IndexDailyDoesNotCreateTemporaryBarWhenQuoteOhlcMissing()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "纳指科技指数");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 23), 2800, 2860, 2790, 2850, 100),
                K(new DateTime(2026, 6, 24), 2850, 2870, 2820, 2853.45, 100)
            },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote("251.NDXTMC", 2851.11, "2026-06-26 00:01:00", lastClose: 2853.45, marketType: "INDEX");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(new DateTime(2026, 6, 24), snapshot.KLines[^1].Date);
        Assert.False(snapshot.KLines[^1].IsDisplayOnly);
        Assert.Contains("盘中OHLC字段不足", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshot_IndexWeeklyAndMonthlyIncludeDisplayOnlyIntradayBar()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 22), 29200, 29400, 29100, 29300, 100),
                K(new DateTime(2026, 6, 24), 29300, 29500, 29200, 29220.06, 100)
            },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote(
            "100.NDX100",
            29509.60,
            "2026-06-26 00:01:00",
            lastClose: 29220.06,
            marketType: "INDEX",
            open: 29843.89,
            high: 29843.89,
            low: 29000.55);

        SecurityChartSnapshot weekly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);
        SecurityChartSnapshot monthly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(new DateTime(2026, 6, 25), weekly.KLines[^1].Date);
        Assert.Equal(new DateTime(2026, 6, 25), monthly.KLines[^1].Date);
        Assert.True(weekly.KLines[^1].IsDisplayOnly);
        Assert.True(monthly.KLines[^1].IsDisplayOnly);
        Assert.Equal("QUOTE_INTRADAY_BAR", weekly.KLines[^1].PointSource);
        Assert.Equal("QUOTE_INTRADAY_BAR", monthly.KLines[^1].PointSource);
        Assert.Contains("仅显示，不写缓存", weekly.MainStatus.Message, StringComparison.Ordinal);
        Assert.Contains("仅显示，不写缓存", monthly.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshot_EtfDailyAppendsDisplayOnlyQuoteBarWhenDailyLikeIsOlder()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartKLineCacheEntry(
            new[] { K(new DateTime(2026, 6, 24), 1.65, 1.70, 1.62, 1.66, 100) },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote(
            "159941",
            1.69,
            "2026-06-25 14:30:00",
            lastClose: 1.66,
            open: 1.67,
            high: 1.70,
            low: 1.65);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(2, snapshot.KLines.Count);
        KLinePoint line = snapshot.KLines[^1];
        Assert.Equal(new DateTime(2026, 6, 25), line.Date);
        Assert.True(line.IsDisplayOnly);
        Assert.True(line.IsQuoteAdjusted);
        Assert.Equal("QUOTE_INTRADAY_BAR", line.PointSource);
        Assert.Equal(1.67, line.Open);
        Assert.Equal(1.70, line.High);
        Assert.Equal(1.65, line.Low);
        Assert.Equal(1.69, line.Close);
        Assert.Contains("仅显示，不写缓存", snapshot.MainStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshot_EtfWeeklyAndMonthlyIncludeDisplayOnlyQuoteBar()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 22), 1.60, 1.68, 1.58, 1.64, 100),
                K(new DateTime(2026, 6, 24), 1.64, 1.69, 1.61, 1.66, 100)
            },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote(
            "159941",
            1.69,
            "2026-06-25 14:30:00",
            lastClose: 1.66,
            open: 1.67,
            high: 1.70,
            low: 1.65);

        SecurityChartSnapshot weekly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Weekly,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);
        SecurityChartSnapshot monthly = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Monthly,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(new DateTime(2026, 6, 25), weekly.KLines[^1].Date);
        Assert.Equal(new DateTime(2026, 6, 25), monthly.KLines[^1].Date);
        Assert.True(weekly.KLines[^1].IsDisplayOnly);
        Assert.True(monthly.KLines[^1].IsDisplayOnly);
        Assert.Equal("QUOTE_INTRADAY_BAR", weekly.KLines[^1].PointSource);
        Assert.Equal("QUOTE_INTRADAY_BAR", monthly.KLines[^1].PointSource);
        Assert.Equal(1.69, weekly.KLines[^1].Close);
        Assert.Equal(1.69, monthly.KLines[^1].Close);
    }

    [Fact]
    public void BuildSnapshot_EtfDailyDoesNotDuplicateWhenDailyLikeAlreadyHasQuoteDate()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        var cache = new ChartKLineCacheEntry(
            new[]
            {
                K(new DateTime(2026, 6, 24), 1.65, 1.70, 1.62, 1.66, 100),
                K(new DateTime(2026, 6, 25), 1.66, 1.69, 1.64, 1.68, 100)
            },
            new ChartDataStatus(true, "使用最近真实日K缓存", true),
            DateTimeOffset.Now);
        MarketQuoteRecord quote = Quote(
            "159941",
            1.69,
            "2026-06-25 14:30:00",
            lastClose: 1.66,
            open: 1.67,
            high: 1.70,
            low: 1.65);

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            new[] { quote },
            Array.Empty<MarketQuoteRecord>(),
            null,
            cache);

        Assert.Equal(2, snapshot.KLines.Count);
        Assert.Equal(new DateTime(2026, 6, 25), snapshot.KLines[^1].Date);
        Assert.True(snapshot.KLines[^1].IsDisplayOnly);
        Assert.Equal("QUOTE_INTRADAY_BAR", snapshot.KLines[^1].PointSource);
        Assert.Single(snapshot.KLines.Where(line => line.Date.Date == new DateTime(2026, 6, 25)));
    }

    [Fact]
    public void BuildSnapshot_DisplayOnlyQuoteBarCoversConfiguredEtfsAndIndexes()
    {
        (ChartSecurityInfo Info, MarketQuoteRecord Quote, DateTime ExpectedDate)[] cases =
        [
            (ChartDataService.CreateSecurityInfo("159509", "ETF"), Quote("159509", 2.62, "2026-06-25 14:30:00", open: 2.60, high: 2.64, low: 2.58), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("159941", "ETF"), Quote("159941", 1.69, "2026-06-25 14:30:00", open: 1.67, high: 1.70, low: 1.65), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("159513", "ETF"), Quote("159513", 1.83, "2026-06-25 14:30:00", open: 1.80, high: 1.84, low: 1.79), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("159660", "ETF"), Quote("159660", 2.39, "2026-06-25 14:30:00", open: 2.35, high: 2.40, low: 2.34), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("159501", "ETF"), Quote("159501", 2.07, "2026-06-25 14:30:00", open: 2.05, high: 2.08, low: 2.04), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("159659", "ETF"), Quote("159659", 2.36, "2026-06-25 14:30:00", open: 2.34, high: 2.37, low: 2.33), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("513100", "ETF"), Quote("513100", 2.21, "2026-06-25 14:30:00", open: 2.18, high: 2.22, low: 2.17), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateSecurityInfo("513300", "ETF"), Quote("513300", 2.71, "2026-06-25 14:30:00", open: 2.68, high: 2.72, low: 2.67), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateIndexSecurityInfo("251.NDXTMC", "Index"), Quote("251.NDXTMC", 2851.11, "2026-06-26 00:01:00", marketType: "INDEX", open: 2910.50, high: 2910.50, low: 2808.53), new DateTime(2026, 6, 25)),
            (ChartDataService.CreateIndexSecurityInfo("100.NDX100", "Index"), Quote("100.NDX100", 29509.60, "2026-06-26 00:01:00", marketType: "INDEX", open: 29843.89, high: 29843.89, low: 29000.55), new DateTime(2026, 6, 25))
        ];

        foreach ((ChartSecurityInfo info, MarketQuoteRecord quote, DateTime expectedDate) in cases)
        {
            var cache = new ChartKLineCacheEntry(
                new[] { K(new DateTime(2026, 6, 24), 1, 2, 0.8, 1.5, 100) },
                new ChartDataStatus(true, "使用最近真实日K缓存", true),
                DateTimeOffset.Now);

            SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
                info,
                SecurityChartPeriod.Daily,
                SecurityChartSubPanel.Volume,
                new[] { quote },
                Array.Empty<MarketQuoteRecord>(),
                null,
                cache);

            KLinePoint tail = snapshot.KLines[^1];
            Assert.Equal(expectedDate, tail.Date);
            Assert.True(tail.IsDisplayOnly);
            Assert.Equal("QUOTE_INTRADAY_BAR", tail.PointSource);
        }
    }

    [Fact]
    public void BuildSnapshot_IndexDoesNotUseEtfHistoryAsIndexDailyK()
    {
        ChartSecurityInfo info = ChartDataService.CreateIndexSecurityInfo("100.NDX100", "纳斯达克100");
        MarketQuoteRecord etfHistoryWithSameSymbol = History("100.NDX100", DailyHistoryPayload(220), marketType: "ETF");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { etfHistoryWithSameSymbol },
            null,
            null);

        Assert.Empty(snapshot.KLines);
        Assert.False(snapshot.MainStatus.IsReady);
        Assert.Equal("无可用DailyLike日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void BuildSnapshot_UsesOlderSqliteDailyLikeWhenLatestHistoryIsMonthlyLike()
    {
        ChartSecurityInfo info = ChartDataService.CreateSecurityInfo("159941", "纳指ETF广发");
        MarketQuoteRecord monthlyHistory = History("159941", MonthlyHistoryPayload(), "2026-06-22 13:00:00");
        MarketQuoteRecord olderDailyHistory = History("159941", DailyHistoryPayload(220), "2026-06-22 10:00:00");

        SecurityChartSnapshot snapshot = ChartDataService.BuildSnapshot(
            info,
            SecurityChartPeriod.Daily,
            SecurityChartSubPanel.Volume,
            Array.Empty<MarketQuoteRecord>(),
            new[] { monthlyHistory, olderDailyHistory },
            null,
            null);

        Assert.NotEmpty(snapshot.KLines);
        Assert.True(snapshot.MainStatus.IsReady);
        Assert.Equal("使用最近真实日K缓存", snapshot.MainStatus.Message);
    }

    [Fact]
    public void SecurityChartWindow_CompactStatusTextKeepsHeaderShort()
    {
        Assert.Equal(
            "接口限频",
            CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText("分时接口限频中，使用最近真实分时缓存 + 实时 quote 尾点"));
        Assert.Equal(
            "接口熔断",
            CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText("分时接口熔断中，使用最近真实分时缓存 + 实时 quote 尾点"));
        Assert.Equal(
            "无分时",
            CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText("指数分时接口熔断中，无真实分时缓存；最新价来自实时quote，仅作独立标记，缺少中间分钟分时点"));
        Assert.Equal(
            "日K缓存",
            CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText("使用最近真实日K缓存"));
        Assert.Equal(
            "无日K",
            CrossETF.Terminal.UiShell.Reference.Views.SecurityChartWindow.CompactStatusText("无可用DailyLike日K缓存"));
    }

    [Fact]
    public void SecurityChartWindow_UsesShortStatusTooltipAndIndexPremiumPlaceholder()
    {
        string xaml = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml"));
        string code = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusText.ToolTip = snapshot.MainStatus.Message", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Security.InstrumentType == ChartInstrumentType.Index", code, StringComparison.Ordinal);
        Assert.Contains("PremiumText.Text = \"--\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IndexDrawdownCanOpenIndexChartOnDoubleClick()
    {
        string xaml = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml"));
        string code = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml.cs"));

        Assert.Contains("MouseLeftButtonDown=\"LeftChartCanvas_MouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonDown=\"RightChartCanvas_MouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateIndexSecurityInfo(indexSymbol, indexName)", code, StringComparison.Ordinal);
        Assert.Contains("IndexDrawdownChartSeriesBuilder.LeftChartSymbol", code, StringComparison.Ordinal);
        Assert.Contains("IndexDrawdownChartSeriesBuilder.RightChartSymbol", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChartWindowManager_GuardsClosedChartWindowCallbacks()
    {
        string source = File.ReadAllText(FindWorkspaceFile("Views", "ChartWindowManager.cs"));
        string windowSource = File.ReadAllText(FindWorkspaceFile("Views", "SecurityChartWindow.xaml.cs"));

        Assert.Contains("window.IsClosed", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, window)", source, StringComparison.Ordinal);
        Assert.Contains("ObjectDisposedException", source, StringComparison.Ordinal);
        Assert.Contains("public bool IsClosed", windowSource, StringComparison.Ordinal);
        Assert.Contains("protected override void OnClosed", windowSource, StringComparison.Ordinal);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(parts);
    }

    private static KLinePoint K(DateTime date, double open, double high, double low, double close, double? volume, double? amount = null)
        => new()
        {
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            Amount = amount
        };

    private static KLinePoint[] DailyKLines(int count, double closeOffset = 0)
        => DailyKLines(count, new DateTime(2026, 1, 1), closeOffset);

    private static KLinePoint[] DailyKLines(int count, DateTime startDate, double closeOffset = 0)
        => Enumerable.Range(0, count)
            .Select(index => K(
                startDate.AddDays(index),
                1 + closeOffset + index * 0.001,
                1.02 + closeOffset + index * 0.001,
                0.99 + closeOffset + index * 0.001,
                1.01 + closeOffset + index * 0.001,
                100 + index,
                1000 + index))
            .ToArray();

    private static string DailyHistoryPayload(int count)
        => DailyHistoryPayload(count, new DateTime(2026, 1, 1));

    private static string DailyHistoryPayload(int count, DateTime startDate, double closeOffset = 0)
        => HistoryPayload(Enumerable.Range(0, count)
            .Select(index => startDate.AddDays(index)),
            closeOffset);

    private static string MonthlyHistoryPayload()
        => HistoryPayload(Enumerable.Range(0, 12)
            .Select(index => new DateTime(2025, 1, 31).AddMonths(index)));

    private static string IntradayPayload(DateTime date, int count)
    {
        string[] trends = Enumerable.Range(0, count)
            .Select(index =>
            {
                DateTime time = date.Date.Add(IntradayTradingTimeAxis.MorningOpen).AddMinutes(index);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "\"{0:yyyy-MM-dd HH:mm},{1:0.000},{1:0.000},{2:0.000},{3:0.000},{4},{5},{1:0.000}\"",
                    time,
                    1.60 + index * 0.001,
                    1.61 + index * 0.001,
                    1.59 + index * 0.001,
                    100 + index,
                    1000 + index);
            })
            .ToArray();
        return "{\"data\":{\"trends\":[" + string.Join(",", trends) + "]}}";
    }

    private static string IndexIntradayPayload(params DateTime[] times)
    {
        string[] trends = times.Select((time, index) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "\"{0:yyyy-MM-dd HH:mm},{1:0.00},{1:0.00},{2:0.00},{3:0.00},0,0,{1:0.00}\"",
                time,
                2880 + index,
                2885 + index,
                2875 + index)).ToArray();
        return "{\"data\":{\"trends\":[" + string.Join(",", trends) + "]}}";
    }

    private static string HistoryPayload(IEnumerable<DateTime> dates, double closeOffset = 0)
    {
        string[] klines = dates.Select((date, index) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "\"{0:yyyy-MM-dd},{1:0.000},{2:0.000},{3:0.000},{4:0.000},{5},{6}\"",
                date,
                1 + closeOffset + index * 0.001,
                1.01 + closeOffset + index * 0.001,
                1.02 + closeOffset + index * 0.001,
                0.99 + closeOffset + index * 0.001,
                100 + index,
                1000 + index)).ToArray();
        return "{\"data\":{\"klines\":[" + string.Join(",", klines) + "]}}";
    }

    private static IntradayPoint Intraday(DateTime time, double price, double? volume = 100)
        => new()
        {
            Time = time,
            Price = price,
            Volume = volume
        };

    private static IntradayPoint[] FullTradingDayPoints(DateTime date)
    {
        var points = new List<IntradayPoint>();
        DateTime cursor = date.Date.Add(IntradayTradingTimeAxis.MorningOpen);
        DateTime morningClose = date.Date.Add(IntradayTradingTimeAxis.MorningClose);
        while (cursor <= morningClose)
        {
            points.Add(Intraday(cursor, 1.60 + points.Count * 0.0001, 100 + points.Count));
            cursor = cursor.AddMinutes(1);
        }

        cursor = date.Date.Add(IntradayTradingTimeAxis.AfternoonOpen);
        DateTime afternoonClose = date.Date.Add(IntradayTradingTimeAxis.AfternoonClose);
        while (cursor <= afternoonClose)
        {
            points.Add(Intraday(cursor, 1.60 + points.Count * 0.0001, 100 + points.Count));
            cursor = cursor.AddMinutes(1);
        }

        return points.ToArray();
    }

    private static IntradayPoint[] IntradaySeries(int count)
        => Enumerable.Range(0, count)
            .Select(index => Intraday(
                new DateTime(2026, 6, 19, 9, 30, 0).AddMinutes(index),
                1.60 + Math.Sin(index / 5d) * 0.01 + index * 0.0005,
                100 + index))
            .ToArray();

    private static IntradayPoint[] IndexIntradaySeries(int count)
        => Enumerable.Range(0, count)
            .Select(index => Intraday(
                new DateTime(2026, 6, 22, 21, 30, 0).AddMinutes(index),
                2880 + Math.Sin(index / 4d) * 8 + index * 0.8,
                null))
            .ToArray();

    private static IntradayPoint[] FullUsEasternSessionPoints(DateTime easternTradeDate, double basePrice)
    {
        var points = new List<IntradayPoint>();
        DateTime eastern = easternTradeDate.Date.Add(IntradayTradingTimeAxis.UsEasternOpen);
        DateTime close = easternTradeDate.Date.Add(IntradayTradingTimeAxis.UsEasternClose);
        while (eastern <= close)
        {
            Assert.True(IntradayTradingTimeAxis.TryConvertUsEasternToChina(eastern, out DateTime china));
            points.Add(Intraday(
                china,
                basePrice + points.Count * 0.1,
                null));
            eastern = eastern.AddMinutes(1);
        }

        return points.ToArray();
    }

    private static MarketQuoteRecord History(
        string symbol,
        string rawPayload,
        string receivedAt = "2026-06-19 10:00:00",
        string marketType = "ETF")
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = MarketSources.EastMoneyHistory,
            RawPayload = rawPayload,
            ReceivedAt = receivedAt
        };

    private static MarketQuoteRecord Quote(
        string symbol,
        double price,
        string time,
        double? lastClose = null,
        string marketType = "ETF",
        double? open = null,
        double? high = null,
        double? low = null)
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = MarketSources.Tencent,
            Price = price,
            LastClose = lastClose,
            ChangePercent = 1.2,
            OpenValue = open,
            HighValue = high,
            LowValue = low,
            QuoteTime = time,
            ReceivedAt = time
        };

    private sealed class FakeChartMarketDataClient : IChartMarketDataClient
    {
        private readonly Dictionary<string, Queue<Func<Task<EastMoneyIntradayFetchResult>>>> _intraday = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<Func<Task<EastMoneyHistoryFetchResult>>>> _history = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _intradayRequestCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _lastHistoryIsEtfRequests = new(StringComparer.OrdinalIgnoreCase);

        public void EnqueueIntradayFailure(string secId, string message)
            => Enqueue(_intraday, secId, () => Task.FromException<EastMoneyIntradayFetchResult>(new InvalidOperationException(message)));

        public void EnqueueIntradaySuccess(string secId, IReadOnlyList<IntradayPoint> points)
            => Enqueue(_intraday, secId, () => Task.FromResult(new EastMoneyIntradayFetchResult(secId, "{}", points, DateTimeOffset.Now)));

        public void EnqueueIntradaySuccessPayload(string secId, string rawPayload)
            => Enqueue(_intraday, secId, () => Task.FromResult(new EastMoneyIntradayFetchResult(
                secId,
                rawPayload,
                EastMoneyIntradayParser.ParsePoints(rawPayload),
                DateTimeOffset.Now)));

        public void EnqueueHistorySuccess(string secId, string rawPayload)
            => Enqueue(_history, secId, () => Task.FromResult(new EastMoneyHistoryFetchResult
            {
                SecId = secId,
                Fqt = 1,
                Klt = 101,
                RawPayload = rawPayload,
                PointCount = EastMoneyHistoryParser.ParsePoints(rawPayload).Count
            }));

        public int GetIntradayRequestCount(string secId)
            => _intradayRequestCounts.TryGetValue(secId, out int count) ? count : 0;

        public bool? GetLastHistoryIsEtf(string secId)
            => _lastHistoryIsEtfRequests.TryGetValue(secId, out bool isEtf) ? isEtf : null;

        public Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
        {
            _intradayRequestCounts[secId] = GetIntradayRequestCount(secId) + 1;
            if (_intraday.TryGetValue(secId, out Queue<Func<Task<EastMoneyIntradayFetchResult>>>? queue)
                && queue.Count > 0)
            {
                return queue.Dequeue()();
            }

            return Task.FromResult(new EastMoneyIntradayFetchResult(
                secId,
                "{}",
                new[] { Intraday(new DateTime(2026, 6, 22, 10, 1, 0), 1.68) },
                DateTimeOffset.Now));
        }

        public Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
        {
            string alias = MarketSymbolNormalizer.NormalizeEastMoneyEtfSecId(tencentCode);
            _intradayRequestCounts[alias] = GetIntradayRequestCount(alias) + 1;
            if (_intraday.TryGetValue(tencentCode, out Queue<Func<Task<EastMoneyIntradayFetchResult>>>? directQueue)
                && directQueue.Count > 0)
            {
                return directQueue.Dequeue()();
            }

            if (_intraday.TryGetValue(alias, out Queue<Func<Task<EastMoneyIntradayFetchResult>>>? aliasQueue)
                && aliasQueue.Count > 0)
            {
                return aliasQueue.Dequeue()();
            }

            return Task.FromResult(new EastMoneyIntradayFetchResult(
                tencentCode,
                "{}",
                new[] { Intraday(new DateTime(2026, 6, 22, 10, 1, 0), 1.68) },
                DateTimeOffset.Now));
        }

        public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(
            string secId,
            bool isEtf,
            bool preferDaily,
            CancellationToken cancellationToken)
        {
            _lastHistoryIsEtfRequests[secId] = isEtf;
            if (_history.TryGetValue(secId, out Queue<Func<Task<EastMoneyHistoryFetchResult>>>? queue)
                && queue.Count > 0)
            {
                return queue.Dequeue()();
            }

            string payload = DailyHistoryPayload(220);
            return Task.FromResult(new EastMoneyHistoryFetchResult
            {
                SecId = secId,
                Fqt = 1,
                Klt = 101,
                RawPayload = payload,
                PointCount = 220
            });
        }

        public Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken)
        {
            string alias = MarketSymbolNormalizer.NormalizeEastMoneyEtfSecId(tencentCode);
            if (_history.TryGetValue(tencentCode, out Queue<Func<Task<EastMoneyHistoryFetchResult>>>? directQueue)
                && directQueue.Count > 0)
            {
                return directQueue.Dequeue()();
            }

            if (_history.TryGetValue(alias, out Queue<Func<Task<EastMoneyHistoryFetchResult>>>? aliasQueue)
                && aliasQueue.Count > 0)
            {
                return aliasQueue.Dequeue()();
            }

            string payload = DailyHistoryPayload(220);
            return Task.FromResult(new EastMoneyHistoryFetchResult
            {
                SecId = tencentCode,
                Fqt = 1,
                Klt = 101,
                RawPayload = payload,
                PointCount = 220
            });
        }

        public void Dispose()
        {
        }

        private static void Enqueue<T>(
            Dictionary<string, Queue<Func<Task<T>>>> target,
            string key,
            Func<Task<T>> factory)
        {
            if (!target.TryGetValue(key, out Queue<Func<Task<T>>>? queue))
            {
                queue = new Queue<Func<Task<T>>>();
                target[key] = queue;
            }

            queue.Enqueue(factory);
        }
    }

    private sealed class FakeChartIntradayCacheStore : IChartIntradayCacheStore
    {
        public ChartIntradayCacheEntry? Entry { get; set; }

        public int SaveCount { get; private set; }

        public string? SavedStrategyCode { get; private set; }

        public string? SavedPayload { get; private set; }

        public ChartIntradayCacheEntry? ReadLatestChartIntradayCache(string strategyCode)
            => Entry;

        public void SaveChartIntradayCache(
            string strategyCode,
            string? actualCode,
            string rawPayload,
            DateTimeOffset fetchedAt,
            string source = "EASTMONEY_INTRADAY",
            string quality = "REAL_TRENDS2")
        {
            SaveCount++;
            SavedStrategyCode = strategyCode;
            SavedPayload = rawPayload;
        }
    }
}

