using CrossETF.Terminal.UiShell.Reference;

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

    [Fact]
    public void MainWindow_VersionDisplayUsesAssemblyVersion()
    {
        string xaml = ReadRepositoryFile("MainWindow.xaml");
        string code = ReadRepositoryFile("MainWindow.xaml.cs");
        string project = ReadRepositoryFile("CrossETF.Terminal.UiShell.Reference.csproj");

        Assert.DoesNotContain("V8.0.0", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VersionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VersionText.Text = BuildVersionDisplayText();", code, StringComparison.Ordinal);
        Assert.Contains("<Version>8.7.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>8.7.0</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.Equal("V8.7.0", MainWindow.ResolveDisplayVersion());
        Assert.Equal("版本： V8.7.0", MainWindow.BuildVersionDisplayText());
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
