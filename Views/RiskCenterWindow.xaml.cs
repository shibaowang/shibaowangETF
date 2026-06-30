using System.Collections.ObjectModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class RiskCenterWindow : Window
{
    private readonly LocalDataRepository _repository;

    public RiskCenterWindow(LocalDataRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        WindowInteractionEffects.ApplySmoothOpen(this);
        DataContext = this;
        SourceInitialized += (_, _) => TryApplyDarkTitleBar();
        Loaded += (_, _) => RefreshAlertLogs(false);
    }

    public ObservableCollection<RiskAlertLogRow> Rows { get; } = new();

    public static IReadOnlyList<RiskAlertLogRow> BuildRows(IEnumerable<AlertLogRecord> records)
        => records.Select(record => new RiskAlertLogRow(
            record.CreatedAt,
            record.AlertType,
            record.Severity,
            BuildTarget(record),
            record.Title,
            DisplayText(record.WechatStatus),
            DisplayText(record.VoiceStatus),
            BuildError(record),
            DisplayText(record.Source))).ToArray();

    private void RefreshLogsButton_Click(object sender, RoutedEventArgs e)
        => RefreshAlertLogs(true);

    private void RefreshAlertLogs(bool showSuccessStatus)
    {
        try
        {
            Rows.Clear();
            foreach (RiskAlertLogRow row in BuildRows(_repository.ReadAlertLogs(100)))
            {
                Rows.Add(row);
            }

            EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AlertLogGrid.Visibility = Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (showSuccessStatus)
            {
                SetStatus("预警日志已刷新。", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"刷新预警日志失败：{ex.Message}", true);
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowClearAlertLogConfirmation())
        {
            SetStatus("已取消清空操作。", false);
            return;
        }

        try
        {
            _repository.ClearAlertLogs();
            RefreshAlertLogs(false);
            SetStatus("已清空预警日志。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"清空预警日志失败：{ex.Message}", true);
        }
    }

    private bool ShowClearAlertLogConfirmation()
    {
        var dialog = new Window
        {
            Title = "确认清空预警日志",
            Owner = this,
            Width = 520,
            Height = 260,
            MinWidth = 480,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = BrushFrom("#050B14"),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            ShowInTaskbar = false
        };

        WindowInteractionEffects.ApplySmoothOpen(dialog);
        dialog.SourceInitialized += (_, _) => TryApplyDarkTitleBar(dialog);

        var root = new Grid
        {
            Margin = new Thickness(22),
            Background = BrushFrom("#050B14")
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "确认清空全部预警日志吗？",
            Foreground = BrushFrom("#EAF6FF"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });

        var body = new TextBlock
        {
            Text = "此操作只清空 alert_log，不影响预警去重状态、系统设置、TradeLog 和交易数据。\n清空后不可恢复。",
            Foreground = BrushFrom("#9FB7C8"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
            Margin = new Thickness(0, 22, 0, 0)
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button cancelButton = CreateDialogButton("取消");
        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };
        Button confirmButton = CreateDialogButton("确认清空");
        confirmButton.Margin = new Thickness(12, 0, 0, 0);
        confirmButton.BorderBrush = BrushFrom("#EF4444");
        confirmButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        return dialog.ShowDialog() == true;
    }

    private static Button CreateDialogButton(string text)
        => new()
        {
            Content = text,
            MinWidth = 92,
            Padding = new Thickness(18, 8, 18, 8),
            Background = BrushFrom("#0B2B42"),
            Foreground = BrushFrom("#EAF6FF"),
            BorderBrush = BrushFrom("#2A6F93"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };

    private static string BuildTarget(AlertLogRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.StrategyCode))
        {
            return string.IsNullOrWhiteSpace(record.ActualCode)
                ? record.StrategyCode
                : $"{record.StrategyCode}/{record.ActualCode}";
        }

        return string.IsNullOrWhiteSpace(record.ActualCode) ? "--" : record.ActualCode;
    }

    private static string BuildError(AlertLogRecord record)
    {
        string? error = !string.IsNullOrWhiteSpace(record.WechatError)
            ? record.WechatError
            : record.VoiceError;
        if (!string.IsNullOrWhiteSpace(error))
        {
            return DisplayText(error);
        }

        return BuildEventDetail(record.Content);
    }

    private static string BuildEventDetail(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "--";
        }

        string normalized = content
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);
        string? message = null;
        foreach (string rawLine in normalized.Split('\n'))
        {
            string line = WebUtility.HtmlDecode(rawLine).Trim();
            if (line.StartsWith("详情：", StringComparison.Ordinal)
                || line.StartsWith("原因：", StringComparison.Ordinal))
            {
                return DisplayText(line);
            }

            if (message is null && line.StartsWith("消息：", StringComparison.Ordinal))
            {
                message = line;
            }
        }

        if (message is not null)
        {
            return DisplayText(message);
        }

        return DisplayText(WebUtility.HtmlDecode(normalized));
    }

    private static string DisplayText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = BrushFrom(isError ? "#EF4444" : "#9FB7C8");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void TryApplyDarkTitleBar()
        => TryApplyDarkTitleBar(this);

    private static void TryApplyDarkTitleBar(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, Marshal.SizeOf<int>());
            _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, Marshal.SizeOf<int>());
        }
        catch
        {
            // Keep the native title bar unchanged on Windows builds without this DWM attribute.
        }
    }

    private static SolidColorBrush BrushFrom(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}

public sealed record RiskAlertLogRow(
    string CreatedAt,
    string AlertType,
    string Severity,
    string Target,
    string Title,
    string WechatStatus,
    string VoiceStatus,
    string Error,
    string Source);
