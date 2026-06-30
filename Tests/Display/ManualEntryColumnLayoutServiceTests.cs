using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class ManualEntryColumnLayoutServiceTests
{
    [Fact]
    public void SerializeOrder_RoundTripsUserOrder()
    {
        string[] defaults = ["id", "strategy_code", "otc_code", "class_type", "enabled"];
        string[] userOrder = ["strategy_code", "class_type", "id", "otc_code", "enabled"];

        string saved = ManualEntryColumnLayoutService.SerializeOrder(userOrder, defaults);
        IReadOnlyList<string> restored = ManualEntryColumnLayoutService.ResolveOrder(defaults, saved);

        Assert.Equal(userOrder, restored);
    }

    [Fact]
    public void ResolveOrder_IgnoresUnknownColumnsAndAppendsMissingDefaults()
    {
        string[] defaults = ["id", "strategy_code", "otc_code", "class_type", "enabled"];

        IReadOnlyList<string> restored = ManualEntryColumnLayoutService.ResolveOrder(defaults, "strategy_code,unknown_col,id");

        Assert.Equal(["strategy_code", "id", "otc_code", "class_type", "enabled"], restored);
    }

    [Fact]
    public void ResolveOrder_AppendsNewColumnsToOldConfiguration()
    {
        string[] defaults = ["id", "strategy_code", "otc_code", "class_type"];

        IReadOnlyList<string> restored = ManualEntryColumnLayoutService.ResolveOrder(defaults, "id,strategy_code");

        Assert.Equal(defaults, restored);
    }

    [Fact]
    public void BuildSettingKey_UsesIndependentTabKeys()
    {
        string otcKey = ManualEntryColumnLayoutService.BuildSettingKey("otc_map");
        string tradeLogKey = ManualEntryColumnLayoutService.BuildSettingKey("trade_log");

        Assert.NotEqual(otcKey, tradeLogKey);
        Assert.StartsWith(ManualEntryColumnLayoutService.SettingKeyPrefix, otcKey);
        Assert.StartsWith(ManualEntryColumnLayoutService.SettingKeyPrefix, tradeLogKey);
    }
}
