using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.TradeLog;

public class TradeLogSchemaTests
{
    [Fact]
    public void All15Columns_Pass()
    {
        var validator = new TradeLogSchemaValidator();
        var headers = TradeLogEntry.RequiredHeaders.ToList();
        bool result = validator.Validate(headers);
        Assert.True(result);
        Assert.Empty(validator.Errors);
    }

    [Fact]
    public void MissingColumn_Fails()
    {
        var validator = new TradeLogSchemaValidator();
        var headers = TradeLogEntry.RequiredHeaders.Take(14).ToList();
        bool result = validator.Validate(headers);
        Assert.False(result);
        Assert.NotEmpty(validator.Errors);
        Assert.Contains(validator.Errors, e => e.Message.Contains("缺失"));
    }

    [Fact]
    public void DuplicateKeyColumn_Fails()
    {
        var validator = new TradeLogSchemaValidator();
        var headers = TradeLogEntry.RequiredHeaders.ToList();
        headers.Add("时间"); // duplicate
        bool result = validator.Validate(headers);
        Assert.False(result);
        Assert.Contains(validator.Errors, e => e.Message.Contains("重复"));
    }

    [Fact]
    public void ExtraColumn_DoesNotFail()
    {
        var validator = new TradeLogSchemaValidator();
        var headers = TradeLogEntry.RequiredHeaders.ToList();
        headers.Add("自定义列");
        bool result = validator.Validate(headers);
        Assert.True(result);
        Assert.Empty(validator.Errors);
    }

    [Fact]
    public void EmptyHeaders_Fails()
    {
        var validator = new TradeLogSchemaValidator();
        var headers = new List<string>();
        bool result = validator.Validate(headers);
        Assert.False(result);
    }

    [Fact]
    public void NullHeaders_Fails()
    {
        var validator = new TradeLogSchemaValidator();
        bool result = validator.Validate(null!);
        Assert.False(result);
    }
}
