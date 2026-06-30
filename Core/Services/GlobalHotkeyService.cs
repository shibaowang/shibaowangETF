using System.Globalization;
using System.Runtime.InteropServices;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    public const int HotkeyId = 0x4554;
    private const int ValidationHotkeyId = 0x4555;
    public const int WmHotkey = 0x0312;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private IntPtr _registeredHwnd;
    private HotkeySettings? _registeredSettings;

    public HotkeySettings? RegisteredSettings => _registeredSettings;

    public GlobalHotkeyRegistrationResult Apply(IntPtr hwnd, HotkeySettings settings)
    {
        if (!settings.Enabled)
        {
            Unregister();
            return GlobalHotkeyRegistrationResult.Disabled();
        }

        if (!settings.TryValidate(out string? error))
        {
            return GlobalHotkeyRegistrationResult.Invalid(error ?? "快捷键组合不合法。");
        }

        if (hwnd == IntPtr.Zero)
        {
            return GlobalHotkeyRegistrationResult.Failed("注册失败", "主窗口句柄不可用。");
        }

        if (_registeredSettings == settings && _registeredHwnd == hwnd)
        {
            return GlobalHotkeyRegistrationResult.Active();
        }

        if (!TryRegister(hwnd, ValidationHotkeyId, settings, out int validationError))
        {
            return validationError == ErrorHotkeyAlreadyRegistered
                ? GlobalHotkeyRegistrationResult.Failed("快捷键冲突", "快捷键冲突，未保存")
                : GlobalHotkeyRegistrationResult.Failed("注册失败", $"快捷键注册失败，错误码：{validationError}。");
        }

        _ = UnregisterHotKey(hwnd, ValidationHotkeyId);

        IntPtr previousHwnd = _registeredHwnd;
        HotkeySettings? previousSettings = _registeredSettings;
        Unregister();

        if (!TryRegister(hwnd, HotkeyId, settings, out int registerError))
        {
            if (previousHwnd != IntPtr.Zero && previousSettings is not null)
            {
                _ = TryRegister(previousHwnd, HotkeyId, previousSettings, out _);
                _registeredHwnd = previousHwnd;
                _registeredSettings = previousSettings;
            }

            return registerError == ErrorHotkeyAlreadyRegistered
                ? GlobalHotkeyRegistrationResult.Failed("快捷键冲突", "快捷键冲突，未保存")
                : GlobalHotkeyRegistrationResult.Failed("注册失败", $"快捷键注册失败，错误码：{registerError}。");
        }

        _registeredHwnd = hwnd;
        _registeredSettings = settings;
        return GlobalHotkeyRegistrationResult.Active();
    }

    public void Unregister()
    {
        if (_registeredHwnd != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_registeredHwnd, HotkeyId);
        }

        _registeredHwnd = IntPtr.Zero;
        _registeredSettings = null;
    }

    public void Dispose()
        => Unregister();

    private static bool TryRegister(IntPtr hwnd, int id, HotkeySettings settings, out int error)
    {
        error = 0;
        int virtualKey = GetVirtualKey(settings.Key);
        if (virtualKey <= 0)
        {
            error = -1;
            return false;
        }

        if (RegisterHotKey(hwnd, id, (uint)settings.Modifiers, (uint)virtualKey))
        {
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static int GetVirtualKey(string key)
    {
        string normalized = HotkeySettings.FormatKey(key);
        if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
        {
            return normalized[0];
        }

        if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] is >= '0' and <= '9')
        {
            return 0x30 + int.Parse(normalized[1].ToString(), CultureInfo.InvariantCulture);
        }

        if (normalized.Length is 2 or 3
            && normalized[0] == 'F'
            && int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int functionNumber)
            && functionNumber is >= 1 and <= 12)
        {
            return 0x70 + functionNumber - 1;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public sealed record GlobalHotkeyRegistrationResult(bool Success, string StatusText, string? Message)
{
    public static GlobalHotkeyRegistrationResult Active()
        => new(true, "已生效", null);

    public static GlobalHotkeyRegistrationResult Disabled()
        => new(true, "未启用", null);

    public static GlobalHotkeyRegistrationResult Invalid(string message)
        => new(false, "未保存", message);

    public static GlobalHotkeyRegistrationResult Failed(string statusText, string message)
        => new(false, statusText, message);
}
