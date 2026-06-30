using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class GlobalHotkeyServiceTests
{
    [Fact]
    public void DefaultHotkeySettings_UseAltD1AndDisplayAlt1()
    {
        HotkeySettings settings = HotkeySettings.Default;

        Assert.True(settings.Enabled);
        Assert.Equal(HotkeyModifierKeys.Alt, settings.Modifiers);
        Assert.Equal("D1", settings.Key);
        Assert.Equal("Alt+1", settings.DisplayText);
    }

    [Theory]
    [InlineData("Alt", "D1", "Alt+1")]
    [InlineData("Ctrl,Alt", "H", "Ctrl+Alt+H")]
    [InlineData("Ctrl,Shift", "F9", "Ctrl+Shift+F9")]
    [InlineData("Alt", "F12", "Alt+F12")]
    [InlineData("Ctrl", "9", "Ctrl+9")]
    public void HotkeySettings_FormatDisplayText(string modifiersText, string keyText, string expected)
    {
        Assert.True(HotkeySettings.TryParseModifiers(modifiersText, out HotkeyModifierKeys modifiers));
        Assert.True(HotkeySettings.TryParseKey(keyText, out string key));

        Assert.Equal(expected, HotkeySettings.FormatDisplay(modifiers, key));
    }

    [Theory]
    [InlineData("1", "D1", "1")]
    [InlineData("D1", "D1", "1")]
    [InlineData("9", "D9", "9")]
    [InlineData("H", "H", "H")]
    [InlineData("F9", "F9", "F9")]
    public void HotkeySettings_NormalizesStorageAndDisplayKeys(string input, string expectedStorage, string expectedDisplay)
    {
        Assert.True(HotkeySettings.TryParseKey(input, out string key));

        Assert.Equal(expectedStorage, key);
        Assert.Equal(expectedStorage, HotkeySettings.FormatKey(input));
        Assert.Equal(expectedDisplay, HotkeySettings.FormatDisplayKey(input));
    }

    [Fact]
    public void HotkeySettings_RejectsEnabledSingleLetterWithoutModifier()
    {
        var settings = new HotkeySettings(true, HotkeyModifierKeys.None, "H");

        Assert.False(settings.TryValidate(out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void HotkeySettings_AllowsDisabledState()
    {
        var settings = new HotkeySettings(false, HotkeyModifierKeys.None, "H");

        Assert.True(settings.TryValidate(out string? error), error);
    }

    [Fact]
    public void HotkeySettings_FromMissingStoredValuesUsesDefault()
    {
        HotkeySettings settings = HotkeySettings.FromStoredValues(null, null, null);

        Assert.True(settings.Enabled);
        Assert.Equal(HotkeyModifierKeys.Alt, settings.Modifiers);
        Assert.Equal("D1", settings.Key);
        Assert.Equal("Alt+1", settings.DisplayText);
    }

    [Fact]
    public void HotkeySettings_RejectsInvalidKeys()
    {
        Assert.False(HotkeySettings.TryParseKey("Esc", out _));
        Assert.False(HotkeySettings.TryParseKey("Enter", out _));
        Assert.False(HotkeySettings.TryParseKey("Tab", out _));
        Assert.False(HotkeySettings.TryParseKey("Left", out _));
    }

    [Fact]
    public void GlobalHotkeyService_ExposesRegisterHotKeyApiBoundary()
    {
        string code = ReadRepositoryFile(Path.Combine("Core", "Services", "GlobalHotkeyService.cs"));

        Assert.Contains("RegisterHotKey", code);
        Assert.Contains("UnregisterHotKey", code);
        Assert.DoesNotContain("SetWindowsHookEx", code);
    }

    [Fact]
    public void GlobalHotkeyService_KeepsPreviousRegistrationOnApplyFailurePath()
    {
        string code = ReadRepositoryFile(Path.Combine("Core", "Services", "GlobalHotkeyService.cs"));

        Assert.Contains("previousSettings", code);
        Assert.Contains("previousHwnd", code);
        Assert.Contains("TryRegister(previousHwnd, HotkeyId, previousSettings", code);
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
