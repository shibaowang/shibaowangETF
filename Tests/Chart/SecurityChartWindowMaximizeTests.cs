namespace CrossETF.Terminal.UiShell.Reference.Tests.Chart;

public class SecurityChartWindowMaximizeTests
{
    [Fact]
    public void TitleBar_ReservesThreeButtonsInStandardOrder()
    {
        string xaml = ReadRepositoryFile("Views", "SecurityChartWindow.xaml");
        string titleBar = Slice(xaml, "<Grid Grid.Row=\"0\"", "<Border Grid.Row=\"1\"");

        Assert.Equal(3, CountOccurrences(titleBar, "<ColumnDefinition Width=\"42\" />"));
        Assert.Contains("x:Name=\"MaximizeRestoreButton\"", titleBar, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", titleBar, StringComparison.Ordinal);
        Assert.Contains("Click=\"MaximizeRestoreButton_Click\"", titleBar, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ChartButtonStyle}\"", titleBar, StringComparison.Ordinal);
        Assert.Contains("Content=\"□\"", titleBar, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"最大化\"", titleBar, StringComparison.Ordinal);

        int minimize = titleBar.IndexOf("Click=\"MinimizeButton_Click\"", StringComparison.Ordinal);
        int maximize = titleBar.IndexOf("Click=\"MaximizeRestoreButton_Click\"", StringComparison.Ordinal);
        int close = titleBar.IndexOf("Click=\"CloseButton_Click\"", StringComparison.Ordinal);
        Assert.True(minimize >= 0 && minimize < maximize && maximize < close);
    }

    [Fact]
    public void WindowChromeAndResizeConfiguration_RemainLocked()
    {
        string xaml = ReadRepositoryFile("Views", "SecurityChartWindow.xaml");

        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CaptionHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GlassFrameThickness=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeBorderThickness=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UseAeroCaptionButtons=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StateChanged=\"SecurityChartWindow_StateChanged\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MaximizeRestore_UsesSystemWindowStateWithoutManualGeometry()
    {
        string code = ReadRepositoryFile("Views", "SecurityChartWindow.xaml.cs");
        string toggle = Slice(code, "private void ToggleMaximizeRestore()", "private void SecurityChartWindow_StateChanged");

        Assert.Contains("WindowState == WindowState.Normal", toggle, StringComparison.Ordinal);
        Assert.Contains("SystemCommands.MaximizeWindow(this);", toggle, StringComparison.Ordinal);
        Assert.Contains("WindowState == WindowState.Maximized", toggle, StringComparison.Ordinal);
        Assert.Contains("SystemCommands.RestoreWindow(this);", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Width =", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Height =", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Left =", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Top =", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryScreen", toggle, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowState_UpdatesIconTooltipAndAccessibleName()
    {
        string code = ReadRepositoryFile("Views", "SecurityChartWindow.xaml.cs");
        string update = Slice(code, "private void UpdateMaximizeRestoreButton()", "private void CloseButton_Click");

        Assert.Contains("WindowState == WindowState.Maximized", update, StringComparison.Ordinal);
        Assert.Contains("isMaximized ? \"❐\" : \"□\"", update, StringComparison.Ordinal);
        Assert.Contains("isMaximized ? \"还原\" : \"最大化\"", update, StringComparison.Ordinal);
        Assert.Contains("MaximizeRestoreButton.ToolTip = action", update, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(MaximizeRestoreButton, action)", update, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBarDoubleClick_TogglesBeforeDragMove()
    {
        string code = ReadRepositoryFile("Views", "SecurityChartWindow.xaml.cs");
        string handler = Slice(code, "private void TitleBar_MouseLeftButtonDown", "private void MinimizeButton_Click");

        Assert.Contains("e.ChangedButton != MouseButton.Left", handler, StringComparison.Ordinal);
        Assert.Contains("e.ClickCount == 2", handler, StringComparison.Ordinal);
        Assert.Contains("ToggleMaximizeRestore();", handler, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", handler, StringComparison.Ordinal);
        Assert.Contains("WindowState == WindowState.Normal", handler, StringComparison.Ordinal);
        Assert.Contains("DragMove();", handler, StringComparison.Ordinal);
        Assert.True(
            handler.IndexOf("e.ClickCount == 2", StringComparison.Ordinal)
            < handler.IndexOf("DragMove();", StringComparison.Ordinal));
    }

    [Fact]
    public void MaximizeHandlers_AreUiOnlyAndPreserveExistingWindowCommands()
    {
        string code = ReadRepositoryFile("Views", "SecurityChartWindow.xaml.cs");
        string handlers = Slice(code, "private void MaximizeRestoreButton_Click", "protected override void OnClosed");

        Assert.Contains("private void MinimizeButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("=> WindowState = WindowState.Minimized;", code, StringComparison.Ordinal);
        Assert.Contains("private void CloseButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("=> Close();", code, StringComparison.Ordinal);
        Assert.Contains("private void ChartCanvas_SizeChanged", code, StringComparison.Ordinal);
        Assert.Contains("=> DrawCharts();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("_coordinator", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("Subscribe", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsubscribe", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewports", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLog", handlers, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker was not found: {startMarker}");
        Assert.True(end > start, $"End marker was not found after start: {endMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
