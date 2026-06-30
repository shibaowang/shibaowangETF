namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class BrandingResourceTests
{
    [Fact]
    public void BrandingResources_ExistAndAreReferenced()
    {
        string root = FindWorkspaceRoot();
        string pngPath = Path.Combine(root, "Assets", "Branding", "CrossETF_V8_AppIcon.png");
        string icoPath = Path.Combine(root, "Assets", "Branding", "CrossETF_V8_AppIcon.ico");
        string project = File.ReadAllText(Path.Combine(root, "CrossETF.Terminal.UiShell.Reference.csproj"));
        string xaml = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));

        Assert.True(File.Exists(pngPath));
        Assert.True(File.Exists(icoPath));
        Assert.True(new FileInfo(icoPath).Length > 0);
        Assert.Contains("<ApplicationIcon>Assets\\Branding\\CrossETF_V8_AppIcon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"Assets/Branding/CrossETF_V8_AppIcon.ico\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"Assets/Branding/CrossETF_V8_AppIcon.png\"", xaml, StringComparison.Ordinal);
    }

    private static string FindWorkspaceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "CrossETF.Terminal.UiShell.Reference.csproj");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }
}
