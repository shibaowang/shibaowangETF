using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32.SafeHandles;

namespace CrossETF.Terminal.UiShell.Reference.Views;

internal sealed class WindowWhiteFlashGuard : IDisposable
{
    private const int WmEraseBackground = 0x0014;
    private static readonly object RegistrySync = new();
    private static readonly ConditionalWeakTable<Window, WindowWhiteFlashGuard> Registry = new();

    private readonly Window _window;
    private readonly Color _backgroundColor;
    private readonly HwndSourceHook _windowHook;
    private HwndSource? _source;
    private SafeGdiBrushHandle? _backgroundBrush;
    private bool _hookAttached;
    private bool _disposed;

    private WindowWhiteFlashGuard(Window window, Color backgroundColor)
    {
        _window = window;
        _backgroundColor = backgroundColor;
        _windowHook = WindowProc;
        _window.SourceInitialized += Window_SourceInitialized;
        _window.Closed += Window_Closed;
    }

    internal bool IsHookAttached => _hookAttached;

    internal bool HasNativeBrush => _backgroundBrush is { IsInvalid: false, IsClosed: false };

    internal bool IsDisposed => _disposed;

    internal int HookAttachCount { get; private set; }

    internal static WindowWhiteFlashGuard Attach(Window window, Color backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (RegistrySync)
        {
            if (Registry.TryGetValue(window, out WindowWhiteFlashGuard? existing))
            {
                if (existing._backgroundColor != backgroundColor)
                {
                    throw new InvalidOperationException("窗口已使用不同颜色的首帧保护。");
                }

                return existing;
            }

            var guard = new WindowWhiteFlashGuard(window, backgroundColor);
            Registry.Add(window, guard);
            try
            {
                if (TryGetHwndSource(window) is HwndSource source)
                {
                    guard.AttachToSource(source);
                }
            }
            catch
            {
                guard.Dispose();
                throw;
            }

            return guard;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _window.SourceInitialized -= Window_SourceInitialized;
        _window.Closed -= Window_Closed;
        try
        {
            DetachFromSource(sourceIsDisposing: false);
        }
        finally
        {
            _disposed = true;
            lock (RegistrySync)
            {
                Registry.Remove(_window);
            }
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (TryGetHwndSource(_window) is not HwndSource source)
        {
            Dispose();
            throw new InvalidOperationException("窗口 HWND 已初始化，但无法获取 HwndSource。");
        }

        try
        {
            AttachToSource(source);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static HwndSource? TryGetHwndSource(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource visualSource)
        {
            return visualSource;
        }

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        return windowHandle == IntPtr.Zero ? null : HwndSource.FromHwnd(windowHandle);
    }

    private void Window_Closed(object? sender, EventArgs e)
        => Dispose();

    private void AttachToSource(HwndSource source)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowWhiteFlashGuard));
        }

        if (_hookAttached && ReferenceEquals(_source, source))
        {
            return;
        }

        DetachFromSource(sourceIsDisposing: false);
        source.CompositionTarget.BackgroundColor = _backgroundColor;

        IntPtr brush = CreateSolidBrush(ToColorRef(_backgroundColor));
        if (brush == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建窗口首帧深色背景画刷。");
        }

        _backgroundBrush = new SafeGdiBrushHandle(brush);
        _source = source;
        source.Disposed += Source_Disposed;
        try
        {
            source.AddHook(_windowHook);
            _hookAttached = true;
            HookAttachCount++;
        }
        catch
        {
            source.Disposed -= Source_Disposed;
            _source = null;
            _backgroundBrush.Dispose();
            _backgroundBrush = null;
            throw;
        }
    }

    private void Source_Disposed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _source))
        {
            return;
        }

        DetachFromSource(sourceIsDisposing: true);
    }

    private void DetachFromSource(bool sourceIsDisposing)
    {
        HwndSource? source = _source;
        SafeGdiBrushHandle? brush = _backgroundBrush;
        try
        {
            if (source is not null)
            {
                source.Disposed -= Source_Disposed;
                if (_hookAttached && !sourceIsDisposing)
                {
                    source.RemoveHook(_windowHook);
                }
            }
        }
        finally
        {
            _hookAttached = false;
            _source = null;
            _backgroundBrush = null;
            brush?.Dispose();
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmEraseBackground
            || wParam == IntPtr.Zero
            || _backgroundBrush is not { IsInvalid: false, IsClosed: false } brush
            || !GetClientRect(hwnd, out NativeRect clientRect))
        {
            return IntPtr.Zero;
        }

        if (FillRect(wParam, ref clientRect, brush.DangerousGetHandle()) == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(1);
    }

    private static uint ToColorRef(Color color)
        => color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    private sealed class SafeGdiBrushHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeGdiBrushHandle(IntPtr handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
            => DeleteObject(handle);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect clientRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int FillRect(IntPtr deviceContext, ref NativeRect clientRect, IntPtr brushHandle);
}
