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
        Assert.DoesNotContain("WindowInteractionEffects.ApplySmoothOpen(this)", manualCode, StringComparison.Ordinal);
        Assert.Contains("WindowInteractionEffects.ApplySmoothOpen(this)", riskCode, StringComparison.Ordinal);
        Assert.Contains("WindowInteractionEffects.ApplySmoothOpen(dialog)", riskCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualDataEntryWindow_UsesNativeCaptionButtonsWithDwmDarkFrame()
    {
        string manualXaml = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml"));
        string manualCode = ReadRepositoryFile(Path.Combine("Views", "ManualDataEntryWindow.xaml.cs"));

        Assert.DoesNotContain("WindowStyle=\"None\"", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<shell:WindowChrome", manualXaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"260\"", manualXaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"220\"", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarButtonStyle", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"TitleMinimizeButton\"", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"TitleMaximizeButton\"", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"TitleCloseButton\"", manualXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleMinimizeButton_Click", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleMaximizeButton_Click", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureTitleBarButtons", manualCode, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute(hwnd, 34", manualCode, StringComparison.Ordinal);
        Assert.Contains("ApplyDarkHwndBackground", manualCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity = 0", manualCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DarkStartupShield", manualXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content = null", manualCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindowNavigation_UsesOpaqueDarkBackgroundsAndAllowsRepeatClicks()
    {
        string xaml = ReadRepositoryFile("MainWindow.xaml");
        string code = ReadRepositoryFile("MainWindow.xaml.cs");

        Assert.Contains("<StackPanel x:Name=\"NavStack\" Background=\"#06101B\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("new Grid { Height = 83, Tag = names[i], Background = BrushFrom(\"#06101B\") }", code, StringComparison.Ordinal);
        Assert.Contains("Background = i == 0 ? BrushFrom(\"#8A1D2A\") : BrushFrom(\"#06101B\")", code, StringComparison.Ordinal);
        Assert.Contains("border.SetValue(System.Windows.Controls.Border.BackgroundProperty, BrushFrom(\"#06101B\"));", code, StringComparison.Ordinal);
        Assert.Contains("SelectNavigation(navigationName);", code, StringComparison.Ordinal);
        Assert.Contains("OpenRiskCenter();", code, StringComparison.Ordinal);
        Assert.Contains("OpenManualEntry(scope);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (string.Equals(_selectedNavigationName, navigationName, StringComparison.Ordinal))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = null", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Thread.Sleep", code, StringComparison.OrdinalIgnoreCase);
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
