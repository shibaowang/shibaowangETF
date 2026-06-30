using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class MarketSymbolNormalizerTests
{
    [Theory]
    [InlineData("159941", "sz159941", "0.159941")]
    [InlineData("513100", "sh513100", "1.513100")]
    [InlineData("588000", "sh588000", "1.588000")]
    public void EtfCodes_AreNormalizedForTencentAndEastMoney(string code, string tencentCode, string eastMoneySecId)
    {
        Assert.Equal(tencentCode, MarketSymbolNormalizer.NormalizeTencentEtf(code).RawCode);
        Assert.Equal(eastMoneySecId, MarketSymbolNormalizer.NormalizeEastMoneyEtfSecId(code));
    }

    [Fact]
    public void EastMoneySecId_IsKeptAsConfigured()
    {
        var item = MarketSymbolNormalizer.NormalizeEastMoneySecId("100.NDX", "纳斯达克100", "INDEX");

        Assert.Equal("100.NDX", item.Symbol);
        Assert.Equal("100.NDX", item.RawCode);
    }

    [Theory]
    [InlineData("sz.159941", false, "0.159941")]
    [InlineData("sh.513100", false, "1.513100")]
    [InlineData("159941.SZ", false, "0.159941")]
    [InlineData("513100.SH", false, "1.513100")]
    [InlineData(" ' 251.NDXTMC ' ", true, "251.NDXTMC")]
    [InlineData("\"100.NDX100\"", true, "100.NDX100")]
    [InlineData("　100.NDX　", true, "100.NDX")]
    [InlineData("159941", false, "0.159941")]
    [InlineData("513100", false, "1.513100")]
    [InlineData("399001", true, "0.399001")]
    [InlineData("000300", true, "1.000300")]
    [InlineData("880001", true, "1.880001")]
    [InlineData("688001", true, "1.688001")]
    [InlineData("123456", true, "0.123456")]
    [InlineData("0.399006", true, "0.399006")]
    [InlineData("100.NDX", true, "100.NDX")]
    [InlineData("100.NDX100", true, "100.NDX100")]
    [InlineData("116.HSI", true, "116.HSI")]
    [InlineData("251.NDXTMC", true, "251.NDXTMC")]
    public void EastMoneySecId_NormalizesByVbaRules(string rawCode, bool preferIndex, string expected)
    {
        Assert.Equal(expected, MarketSymbolNormalizer.NormalizeEastMoneySecId(rawCode, preferIndex));
    }

    [Theory]
    [InlineData("251.NDXTMC", "NDXTMC")]
    [InlineData("100.NDX100", "NDX100")]
    [InlineData("100.NDX", "NDX")]
    [InlineData("116.HSI", "HSI")]
    [InlineData("1.000300", "000300")]
    [InlineData("0.399001", "399001")]
    public void EastMoneyTargetCode_IsExtractedAfterDot(string secId, string expected)
    {
        Assert.Equal(expected, MarketSymbolNormalizer.ExtractEastMoneyTargetCode(secId));
    }

    [Fact]
    public void DefaultTopBarItems_UseConfiguredNasdaqSecIds()
    {
        string[] rawCodes = MarketSymbolNormalizer.DefaultTopBarItems()
            .Select(item => item.RawCode)
            .ToArray();

        Assert.Contains("100.NDX100", rawCodes);
        Assert.Contains("251.NDXTMC", rawCodes);
    }
}
