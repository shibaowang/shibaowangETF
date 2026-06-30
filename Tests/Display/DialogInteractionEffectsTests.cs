namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class DialogInteractionEffectsTests
{
    [Fact]
    public void LeftNavigationDialogs_UseOwnerAndDoNotShowInTaskbar()
    {
        string mainWindowCode = ReadRepositoryFile("MainWindow.xaml.cs");
        string manualXaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string riskXaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));

        Assert.Contains("Owner = this", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", riskXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LeftNavigationDialogs_UseDarkInitialBackgrounds()
    {
        string manualXaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string riskXaml = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml"));

        Assert.Contains("Background=\"#050B14\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Margin=\"18\" Background=\"#050B14\">", manualXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#050B14\"", riskXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Margin=\"18\" Background=\"#050B14\">", riskXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"White\"", manualXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Background=\"White\"", riskXaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeftNavigationDialogs_ApplySmoothOpenFade()
    {
        string helper = ReadRepositoryFile(Path.Combine("Views", "WindowInteractionEffects.cs"));
        string manualCode = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));
        string riskCode = ReadRepositoryFile(Path.Combine("Views", "RiskCenterWindow.xaml.cs"));

        Assert.Contains("TimeSpan.FromMilliseconds(160)", helper, StringComparison.Ordinal);
        Assert.Contains("CubicEase", helper, StringComparison.Ordinal);
        Assert.Contains("BeginAnimation(UIElement.OpacityProperty", helper, StringComparison.Ordinal);
        Assert.Contains("WindowInteractionEffects.ApplySmoothOpen(this)", manualCode, StringComparison.Ordinal);
        Assert.Contains("WindowInteractionEffects.ApplySmoothOpen(this)", riskCode, StringComparison.Ordinal);
        Assert.Contains("WindowInteractionEffects.ApplySmoothOpen(dialog)", riskCode, StringComparison.Ordinal);
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
