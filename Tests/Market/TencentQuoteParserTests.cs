using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public class TencentQuoteParserTests
{
    [Fact]
    public void Parse_KeepsTencentIopvWhenPresent()
    {
        string payload = "v_sh159941=\"0~ETF~159941~1.05~1.00~1.01~1.00~~~~CNY~~~~~~~~~~~~~~~~~~~~20260614103000~~~1.06~1.04~~1000~2000\";";

        var quote = Assert.Single(TencentQuoteParser.Parse(payload, DateTimeOffset.Parse("2026-06-14 10:30:00")));

        Assert.Equal(1.05, quote.Price);
        Assert.Equal(1.00, quote.Iopv);
    }

    [Fact]
    public void Parse_DoesNotFallbackMissingIopvToPrice()
    {
        string payload = "v_sh159941=\"0~ETF~159941~1.05~1.00~1.01~~~~~~~~~~~~~~~~~~~~~~~~20260614103000~~~1.06~1.04~~1000~2000\";";

        var quote = Assert.Single(TencentQuoteParser.Parse(payload, DateTimeOffset.Parse("2026-06-14 10:30:00")));

        Assert.Equal(1.05, quote.Price);
        Assert.Null(quote.Iopv);
    }
}
