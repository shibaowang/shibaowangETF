namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class BrandingResourceTests
{
    [Fact]
    public void BrandingResources_ExistAndAreReferenced()
    {
        string root = FindWorkspaceRoot();
        string pngPath = Path.Combine(root, "Assets", "Branding", "CrossETF_V8_AppIcon.png");
        string icoPath = Path.Combine(root, "Resources", "AppIcon.ico");
        string project = File.ReadAllText(Path.Combine(root, "CrossETF.Terminal.UiShell.Reference.csproj"));
        string xaml = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));

        Assert.True(File.Exists(pngPath));
        Assert.True(File.Exists(icoPath));
        Assert.True(new FileInfo(icoPath).Length > 0);
        Assert.Contains("<ApplicationIcon>Resources\\AppIcon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Resources\\AppIcon.ico\" />", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"Resources/AppIcon.ico\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"Assets/Branding/CrossETF_V8_AppIcon.png\"", xaml, StringComparison.Ordinal);
        AssertIconContainsRequiredSizes(icoPath);
    }

    private static void AssertIconContainsRequiredSizes(string icoPath)
    {
        byte[] bytes = File.ReadAllBytes(icoPath);
        Assert.True(bytes.Length > 6);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        ushort count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(count >= 7);

        var sizes = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            int offset = 6 + index * 16;
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            int height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            if (width == height)
            {
                sizes.Add(width);
            }
        }

        foreach (int requiredSize in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Contains(requiredSize, sizes);
        }
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
