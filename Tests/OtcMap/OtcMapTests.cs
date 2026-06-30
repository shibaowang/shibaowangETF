using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Mocks;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.OtcMap;

public class OtcMapTests
{
    private static List<OtcChannel> CreateSampleChannels()
    {
        return new List<OtcChannel>
        {
            new() { StrategyCode = "159509", OtcCode = "017091", ClassName = "A类", Enabled = true, DailyLimit = 50000, Priority = 1, MinBuyAmount = 100 },
            new() { StrategyCode = "159509", OtcCode = "017092", ClassName = "C类", Enabled = true, DailyLimit = 100000, Priority = 2, MinBuyAmount = 100 },
            new() { StrategyCode = "159509", OtcCode = "017093", ClassName = "C类", Enabled = false, DailyLimit = 50000, Priority = 3, MinBuyAmount = 100 },
            new() { StrategyCode = "513100", OtcCode = "017094", ClassName = "A类", Enabled = true, DailyLimit = 30000, Priority = 1, MinBuyAmount = 100 }
        };
    }

    [Fact]
    public void OtcChannel_Has8Columns()
    {
        var ch = new OtcChannel
        {
            StrategyCode = "159509",
            OtcCode = "017091",
            ClassName = "A类",
            Enabled = true,
            DailyLimit = 50000,
            Priority = 1,
            MinBuyAmount = 100,
            Memo = "测试"
        };

        Assert.Equal("159509", ch.StrategyCode);
        Assert.Equal("017091", ch.OtcCode);
        Assert.Equal("A类", ch.ClassName);
        Assert.True(ch.Enabled);
        Assert.Equal(50000, ch.DailyLimit);
        Assert.Equal(1, ch.Priority);
        Assert.Equal(100, ch.MinBuyAmount);
        Assert.Equal("测试", ch.Memo);
    }

    [Fact]
    public void DisabledChannel_NotIncluded()
    {
        var channels = CreateSampleChannels();
        var service = new OtcMapService(channels);

        Assert.Equal(3, service.EnabledChannels.Count);
        Assert.DoesNotContain(service.EnabledChannels, c => c.OtcCode == "017093");
    }

    [Fact]
    public void BuyLegs_RespectsPriority()
    {
        var channels = CreateSampleChannels();
        var service = new OtcMapService(channels);
        var navPrices = new Dictionary<string, double> { ["017091"] = 1.284, ["017092"] = 1.285 };
        var todayBought = new Dictionary<string, double>();

        var legs = service.BuildBuyLegs("159509", 60000, 100000, "狙击一档", "测试", todayBought, navPrices);

        Assert.NotEmpty(legs);
        // Priority 1 (A类) should be used first
        Assert.Equal("017091", legs[0].ActualCode);
    }

    [Fact]
    public void BuyLegs_RespectsDailyLimit()
    {
        var service = new OtcMapService(CreateSampleChannels());
        var navPrices = new Dictionary<string, double> { ["017091"] = 1.284, ["017092"] = 1.285 };
        var todayBought = new Dictionary<string, double> { ["159509|017091"] = 45000 }; // A类已买45000

        var legs = service.BuildBuyLegs("159509", 30000, 100000, "狙击一档", "测试", todayBought, navPrices);

        // A类剩余只有5000，不够，应该跳到C类
        Assert.NotEmpty(legs);
        // First leg should be limited by remaining daily limit
        double totalAmt = legs.Sum(l => l.Amount);
        Assert.True(totalAmt <= 30000);
    }

    [Fact]
    public void BuyLegs_RespectsMinBuy()
    {
        var channels = new List<OtcChannel>
        {
            new() { StrategyCode = "159509", OtcCode = "017091", ClassName = "A类", Enabled = true, DailyLimit = 0, Priority = 1, MinBuyAmount = 10000 }
        };
        var service = new OtcMapService(channels);
        var navPrices = new Dictionary<string, double> { ["017091"] = 1.284 };
        var todayBought = new Dictionary<string, double>();

        var legs = service.BuildBuyLegs("159509", 50, 100000, "狙击一档", "测试", todayBought, navPrices);
        // 50 < 10000 min buy -> empty
        Assert.Empty(legs);
    }

    [Fact]
    public void CClass_IsSellPriority()
    {
        var channels = CreateSampleChannels();
        var cClassChannel = channels[1]; // 017092, C类, Priority=2
        Assert.True(cClassChannel.IsCClass);
        Assert.True(cClassChannel.IsSellPriority);
    }

    [Fact]
    public void SellLegs_CClassFirst()
    {
        var channels = CreateSampleChannels();
        var service = new OtcMapService(channels);
        // Register holdings for both A and C class
        service.RegisterHolding("159509", "017091", 50000, 60000); // A类
        service.RegisterHolding("159509", "017092", 80000, 100000); // C类

        var navPrices = new Dictionary<string, double> { ["017091"] = 1.30, ["017092"] = 1.30 };

        var legs = service.BuildSellLegs("159509", 50000, navPrices);

        Assert.NotEmpty(legs);
        // C类 (Priority=2) should be sold first
        Assert.Equal("017092", legs[0].ActualCode);
        Assert.True(legs[0].ClassName.Contains("C类") || legs[0].ClassName == "C类");
    }

    [Fact]
    public void SellLegs_OnlySells_RealHoldings()
    {
        var channels = CreateSampleChannels();
        var service = new OtcMapService(channels);
        // Only register A类 holding, not C类
        service.RegisterHolding("159509", "017091", 50000, 60000);

        var navPrices = new Dictionary<string, double> { ["017091"] = 1.30, ["017092"] = 1.30 };

        var legs = service.BuildSellLegs("159509", 50000, navPrices);

        Assert.NotEmpty(legs);
        // Should only contain 017091 (A类), since C类 has no real holding
        Assert.All(legs, l => Assert.Equal("017091", l.ActualCode));
    }

    [Fact]
    public void MultiLegSell_CostPart_PerActualCode()
    {
        var channels = CreateSampleChannels();
        var service = new OtcMapService(channels);
        service.RegisterHolding("159509", "017091", 50000, 60000); // A类: 1.20 avg
        service.RegisterHolding("159509", "017092", 80000, 100000); // C类: 1.25 avg

        var navPrices = new Dictionary<string, double> { ["017091"] = 1.30, ["017092"] = 1.30 };

        var legs = service.BuildSellLegs("159509", 80000, navPrices);

        foreach (var leg in legs)
        {
            Assert.True(leg.CostPart > 0);
            Assert.True(leg.CostPart <= leg.Amount);
        }
    }
}
