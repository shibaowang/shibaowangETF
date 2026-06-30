namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class MainWindowDisplayTextTests
{
    [Fact]
    public void MainWindow_UsesUserFacingAccountAndPoolLabels()
    {
        string xaml = ReadRepositoryFile("MainWindow.xaml");

        Assert.Contains("持仓市值", xaml);
        Assert.Contains("可用档位", xaml);
        Assert.DoesNotContain("已知市值", xaml);
        Assert.DoesNotContain("最近刷新", xaml);
    }

    [Fact]
    public void EtfTable_UsesEtfHighHeader()
    {
        string code = ReadRepositoryFile(Path.Combine("Core", "Services", "EtfDecisionColumnSettings.cs"));

        Assert.Contains("ETF高点", code);
        Assert.DoesNotContain("ETF隐点", code);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", relativePath);
    }
}
