using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class PercentValueParserTests
{
    [Theory]
    [InlineData("40%", 0.40)]
    [InlineData("40", 0.40)]
    [InlineData("0.40", 0.40)]
    [InlineData("8%", 0.08)]
    [InlineData("2", 0.02)]
    [InlineData("0.02", 0.02)]
    public void ParsePercentInput_NormalizesPercentStorage(string input, double expected)
    {
        bool ok = PercentValueParser.TryParsePercentInput(input, out double? value, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, value!.Value, 4);
    }

    [Fact]
    public void ParsePercentInput_InvalidInputReturnsError()
    {
        bool ok = PercentValueParser.TryParsePercentInput("abc%", out double? value, out string? error);

        Assert.False(ok);
        Assert.Null(value);
        Assert.Contains("百分比格式无效", error);
    }

    [Theory]
    [InlineData(0.40, "40%")]
    [InlineData(0.08, "8%")]
    [InlineData(40, "40%")]
    public void FormatPercent_DisplaysPercentText(double value, string expected)
    {
        Assert.Equal(expected, PercentValueParser.FormatPercent(value));
    }
}
