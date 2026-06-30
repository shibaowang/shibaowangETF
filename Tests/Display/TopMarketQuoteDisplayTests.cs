namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class TopMarketQuoteDisplayTests
{
    [Fact]
    public void TopMarketQuoteCards_DoNotRenderStatusWordsWhenCacheExists()
    {
        string method = ReadSetTopMarketQuoteMethod();

        Assert.DoesNotContain("过期", method, StringComparison.Ordinal);
        Assert.DoesNotContain("收盘", method, StringComparison.Ordinal);
        Assert.DoesNotContain("午休", method, StringComparison.Ordinal);
        Assert.DoesNotContain("交易中", method, StringComparison.Ordinal);
        Assert.DoesNotContain("缓存", method, StringComparison.Ordinal);
        Assert.DoesNotContain("未连接", method, StringComparison.Ordinal);
        Assert.Contains("changeText.Text = \"  \" + change;", method, StringComparison.Ordinal);
    }

    [Fact]
    public void TopMarketQuoteCards_ShowDashesWhenNoQuoteCacheExists()
    {
        string method = ReadSetTopMarketQuoteMethod();

        Assert.Contains("priceText.Text = \"--\";", method, StringComparison.Ordinal);
        Assert.Contains("changeText.Text = \"  --\";", method, StringComparison.Ordinal);
    }

    private static string ReadSetTopMarketQuoteMethod()
    {
        string source = File.ReadAllText(FindWorkspaceFile("MainWindow.xaml.cs"));
        const string startMarker = "private void SetTopMarketQuote";
        const string endMarker = "private void UpdateAccountCards";
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "SetTopMarketQuote method was not found.");
        return source[start..end];
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Workspace file not found: " + Path.Combine(parts));
    }
}
