using System.Xml.Linq;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class MarketDiagnosticsViewTests
{
    [Fact]
    public void RiskCenterHostsMarketDiagnosticsWithoutNewLeftNavigationModuleOrWindow()
    {
        string riskCode = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));
        string mainCode = ReadRepositoryFile("MainWindow.xaml.cs");
        string[] viewFiles = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "Views"), "*.xaml");

        Assert.Contains("Header = \"运行诊断\"", riskCode, StringComparison.Ordinal);
        Assert.Contains("Header = \"风险概览\"", riskCode, StringComparison.Ordinal);
        Assert.Contains("new MarketDiagnosticsView", riskCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\"运行诊断\"", mainCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDiagnosticsWindow", riskCode, StringComparison.Ordinal);
        Assert.DoesNotContain(viewFiles, path => Path.GetFileName(path).Equals("MarketDiagnosticsWindow.xaml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MarketDiagnosticsView_IsReadOnlyAndHasFiveTabs()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml.cs"));

        Assert.Contains("Header=\"诊断总览\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"行情与缓存\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"今日盈亏审计\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"运行日志\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"程序环境\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly", xaml, StringComparison.Ordinal);
        Assert.Contains("重新读取本地状态", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshLocalState()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketDiagnosticsView_UsesLocalReadableTabsChineseColumnsAndBoundedScrolling()
    {
        string diagnosticsXaml = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml"));
        string riskXaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        string riskCode = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.Contains("x:Key=\"DiagnosticsTabItemStyle\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsSelected\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseOver\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"RiskCenterTabItemStyle\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle = (Style)FindResource(\"RiskCenterTabItemStyle\")", riskCode, StringComparison.Ordinal);

        Assert.Contains("Header=\"数据源\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"行情时间\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"是否计入\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"计入金额\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"异常摘要\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"程序版本\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"数据库路径\"", diagnosticsXaml, StringComparison.Ordinal);

        Assert.Contains("StringFormat={}{0:0.####}", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat={}{0:N2}", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\"", diagnosticsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketDiagnosticsView_PnlAuditKeepsDecisionColumnsBeforeScrollableDetails()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml"));
        int auditStart = xaml.IndexOf("<TabItem Header=\"今日盈亏审计\">", StringComparison.Ordinal);
        int auditEnd = xaml.IndexOf("<TabItem Header=\"运行日志\">", auditStart, StringComparison.Ordinal);
        Assert.True(auditStart >= 0 && auditEnd > auditStart);
        string auditXaml = xaml[auditStart..auditEnd];

        string[] headers =
        {
            "策略代码", "实际代码", "标的名称", "是否计入", "计入金额", "原因",
            "候选当日盈亏", "数量", "行情时间", "接收时间", "计算时间", "数据源", "市场类型"
        };
        int previousIndex = -1;
        foreach (string header in headers)
        {
            int index = auditXaml.IndexOf($"Header=\"{header}\"", StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Column '{header}' is not in the required order.");
            previousIndex = index;
        }
    }

    [Fact]
    public void RiskCenterWindow_HasOneCommonFooterCloseButtonOutsideTabContent()
    {
        string xaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        string code = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.DoesNotContain("Content=\"关闭\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(code, "Content = \"关闭\""));
        Assert.Contains("Border commonFooter = CreateCommonFooter();", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(commonFooter, 2);", code, StringComparison.Ordinal);
        Assert.Contains("closeButton.Click += CloseButton_Click;", code, StringComparison.Ordinal);
        Assert.Contains("private void CloseButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("=> Close();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketDiagnosticsView_StretchesAllTabsWhileOnlyDataGridsScrollHorizontally()
    {
        string diagnosticsXaml = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml"));
        string riskXaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        int userControlEnd = diagnosticsXaml.IndexOf('>');
        Assert.True(userControlEnd > 0);
        string userControlTag = diagnosticsXaml[..userControlEnd];

        Assert.Contains("HorizontalAlignment=\"Stretch\"", userControlTag, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", userControlTag, StringComparison.Ordinal);
        Assert.DoesNotContain(" Width=", userControlTag, StringComparison.Ordinal);
        Assert.DoesNotContain(" Height=", userControlTag, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"HorizontalContentAlignment\" Value=\"Stretch\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"VerticalContentAlignment\" Value=\"Stretch\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"HorizontalContentAlignment\" Value=\"Stretch\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"VerticalContentAlignment\" Value=\"Stretch\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"3\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\"", diagnosticsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RiskCenterActionButtons_UseScopedNormalAndDangerStyles()
    {
        string riskXaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));
        string diagnosticsXaml = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml"));
        string appXaml = ReadRepositoryFile("App.xaml");
        string riskCode = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));
        string diagnosticsCode = ReadRepositoryFile(Path.Combine("Views", "MarketDiagnosticsView.xaml.cs"));

        Assert.Contains("Content=\"刷新日志\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"清空日志\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"重新读取本地状态\"", diagnosticsXaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(riskXaml, "Style=\"{StaticResource RiskCenterActionButtonStyle}\""));
        Assert.Equal(1, CountOccurrences(diagnosticsXaml, "Style=\"{StaticResource RiskCenterActionButtonStyle}\""));
        Assert.Equal(1, CountOccurrences(riskXaml, "Style=\"{StaticResource RiskCenterDangerButtonStyle}\""));
        Assert.Contains("Styles/RiskCenterActionButtonStyles.xaml", riskXaml, StringComparison.Ordinal);
        Assert.Contains("Styles/RiskCenterActionButtonStyles.xaml", diagnosticsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RiskCenterActionButtonStyles.xaml", appXaml, StringComparison.Ordinal);

        Assert.DoesNotContain("Style = (Style)FindResource(\"RiskCenterActionButtonStyle\")", riskCode, StringComparison.Ordinal);
        Assert.Contains("=> RefreshAlertLogs(true);", riskCode, StringComparison.Ordinal);
        Assert.Contains("ShowClearAlertLogConfirmation()", riskCode, StringComparison.Ordinal);
        Assert.Contains("=> RefreshLocalState();", diagnosticsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled =", riskCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled =", diagnosticsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void RiskCenterActionButtonStyles_DefineReadableVisualStates()
    {
        string stylesPath = Path.Combine(FindRepositoryRoot(), "Views", "Styles", "RiskCenterActionButtonStyles.xaml");
        string stylesXaml = File.ReadAllText(stylesPath);
        XDocument document = XDocument.Parse(stylesXaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement actionStyle = document
            .Descendants(presentation + "Style")
            .Single(element => (string?)element.Attribute(x + "Key") == "RiskCenterActionButtonStyle");
        XElement dangerStyle = document
            .Descendants(presentation + "Style")
            .Single(element => (string?)element.Attribute(x + "Key") == "RiskCenterDangerButtonStyle");

        AssertStyleHasNormalColors(actionStyle);
        AssertStyleHasCompleteTrigger(actionStyle, "IsMouseOver", "True");
        AssertStyleHasCompleteTrigger(actionStyle, "IsPressed", "True");
        AssertStyleHasCompleteTrigger(actionStyle, "IsEnabled", "False");
        AssertStyleHasCompleteTrigger(actionStyle, "IsFocused", "True");
        AssertStyleHasCompleteTrigger(actionStyle, "IsKeyboardFocusWithin", "True");

        Assert.Equal("{StaticResource RiskCenterActionButtonStyle}", (string?)dangerStyle.Attribute("BasedOn"));
        AssertStyleHasNormalColors(dangerStyle);
        AssertStyleHasCompleteTrigger(dangerStyle, "IsMouseOver", "True");
        AssertStyleHasCompleteTrigger(dangerStyle, "IsPressed", "True");
        AssertStyleHasCompleteTrigger(dangerStyle, "IsEnabled", "False");
        AssertStyleHasCompleteTrigger(dangerStyle, "IsFocused", "True");
        AssertStyleHasCompleteTrigger(dangerStyle, "IsKeyboardFocusWithin", "True");

        Assert.Contains("TextElement.Foreground=\"{TemplateBinding Foreground}\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"", stylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity\" Value=\"0\"", stylesXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketDiagnosticsDoesNotModifyManualDataEntryOrWhiteFlashPaths()
    {
        string manualXaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string manualCode = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.DoesNotContain("<shell:WindowChrome", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkStartupShield", manualXaml, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static int CountOccurrences(string value, string text)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = value.IndexOf(text, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += text.Length;
        }

        return count;
    }

    private static void AssertStyleHasNormalColors(XElement style)
    {
        string[] properties = style
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => (string?)element.Attribute("Property") ?? string.Empty)
            .ToArray();
        Assert.Contains("Background", properties);
        Assert.Contains("Foreground", properties);
        Assert.Contains("BorderBrush", properties);
    }

    private static void AssertStyleHasCompleteTrigger(
        XElement style,
        string property,
        string value)
    {
        XElement trigger = style
            .Descendants()
            .Single(element => element.Name.LocalName == "Trigger"
                               && (string?)element.Attribute("Property") == property
                               && (string?)element.Attribute("Value") == value);
        string[] properties = trigger
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => (string?)element.Attribute("Property") ?? string.Empty)
            .ToArray();
        Assert.Contains("Background", properties);
        Assert.Contains("Foreground", properties);
        Assert.Contains("BorderBrush", properties);
        Assert.Contains("BorderThickness", properties);
        Assert.Contains("Opacity", properties);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "CrossETF.Terminal.UiShell.Reference.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }
}
