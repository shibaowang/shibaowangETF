using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class EtfDecisionDisplayAnimationHelperTests
{
    [Fact]
    public void DetectValueChange_ReturnsUpForIncreasingDisplayValue()
    {
        EtfValueChangeDirection direction = EtfDecisionDisplayAnimationHelper.DetectValueChange("1.00", "1.01");

        Assert.Equal(EtfValueChangeDirection.Up, direction);
    }

    [Fact]
    public void DetectValueChange_ReturnsDownForDecreasingDisplayValue()
    {
        EtfValueChangeDirection direction = EtfDecisionDisplayAnimationHelper.DetectValueChange("1.00", "0.99");

        Assert.Equal(EtfValueChangeDirection.Down, direction);
    }

    [Fact]
    public void DetectValueChange_ReturnsNoneForSameDisplayValue()
    {
        EtfValueChangeDirection direction = EtfDecisionDisplayAnimationHelper.DetectValueChange("+1.00%", "+1.00%");

        Assert.Equal(EtfValueChangeDirection.None, direction);
    }

    [Theory]
    [InlineData("+1.23%")]
    [InlineData("1.23")]
    [InlineData("+100.00")]
    public void GetSignedNumberTone_ReturnsPositiveForPositiveValues(string value)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(FinancialValueTone.Positive, tone);
    }

    [Theory]
    [InlineData("-1.23%")]
    [InlineData("-100.00")]
    public void GetSignedNumberTone_ReturnsNegativeForNegativeValues(string value)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(FinancialValueTone.Negative, tone);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0%")]
    [InlineData("0.00")]
    [InlineData("+0.00")]
    [InlineData("-0.00")]
    public void GetSignedNumberTone_ReturnsNeutralForZeroValues(string value)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(FinancialValueTone.Neutral, tone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--")]
    public void GetSignedNumberTone_ReturnsNeutralForBlankValues(string? value)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(FinancialValueTone.Neutral, tone);
    }

    [Theory]
    [InlineData("+1,234.56", FinancialValueTone.Positive)]
    [InlineData("-1,234.56", FinancialValueTone.Negative)]
    [InlineData("+12.34%", FinancialValueTone.Positive)]
    [InlineData("-12.34%", FinancialValueTone.Negative)]
    public void GetSignedNumberTone_ParsesPercentAndCommaValues(string value, FinancialValueTone expected)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData(1.23, FinancialValueTone.Positive)]
    [InlineData(-1.23, FinancialValueTone.Negative)]
    [InlineData(0, FinancialValueTone.Neutral)]
    public void GetSignedNumberTone_ParsesNumericValues(object value, FinancialValueTone expected)
    {
        FinancialValueTone tone = EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value);

        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData("极端溢价")]
    [InlineData("全清换现金")]
    [InlineData("全清换现金(留底)")]
    [InlineData("溢价达标减仓")]
    [InlineData("溢价达标减仓(留底)")]
    [InlineData("止盈减仓")]
    [InlineData("止盈减仓(留底)")]
    [InlineData("禁止建仓")]
    [InlineData("不可执行")]
    [InlineData("行情异常")]
    [InlineData("账户异常")]
    [InlineData("TradeLog异常")]
    [InlineData("底仓保护")]
    [InlineData("战略底仓")]
    public void WarningOperationInstruction_UsesRedBackgroundAndWhiteForeground(string instruction)
    {
        Assert.True(EtfDecisionDisplayAnimationHelper.IsWarningInstruction(instruction));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningBackground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionBackground(instruction));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningForeground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionForeground(instruction));
    }

    [Fact]
    public void StrategicBaseInstruction_IsWarningInstruction()
    {
        Assert.True(EtfDecisionDisplayAnimationHelper.IsWarningInstruction("战略底仓"));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningBackground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionBackground("战略底仓"));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningForeground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionForeground("战略底仓"));
    }

    [Theory]
    [InlineData("√ 持股待涨")]
    [InlineData("等待建仓")]
    [InlineData("逢低吸筹")]
    [InlineData("狙击一档")]
    [InlineData("狙击二档")]
    [InlineData("狙击三档")]
    [InlineData("狙击四档")]
    [InlineData("狙击五档")]
    [InlineData("狙击六档")]
    [InlineData("一档建仓完成")]
    [InlineData("持股待涨")]
    [InlineData("空仓观察")]
    [InlineData("正常趋势")]
    [InlineData("场外替代")]
    public void NonWarningOperationInstruction_UsesWhiteForegroundAndTransparentBackground(string instruction)
    {
        Assert.False(EtfDecisionDisplayAnimationHelper.IsWarningInstruction(instruction));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationNormalBackground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionBackground(instruction));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationNormalForeground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionForeground(instruction));
    }

    [Fact]
    public void EtfNameForeground_UsesOrangeWhenPositionExists()
    {
        Assert.True(EtfDecisionDisplayAnimationHelper.HasEtfPosition(3900, 5809));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.EtfNameHoldingForeground, EtfDecisionDisplayAnimationHelper.GetEtfNameForeground(true));
    }

    [Fact]
    public void EtfNameForeground_UsesWhiteWhenPositionIsEmpty()
    {
        Assert.False(EtfDecisionDisplayAnimationHelper.HasEtfPosition(0, 0));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.EtfNameEmptyForeground, EtfDecisionDisplayAnimationHelper.GetEtfNameForeground(false));
    }

    [Fact]
    public void EtfNameForeground_TreatsOtcPositionAsHoldingThroughTotalQuantity()
    {
        Assert.True(EtfDecisionDisplayAnimationHelper.HasEtfPosition(86.54, 0));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.EtfNameHoldingForeground, EtfDecisionDisplayAnimationHelper.GetEtfNameForeground(true));
    }

    [Fact]
    public void EtfDecisionTableHeaders_AreCentered()
    {
        string[] requiredHeaders =
        [
            "code",
            "name",
            "action_instruction",
            "strategy_status",
            "order_summary",
            "premium",
            "price",
            "change",
            "daily_pnl",
            "total_pnl",
            "return_rate"
        ];

        foreach (string key in requiredHeaders)
        {
            int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == key).SourceIndex;

            Assert.Equal(EtfDecisionTextAlignment.Center, EtfDecisionDisplayAnimationHelper.GetEtfHeaderTextAlignment(sourceIndex));
        }
    }

    [Fact]
    public void EtfNameDataColumn_IsLeftAligned()
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == "name").SourceIndex;

        Assert.Equal(EtfDecisionTextAlignment.Left, EtfDecisionDisplayAnimationHelper.GetEtfDataTextAlignment(sourceIndex));
        Assert.Equal(EtfDecisionTextAlignment.Center, EtfDecisionDisplayAnimationHelper.GetEtfHeaderTextAlignment(sourceIndex));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("action_instruction")]
    [InlineData("strategy_status")]
    [InlineData("order_summary")]
    [InlineData("premium")]
    [InlineData("price")]
    [InlineData("change")]
    [InlineData("daily_pnl")]
    [InlineData("total_pnl")]
    [InlineData("return_rate")]
    [InlineData("etf_drawdown")]
    [InlineData("index_drawdown")]
    [InlineData("total_quantity")]
    [InlineData("average_cost")]
    public void EtfDataColumnsExceptName_AreCentered(string key)
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == key).SourceIndex;

        Assert.Equal(EtfDecisionTextAlignment.Center, EtfDecisionDisplayAnimationHelper.GetEtfDataTextAlignment(sourceIndex));
    }

    [Theory]
    [InlineData("战略底仓")]
    [InlineData("极端溢价")]
    [InlineData("全清换现金(留底)")]
    [InlineData("禁止建仓")]
    public void WarningOperationInstruction_KeepsWarningColorsAndCenterAlignment(string instruction)
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == "action_instruction").SourceIndex;

        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningBackground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionBackground(instruction));
        Assert.Equal(EtfDecisionDisplayAnimationHelper.OperationWarningForeground, EtfDecisionDisplayAnimationHelper.GetOperationInstructionForeground(instruction));
        Assert.Equal(EtfDecisionTextAlignment.Center, EtfDecisionDisplayAnimationHelper.GetEtfDataTextAlignment(sourceIndex));
    }

    [Theory]
    [InlineData("+1.23%", FinancialValueTone.Positive)]
    [InlineData("-1.23%", FinancialValueTone.Negative)]
    [InlineData("0%", FinancialValueTone.Neutral)]
    public void SignedNumberTone_IsUnchangedAndNumericColumnsAreCentered(string value, FinancialValueTone expected)
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == "change").SourceIndex;

        Assert.Equal(expected, EtfDecisionDisplayAnimationHelper.GetSignedNumberTone(value));
        Assert.Equal(EtfDecisionTextAlignment.Center, EtfDecisionDisplayAnimationHelper.GetEtfDataTextAlignment(sourceIndex));
    }

    [Theory]
    [InlineData("index_price")]
    [InlineData("index_high")]
    public void IndexPointAndHighColumns_UseDefaultWhiteForeground(string key)
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == key).SourceIndex;

        Assert.True(EtfDecisionDisplayAnimationHelper.UsesDefaultWhiteForeground(sourceIndex));
        Assert.Equal("#E5EEF8", EtfDecisionDisplayAnimationHelper.EtfDefaultDataForeground);
        Assert.False(EtfDecisionColumnSettings.UsesSignedNumberColorRule(sourceIndex));
    }

    [Theory]
    [InlineData("index_change")]
    [InlineData("index_drawdown")]
    public void IndexSignedColumns_KeepSignedNumberColorRule(string key)
    {
        int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == key).SourceIndex;

        Assert.False(EtfDecisionDisplayAnimationHelper.UsesDefaultWhiteForeground(sourceIndex));
        Assert.True(EtfDecisionColumnSettings.UsesSignedNumberColorRule(sourceIndex));
        Assert.Equal(FinancialValueTone.Positive, EtfDecisionDisplayAnimationHelper.GetSignedNumberTone("+1.23%"));
        Assert.Equal(FinancialValueTone.Negative, EtfDecisionDisplayAnimationHelper.GetSignedNumberTone("-1.23%"));
    }

    [Fact]
    public void BuildLongTextToolTip_ReturnsTextForLongCellContent()
    {
        string? tooltip = EtfDecisionDisplayAnimationHelper.BuildLongTextToolTip("full close to cash with base kept");

        Assert.False(string.IsNullOrWhiteSpace(tooltip));
    }

    [Fact]
    public void StripPinnedMarker_DoesNotChangeOriginalStrategyCode()
    {
        string code = EtfDecisionDisplayAnimationHelper.StripPinnedMarker("★ 159941");

        Assert.Equal("159941", code);
    }

    [Fact]
    public void RetainSelectedCode_KeepsSelectionAfterRefreshWhenCodeStillExists()
    {
        string? selected = EtfDecisionDisplayAnimationHelper.RetainSelectedCode(["159501", "★ 159941"], "159941");

        Assert.Equal("159941", selected);
    }
}
