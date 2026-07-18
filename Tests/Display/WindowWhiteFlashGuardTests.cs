using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CrossETF.Terminal.UiShell.Reference.Views;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class WindowWhiteFlashGuardTests
{
    private static readonly Color DeepWindowColor = Color.FromRgb(0x05, 0x0B, 0x14);

    [Theory]
    [InlineData("T1T6ChartCenterWindow", "WindowBackgroundColor")]
    [InlineData("CapitalPositionWindow", "CapitalWindowBackgroundColor")]
    [InlineData("IndicatorDrawdownWindow", "IndicatorWindowBackgroundColor")]
    [InlineData("ManualDataEntryWindow", "ManualWindowBackgroundColor")]
    [InlineData("MarketMonitorWindow", "MarketMonitorWindowBackgroundColor")]
    public void TargetWindow_UsesOpaqueDeepRootsAndAttachesGuardBeforeSourceInitialization(
        string windowName,
        string colorField)
    {
        string xaml = ReadRepositoryFile("Views", $"{windowName}.xaml");
        string code = ReadRepositoryFile("Views", $"{windowName}.xaml.cs");
        int initializeIndex = code.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        int attachIndex = code.IndexOf($"WindowWhiteFlashGuard.Attach(this, {colorField});", StringComparison.Ordinal);
        string windowDeclaration = xaml[..xaml.IndexOf('>')];

        Assert.Contains("Background=\"#050B14\"", windowDeclaration, StringComparison.Ordinal);
        Assert.True(
            Regex.Matches(xaml, "Background=\\\"#050B14\\\"").Count >= 2,
            $"{windowName} must define the same deep background on the Window and root container.");
        Assert.True(initializeIndex >= 0 && initializeIndex < attachIndex);
        Assert.Contains("private readonly WindowWhiteFlashGuard _whiteFlashGuard;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDarkHwndBackground", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CompositionTarget.BackgroundColor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0\"", windowDeclaration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AllowsTransparency=\"True\"", windowDeclaration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowChrome", xaml + code, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_UsesFullClientEraseAndOwnsNativeLifecycle()
    {
        string code = ReadRepositoryFile("Views", "WindowWhiteFlashGuard.cs");

        Assert.Contains("source.CompositionTarget.BackgroundColor = _backgroundColor;", code, StringComparison.Ordinal);
        Assert.Contains("private const int WmEraseBackground = 0x0014;", code, StringComparison.Ordinal);
        Assert.Contains("source.AddHook(_windowHook);", code, StringComparison.Ordinal);
        Assert.Contains("source.RemoveHook(_windowHook);", code, StringComparison.Ordinal);
        Assert.Contains("GetClientRect(hwnd, out NativeRect clientRect)", code, StringComparison.Ordinal);
        Assert.Contains("FillRect(wParam, ref clientRect, brush.DangerousGetHandle())", code, StringComparison.Ordinal);
        Assert.Contains("SafeHandleZeroOrMinusOneIsInvalid", code, StringComparison.Ordinal);
        Assert.Contains("brush?.Dispose();", code, StringComparison.Ordinal);
        Assert.Contains("DeleteObject(handle)", code, StringComparison.Ordinal);
        Assert.Contains("_window.Closed += Window_Closed;", code, StringComparison.Ordinal);
        Assert.Contains("source.Disposed += Source_Disposed;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_DoesNotHideDelayShowOrAccessBusinessInfrastructure()
    {
        string code = ReadRepositoryFile("Views", "WindowWhiteFlashGuard.cs");
        string[] forbidden =
        {
            "Opacity", "Visibility", "AllowsTransparency", "WindowChrome", "Thread.Sleep", "Task.Delay",
            "DispatcherTimer", ".Show(", "LocalDataRepository", "LocalDatabase", "HttpClient", "MarketDataClient",
            "TradeLog", "AccountReplay", "StrategyDecision"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Guard_AttachIsIdempotentAndCloseReleasesHookAndBrush()
    {
        RunSta(() =>
        {
            var window = new Window { Background = new SolidColorBrush(DeepWindowColor) };
            WindowWhiteFlashGuard first = WindowWhiteFlashGuard.Attach(window, DeepWindowColor);
            WindowWhiteFlashGuard second = WindowWhiteFlashGuard.Attach(window, DeepWindowColor);

            Assert.Same(first, second);
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            var source = Assert.IsType<HwndSource>(HwndSource.FromHwnd(handle));
            Assert.Equal(DeepWindowColor, source.CompositionTarget.BackgroundColor);
            Assert.True(first.IsHookAttached);
            Assert.True(first.HasNativeBrush);
            Assert.Equal(1, first.HookAttachCount);

            window.Close();

            Assert.True(first.IsDisposed);
            Assert.False(first.IsHookAttached);
            Assert.False(first.HasNativeBrush);
        });
    }

    [Fact]
    public void Guard_RepeatedWindowCyclesReleaseEveryNativeResource()
    {
        RunSta(() =>
        {
            for (int index = 0; index < 8; index++)
            {
                var window = new Window { Background = new SolidColorBrush(DeepWindowColor) };
                WindowWhiteFlashGuard guard = WindowWhiteFlashGuard.Attach(window, DeepWindowColor);
                _ = new WindowInteropHelper(window).EnsureHandle();

                Assert.True(guard.IsHookAttached);
                Assert.True(guard.HasNativeBrush);
                Assert.Equal(1, guard.HookAttachCount);

                window.Close();

                Assert.True(guard.IsDisposed);
                Assert.False(guard.IsHookAttached);
                Assert.False(guard.HasNativeBrush);
            }
        });
    }

    [Fact]
    public void Guard_RejectsASecondColorForTheSameWindow()
    {
        RunSta(() =>
        {
            var window = new Window();
            WindowWhiteFlashGuard guard = WindowWhiteFlashGuard.Attach(window, DeepWindowColor);

            Assert.Throws<InvalidOperationException>(
                () => WindowWhiteFlashGuard.Attach(window, Color.FromRgb(0x06, 0x10, 0x1B)));

            guard.Dispose();
            window.Close();
            Assert.True(guard.IsDisposed);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA first-frame guard test timed out.");
        Assert.Null(failure);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(relativeParts));
    }
}
