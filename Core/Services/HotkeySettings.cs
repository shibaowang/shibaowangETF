using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

[Flags]
public enum HotkeyModifierKeys
{
    None = 0,
    Alt = 0x0001,
    Ctrl = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

public sealed record HotkeySettings(bool Enabled, HotkeyModifierKeys Modifiers, string Key)
{
    public const string EnabledSettingKey = "ui_hotkey_enabled";
    public const string ModifiersSettingKey = "ui_hotkey_modifiers";
    public const string KeySettingKey = "ui_hotkey_key";

    public static HotkeySettings Default { get; } = new(true, HotkeyModifierKeys.Alt, "D1");

    public static IReadOnlyList<string> SupportedKeys { get; } = BuildSupportedKeys();

    public string DisplayText => Enabled ? FormatDisplay(Modifiers, Key) : "未设置";

    public static string FormatDisplay(HotkeyModifierKeys modifiers, string key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifierKeys.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Win))
        {
            parts.Add("Win");
        }

        parts.Add(FormatDisplayKey(key));
        return string.Join("+", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static string FormatModifiers(HotkeyModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifierKeys.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifierKeys.Win))
        {
            parts.Add("Win");
        }

        return string.Join(",", parts);
    }

    public static string FormatKey(string? key)
    {
        string normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
        {
            return normalized;
        }

        if (normalized.Length == 1 && normalized[0] is >= '0' and <= '9')
        {
            return "D" + normalized;
        }

        if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] is >= '0' and <= '9')
        {
            return normalized;
        }

        if (normalized.Length is 2 or 3
            && normalized[0] == 'F'
            && int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int functionNumber)
            && functionNumber is >= 1 and <= 12)
        {
            return "F" + functionNumber.ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    public static string FormatDisplayKey(string? key)
    {
        string normalized = FormatKey(key);
        if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] is >= '0' and <= '9')
        {
            return normalized[1].ToString();
        }

        return normalized;
    }

    public static bool TryParseModifiers(string? value, out HotkeyModifierKeys modifiers)
    {
        modifiers = HotkeyModifierKeys.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = value.Replace("+", ",", StringComparison.Ordinal);
        foreach (string rawPart in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawPart.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || rawPart.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifierKeys.Ctrl;
            }
            else if (rawPart.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifierKeys.Alt;
            }
            else if (rawPart.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifierKeys.Shift;
            }
            else if (rawPart.Equals("Win", StringComparison.OrdinalIgnoreCase)
                     || rawPart.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifierKeys.Win;
            }
            else
            {
                modifiers = HotkeyModifierKeys.None;
                return false;
            }
        }

        return true;
    }

    public static bool TryParseKey(string? value, out string key)
    {
        key = string.Empty;
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
        {
            key = normalized;
            return true;
        }

        if (normalized.Length == 1 && normalized[0] is >= '0' and <= '9')
        {
            key = "D" + normalized;
            return true;
        }

        if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] is >= '0' and <= '9')
        {
            key = normalized;
            return true;
        }

        if (normalized.Length is 2 or 3
            && normalized[0] == 'F'
            && int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int functionNumber)
            && functionNumber is >= 1 and <= 12)
        {
            key = "F" + functionNumber.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    public static HotkeySettings FromStoredValues(string? enabled, string? modifiers, string? key)
    {
        bool isEnabled = string.IsNullOrWhiteSpace(enabled)
            ? Default.Enabled
            : enabled.Equals("true", StringComparison.OrdinalIgnoreCase)
              || enabled.Equals("1", StringComparison.OrdinalIgnoreCase);
        HotkeyModifierKeys parsedModifiers = TryParseModifiers(modifiers, out HotkeyModifierKeys storedModifiers)
            ? storedModifiers
            : Default.Modifiers;
        if (parsedModifiers == HotkeyModifierKeys.None && string.IsNullOrWhiteSpace(modifiers))
        {
            parsedModifiers = Default.Modifiers;
        }

        string parsedKey = TryParseKey(key, out string storedKey) ? storedKey : Default.Key;
        return new HotkeySettings(isEnabled, parsedModifiers, parsedKey);
    }

    public bool TryValidate(out string? error)
    {
        error = null;
        if (!Enabled)
        {
            return true;
        }

        if (Modifiers == HotkeyModifierKeys.None)
        {
            error = "至少需要一个修饰键。";
            return false;
        }

        if (!IsSupportedKey(Key))
        {
            error = "主键必须为 A-Z、0-9 或 F1-F12。";
            return false;
        }

        return true;
    }

    private static bool IsSupportedKey(string key)
        => !string.IsNullOrWhiteSpace(FormatKey(key));

    private static string[] BuildSupportedKeys()
    {
        var keys = new List<string>();
        for (char ch = 'A'; ch <= 'Z'; ch++)
        {
            keys.Add(ch.ToString());
        }

        for (char ch = '0'; ch <= '9'; ch++)
        {
            keys.Add("D" + ch);
        }

        for (int i = 1; i <= 12; i++)
        {
            keys.Add("F" + i.ToString(CultureInfo.InvariantCulture));
        }

        return keys.ToArray();
    }
}

public sealed record HotkeySettingsSaveResult(bool Success, string StatusText, string Message)
{
    public static HotkeySettingsSaveResult Saved(string statusText, string message)
        => new(true, statusText, message);

    public static HotkeySettingsSaveResult Failed(string statusText, string message)
        => new(false, statusText, message);
}
