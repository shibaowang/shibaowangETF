using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class SecurityChartWindow : Window
{
    private static readonly SolidColorBrush RedBrush = BrushFrom("#EF4444");
    private static readonly SolidColorBrush GreenBrush = BrushFrom("#84CC16");
    private static readonly SolidColorBrush BlueBrush = BrushFrom("#3B82F6");
    private static readonly SolidColorBrush NeutralVolumeBrush = BrushFrom("#2A6688");
    private static readonly SolidColorBrush TextBrush = BrushFrom("#EAF6FF");
    private static readonly SolidColorBrush MutedBrush = BrushFrom("#9FB7C8");
    private static readonly SolidColorBrush GridBrush = BrushFrom("#143247");
    private static readonly SolidColorBrush PreviousCloseLineBrush = BrushFrom("#EAF6FF");
    private static readonly SolidColorBrush SelectedButtonBrush = BrushFrom("#1A5D80");
    private static readonly SolidColorBrush NormalButtonBrush = BrushFrom("#082235");

    private readonly ChartSecurityInfo _security;
    private SecurityChartSnapshot? _snapshot;
    private bool _isClosed;

    public SecurityChartWindow(ChartSecurityInfo security)
    {
        _security = security;
        InitializeComponent();
        Period = SecurityChartPeriod.Intraday;
        SubPanel = SecurityChartSubPanel.Volume;
        WindowTitleText.Text = $"标的走势图 - {_security.StrategyCode} {_security.Name}";
        Title = WindowTitleText.Text;
        SecurityNameText.Text = _security.Name;
        SecurityCodeText.Text = _security.StrategyCode + " / " + _security.ActualCode;
        UpdateButtonState();
    }

    public SecurityChartPeriod Period { get; private set; }

    public SecurityChartSubPanel SubPanel { get; private set; }

    public bool IsClosed => _isClosed;

    public event EventHandler<SecurityChartPeriodChangedEventArgs>? PeriodChanged;

    public void UpdateSnapshot(SecurityChartSnapshot snapshot)
    {
        if (_isClosed)
        {
            return;
        }

        if (!string.Equals(snapshot.Security.StrategyCode, _security.StrategyCode, StringComparison.OrdinalIgnoreCase)
            || snapshot.Period != Period
            || snapshot.SubPanel != SubPanel)
        {
            return;
        }

        _snapshot = snapshot;
        UpdateHeader(snapshot);
        DrawCharts();
    }

    private void UpdateHeader(SecurityChartSnapshot snapshot)
    {
        PriceText.Text = FormatNumber(snapshot.Quote?.Price);
        double? displayChangePercent = ChartPercentFormatter.NormalizeRatioForDisplay(snapshot.ChangePercent);
        ChangeText.Text = ChartPercentFormatter.FormatRatio(snapshot.ChangePercent);
        ChangeText.Foreground = SignedBrush(displayChangePercent);
        if (snapshot.Security.InstrumentType == ChartInstrumentType.Index)
        {
            PremiumText.Text = "--";
            PremiumText.Foreground = MutedBrush;
        }
        else
        {
            double? premium = EtfDecisionTableMetrics.CalculatePremiumRate(snapshot.Quote);
            PremiumText.Text = premium.HasValue ? FormatSignedPercent(premium.Value * 100) : "--";
            PremiumText.Foreground = SignedBrush(premium);
        }

        StatusText.Text = CompactStatusText(snapshot.MainStatus.Message);
        StatusText.ToolTip = snapshot.MainStatus.Message;
        StatusText.Foreground = snapshot.MainStatus.IsReady ? TextBrush : BrushFrom("#FFB347");
        UpdatedText.Text = snapshot.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        FooterStatusText.Text = BuildFooter(snapshot);
    }

    public static string CompactStatusText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "--";
        }

        string text = message.Trim();
        if (text.Contains("无真实分时缓存", StringComparison.OrdinalIgnoreCase)
            || text.Contains("无可用分时缓存", StringComparison.OrdinalIgnoreCase))
        {
            return "无分时";
        }

        if (text.Contains("熔断", StringComparison.OrdinalIgnoreCase))
        {
            return "接口熔断";
        }

        if (text.Contains("限频", StringComparison.OrdinalIgnoreCase))
        {
            return "接口限频";
        }

        if (text.Contains("后台真实分时缓存", StringComparison.OrdinalIgnoreCase))
        {
            return "分时缓存";
        }

        if (text.Contains("真实分时缓存", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("quote", StringComparison.OrdinalIgnoreCase) ? "缓存+Quote" : "分时缓存";
        }

        if (text.Contains("真实分时数据", StringComparison.OrdinalIgnoreCase))
        {
            return "真实分时";
        }

        if (text.Contains("实时 quote 尾点", StringComparison.OrdinalIgnoreCase))
        {
            return "实时Quote";
        }

        if (text.Contains("最近真实日K缓存", StringComparison.OrdinalIgnoreCase))
        {
            return "日K缓存";
        }

        if (text.Contains("真实日K", StringComparison.OrdinalIgnoreCase)
            || text.Contains("日K接口缓存", StringComparison.OrdinalIgnoreCase))
        {
            return "真实日K";
        }

        if (text.Contains("月线", StringComparison.OrdinalIgnoreCase)
            || text.Contains("非日K", StringComparison.OrdinalIgnoreCase))
        {
            return "非日K数据";
        }

        if (text.Contains("DailyLike", StringComparison.OrdinalIgnoreCase))
        {
            return "无日K";
        }

        if (text.Contains("MACD数据不足", StringComparison.OrdinalIgnoreCase))
        {
            return "MACD不足";
        }

        if (text.Contains("成交量数据不可用", StringComparison.OrdinalIgnoreCase))
        {
            return "无成交量";
        }

        if (text.Contains("非交易时段", StringComparison.OrdinalIgnoreCase))
        {
            return "非交易时段";
        }

        return text.Length <= 10 ? text : text[..10] + "...";
    }

    private string BuildFooter(SecurityChartSnapshot snapshot)
    {
        string main = snapshot.MainStatus.Message;
        string volume = snapshot.SubPanel == SecurityChartSubPanel.Volume ? snapshot.VolumeStatus.Message : snapshot.MacdStatus.Message;
        string previousClose = snapshot.Period == SecurityChartPeriod.Intraday && !snapshot.PreviousClose.HasValue ? "；昨收线不可用" : string.Empty;
        string tail = snapshot.HasQuoteTail
                      && !main.Contains("最新点由", StringComparison.OrdinalIgnoreCase)
                      && !main.Contains("最新价来自", StringComparison.OrdinalIgnoreCase)
            ? "；最新点由真实 quote 驱动，仅用于显示"
            : string.Empty;
        return main + "；" + volume + previousClose + tail;
    }

    private void DrawCharts()
    {
        MainChartCanvas.Children.Clear();
        SubChartCanvas.Children.Clear();
        SecurityChartSnapshot? snapshot = _snapshot;
        if (snapshot is null)
        {
            return;
        }

        if (Period == SecurityChartPeriod.Intraday)
        {
            DrawIntraday(snapshot);
        }
        else
        {
            DrawKLines(snapshot);
        }

        DrawSubPanel(snapshot);
    }

    private void DrawIntraday(SecurityChartSnapshot snapshot)
    {
        DrawGrid(MainChartCanvas);
        double width = MainChartCanvas.ActualWidth;
        double height = MainChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        IntradayPoint[] points = VisibleIntradayPoints(snapshot);
        DrawIntradayAxisLabels(MainChartCanvas, plot, snapshot, points);
        if (points.Length == 0)
        {
            AddCenteredText(MainChartCanvas, snapshot.MainStatus.Message);
            return;
        }

        double min;
        double max;
        if (IntradayPriceAxisCalculator.TryCreate(snapshot.PreviousClose, points.Select(point => point.Price), out IntradayPriceAxis priceAxis))
        {
            min = priceAxis.DisplayMin;
            max = priceAxis.DisplayMax;
            DrawPreviousCloseLine(MainChartCanvas, plot, priceAxis.PreviousClose, min, max);
        }
        else
        {
            min = points.Min(point => point.Price);
            max = points.Max(point => point.Price);
            ExpandRange(ref min, ref max);
            AddText(MainChartCanvas, "昨收线不可用", plot.Left, plot.Top + 4, 12, BrushFrom("#FFB347"));
        }

        IntradayPoint[] linePoints = points
            .Where(point => ShouldConnectIntradayPoint(snapshot, point))
            .ToArray();
        IntradayPoint? quoteTail = points.LastOrDefault(point => point.IsQuoteTail);
        Point latestPoint = new(plot.Left, plot.Bottom);
        if (linePoints.Length > 0)
        {
            var polyline = new Polyline
            {
                Stroke = BlueBrush,
                StrokeThickness = 1.6
            };
            MainChartCanvas.Children.Add(polyline);
            for (int i = 0; i < linePoints.Length; i++)
            {
                IntradayPoint point = linePoints[i];
                double x = XByIntradayPoint(plot, snapshot, point.Time, i, linePoints.Length);
                double y = YByValue(plot, point.Price, min, max);
                latestPoint = new Point(x, y);
                polyline.Points.Add(latestPoint);
            }
        }

        if (quoteTail is not null && snapshot.Security.InstrumentType == ChartInstrumentType.Index)
        {
            Point quotePoint = new(
                XByIntradayPoint(plot, snapshot, quoteTail.Time, points.Length - 1, points.Length),
                YByValue(plot, quoteTail.Price, min, max));
            AddQuoteTailMarker(MainChartCanvas, quotePoint);
            AddText(MainChartCanvas, FormatNumber(quoteTail.Price), Math.Min(plot.Right - 62, quotePoint.X + 6), quotePoint.Y - 18, 13, TextBrush);
            return;
        }

        if (linePoints.Length > 0)
        {
            IntradayPoint last = linePoints[^1];
            AddPointMarker(MainChartCanvas, latestPoint, BlueBrush);
            AddText(MainChartCanvas, FormatNumber(last.Price), Math.Min(plot.Right - 62, latestPoint.X + 6), latestPoint.Y - 18, 13, TextBrush);
        }
    }

    private void DrawKLines(SecurityChartSnapshot snapshot)
    {
        IReadOnlyList<KLinePoint> points = snapshot.KLines;
        DrawGrid(MainChartCanvas);
        if (points.Count == 0)
        {
            AddCenteredText(MainChartCanvas, snapshot.MainStatus.Message);
            return;
        }

        double width = MainChartCanvas.ActualWidth;
        double height = MainChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        double min = points.Min(point => point.Low);
        double max = points.Max(point => point.High);
        ExpandRange(ref min, ref max);
        double step = plot.Width / Math.Max(1, points.Count);
        double bodyWidth = Math.Clamp(step * 0.56, 2, 10);

        for (int i = 0; i < points.Count; i++)
        {
            KLinePoint point = points[i];
            double x = plot.Left + step * i + step / 2;
            double yHigh = YByValue(plot, point.High, min, max);
            double yLow = YByValue(plot, point.Low, min, max);
            double yOpen = YByValue(plot, point.Open, min, max);
            double yClose = YByValue(plot, point.Close, min, max);
            Brush brush = point.Close >= point.Open ? RedBrush : GreenBrush;
            MainChartCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = yHigh,
                Y2 = yLow,
                Stroke = brush,
                StrokeThickness = 1
            });
            double top = Math.Min(yOpen, yClose);
            double bodyHeight = Math.Max(2, Math.Abs(yOpen - yClose));
            var body = new Rectangle
            {
                Width = bodyWidth,
                Height = bodyHeight,
                Fill = brush,
                Stroke = brush,
                StrokeThickness = 1,
                Opacity = point.IsQuoteAdjusted ? 0.88 : 1
            };
            Canvas.SetLeft(body, x - bodyWidth / 2);
            Canvas.SetTop(body, top);
            MainChartCanvas.Children.Add(body);
        }

        KLinePoint latest = points[^1];
        AddText(MainChartCanvas, latest.Date.ToString("MM-dd", CultureInfo.InvariantCulture), plot.Right - 42, plot.Bottom + 5, 12, MutedBrush);
        AddText(MainChartCanvas, FormatNumber(latest.Close), plot.Right - 62, YByValue(plot, latest.Close, min, max) - 18, 13, TextBrush);
    }

    private void DrawSubPanel(SecurityChartSnapshot snapshot)
    {
        DrawGrid(SubChartCanvas);
        if (SubPanel == SecurityChartSubPanel.Macd)
        {
            DrawMacd(snapshot);
        }
        else
        {
            DrawVolume(snapshot);
        }
    }

    private void DrawVolume(SecurityChartSnapshot snapshot)
    {
        if (Period == SecurityChartPeriod.Intraday)
        {
            DrawIntradayVolume(snapshot);
            return;
        }

        KLinePoint[] points = snapshot.KLines.ToArray();
        if (points.Length == 0 || points.All(point => !point.Volume.HasValue))
        {
            AddCenteredText(SubChartCanvas, snapshot.VolumeStatus.Message);
            return;
        }

        double width = SubChartCanvas.ActualWidth;
        double height = SubChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        double max = KLineVolumeMetrics.MaxVisibleVolume(points);
        if (max <= 0)
        {
            AddCenteredText(SubChartCanvas, "成交量数据不可用");
            return;
        }

        double step = plot.Width / Math.Max(1, points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            KLinePoint point = points[i];
            double barHeight = KLineVolumeMetrics.ScaleBarHeight(point.Volume, max, plot.Height);
            if (barHeight <= 0)
            {
                continue;
            }

            KLineVolumeColorKind color = KLineVolumeColorResolver.Resolve(point.Open, point.Close);
            var rect = new Rectangle
            {
                Width = Math.Max(1, step * 0.55),
                Height = barHeight,
                Fill = KLineVolumeBrush(color),
                Opacity = 0.82
            };
            Canvas.SetLeft(rect, plot.Left + i * step + step * 0.22);
            Canvas.SetTop(rect, plot.Bottom - barHeight);
            SubChartCanvas.Children.Add(rect);
        }

        AddText(SubChartCanvas, "成交量", plot.Left, 8, 12, MutedBrush);
    }

    private void DrawIntradayVolume(SecurityChartSnapshot snapshot)
    {
        IntradayPoint[] points = VisibleIntradayPoints(snapshot);
        IntradayPoint[] volumePoints = points
            .Where(point => point.Volume.HasValue)
            .ToArray();
        if (points.Length == 0 || volumePoints.Length == 0)
        {
            AddCenteredText(SubChartCanvas, snapshot.VolumeStatus.Message);
            return;
        }

        double width = SubChartCanvas.ActualWidth;
        double height = SubChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        double max = IntradayVolumeNormalizer.MaxVisibleMinuteVolume(volumePoints);
        if (max <= 0)
        {
            AddCenteredText(SubChartCanvas, "成交量数据不可用");
            return;
        }

        double totalAxisMinutes = UsesUsEasternIntradayAxis(snapshot)
            ? IntradayTradingTimeAxis.UsEasternTotalTradingMinutes
            : IntradayTradingTimeAxis.TotalTradingMinutes;
        double barWidth = Math.Clamp(plot.Width / totalAxisMinutes * 0.72, 1, 4);
        double? previousPrice = null;
        IntradayVolumeColorKind? previousColor = null;
        for (int i = 0; i < points.Length; i++)
        {
            IntradayPoint point = points[i];
            IntradayVolumeColorKind color = IntradayVolumeColorResolver.Resolve(point.Price, previousPrice, previousColor);
            if (point.Volume.HasValue)
            {
                double volume = Math.Max(0, point.Volume.Value);
                double barHeight = IntradayVolumeNormalizer.ScaleBarHeight(volume, max, plot.Height);
                double x = XByIntradayPoint(plot, snapshot, point.Time, i, points.Length);
                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(1, barHeight),
                    Fill = IntradayVolumeBrush(color),
                    Opacity = 0.82
                };
                Canvas.SetLeft(rect, x - barWidth / 2);
                Canvas.SetTop(rect, plot.Bottom - barHeight);
                SubChartCanvas.Children.Add(rect);
            }

            previousPrice = point.Price;
            previousColor = color;
        }

        AddText(SubChartCanvas, "成交量", plot.Left, 8, 12, MutedBrush);
    }

    private void DrawMacd(SecurityChartSnapshot snapshot)
    {
        MacdPoint[] points = Period == SecurityChartPeriod.Intraday
            ? VisibleIntradayMacdPoints(snapshot)
            : snapshot.Macd.ToArray();
        if (points.Length == 0)
        {
            AddCenteredText(SubChartCanvas, snapshot.MacdStatus.Message);
            return;
        }

        double width = SubChartCanvas.ActualWidth;
        double height = SubChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        double min = points.Min(point => Math.Min(point.Bar, Math.Min(point.Dif, point.Dea)));
        double max = points.Max(point => Math.Max(point.Bar, Math.Max(point.Dif, point.Dea)));
        ExpandRange(ref min, ref max);
        double zeroY = YByValue(plot, 0, min, max);
        SubChartCanvas.Children.Add(new Line { X1 = plot.Left, X2 = plot.Right, Y1 = zeroY, Y2 = zeroY, Stroke = GridBrush, StrokeThickness = 1 });
        bool isIntraday = Period == SecurityChartPeriod.Intraday;
        double step = isIntraday
            ? plot.Width / (UsesUsEasternIntradayAxis(snapshot)
                ? IntradayTradingTimeAxis.UsEasternTotalTradingMinutes
                : IntradayTradingTimeAxis.TotalTradingMinutes)
            : plot.Width / Math.Max(1, points.Length);
        double barWidth = isIntraday
            ? Math.Clamp(step * 0.72, 1, 4)
            : Math.Max(1, step * 0.45);
        var difLine = new Polyline { Stroke = BrushFrom("#F59E0B"), StrokeThickness = 1.3 };
        var deaLine = new Polyline { Stroke = BlueBrush, StrokeThickness = 1.3 };
        for (int i = 0; i < points.Length; i++)
        {
            MacdPoint point = points[i];
            if (!TryGetMacdX(plot, snapshot, point, i, points.Length, isIntraday, out double x))
            {
                continue;
            }

            double barY = YByValue(plot, point.Bar, min, max);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, Math.Abs(zeroY - barY)),
                Fill = point.Bar >= 0 ? RedBrush : GreenBrush,
                Opacity = 0.72
            };
            Canvas.SetLeft(bar, x - bar.Width / 2);
            Canvas.SetTop(bar, Math.Min(zeroY, barY));
            SubChartCanvas.Children.Add(bar);
            difLine.Points.Add(new Point(x, YByValue(plot, point.Dif, min, max)));
            deaLine.Points.Add(new Point(x, YByValue(plot, point.Dea, min, max)));
        }

        SubChartCanvas.Children.Add(difLine);
        SubChartCanvas.Children.Add(deaLine);
        AddText(SubChartCanvas, "MACD DIF / DEA", plot.Left, 8, 12, MutedBrush);
    }

    private static void DrawGrid(Canvas canvas)
    {
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Rect plot = PlotRect(width, height);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Top + plot.Height / 4 * i;
            canvas.Children.Add(new Line { X1 = plot.Left, X2 = plot.Right, Y1 = y, Y2 = y, Stroke = GridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 5 } });
        }

        for (int i = 0; i <= 4; i++)
        {
            double x = plot.Left + plot.Width / 4 * i;
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = plot.Top, Y2 = plot.Bottom, Stroke = GridBrush, StrokeThickness = 0.8, Opacity = 0.55 });
        }
    }

    private static void DrawPreviousCloseLine(Canvas canvas, Rect plot, double previousClose, double min, double max)
    {
        double y = YByValue(plot, previousClose, min, max);
        canvas.Children.Add(new Line
        {
            X1 = plot.Left,
            X2 = plot.Right,
            Y1 = y,
            Y2 = y,
            Stroke = PreviousCloseLineBrush,
            StrokeThickness = 1.1,
            Opacity = 0.82,
            StrokeDashArray = new DoubleCollection { 7, 5 }
        });
        AddText(canvas, "昨收", plot.Right - 34, y - 16, 11, PreviousCloseLineBrush);
    }

    private static Rect PlotRect(double width, double height)
        => new(42, 18, Math.Max(20, width - 72), Math.Max(20, height - 48));

    private static double XByIndex(Rect plot, int index, int count)
        => count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * index / (count - 1);

    private static double XByTradingTime(Rect plot, DateTime time)
        => IntradayTradingTimeAxis.TryGetXRatio(time, out double ratio)
            ? plot.Left + plot.Width * ratio
            : plot.Left;

    private static double XByIntradayPoint(Rect plot, SecurityChartSnapshot snapshot, DateTime time, int index, int count)
        => UsesStandardIntradayAxis(snapshot)
            ? XByTradingTime(plot, time)
            : XByUsEasternTime(plot, time);

    private static bool TryGetMacdX(Rect plot, SecurityChartSnapshot snapshot, MacdPoint point, int index, int count, bool isIntraday, out double x)
    {
        if (isIntraday)
        {
            if (UsesUsEasternIntradayAxis(snapshot))
            {
                if (!IntradayTradingTimeAxis.TryGetUsEasternXRatio(point.Date, out double easternRatio))
                {
                    x = 0;
                    return false;
                }

                x = plot.Left + plot.Width * easternRatio;
                return true;
            }

            if (!IntradayTradingTimeAxis.TryGetXRatio(point.Date, out double ratio))
            {
                x = 0;
                return false;
            }

            x = plot.Left + plot.Width * ratio;
            return true;
        }

        x = plot.Left + plot.Width / Math.Max(1, count) * index + plot.Width / Math.Max(1, count) / 2;
        return true;
    }

    private static bool UsesStandardIntradayAxis(SecurityChartSnapshot snapshot)
        => snapshot.Security.InstrumentType != ChartInstrumentType.Index;

    private static bool UsesUsEasternIntradayAxis(SecurityChartSnapshot snapshot)
        => snapshot.Security.InstrumentType == ChartInstrumentType.Index;

    private static double XByUsEasternTime(Rect plot, DateTime time)
        => IntradayTradingTimeAxis.TryGetUsEasternXRatio(time, out double ratio)
            ? plot.Left + plot.Width * ratio
            : plot.Left;

    private static IntradayPoint[] VisibleIntradayPoints(SecurityChartSnapshot snapshot)
        => snapshot.IntradayPoints
            .Where(point => point.Price > 0
                            && (!UsesStandardIntradayAxis(snapshot)
                                || IntradayTradingTimeAxis.IsTradingTime(point.Time)))
            .Where(point => !UsesUsEasternIntradayAxis(snapshot)
                            || IntradayTradingTimeAxis.TryGetUsEasternXRatio(point.Time, out _))
            .OrderBy(point => point.Time)
            .ToArray();

    public static bool ShouldConnectIntradayPoint(SecurityChartSnapshot snapshot, IntradayPoint point)
        // LOCKED: Index quote tails stay independent; do not connect partial real intraday cache directly to quote.
        => snapshot.Security.InstrumentType != ChartInstrumentType.Index || !point.IsQuoteTail;

    private static MacdPoint[] VisibleIntradayMacdPoints(SecurityChartSnapshot snapshot)
        => snapshot.Macd
            .Where(point => !UsesStandardIntradayAxis(snapshot)
                            || IntradayTradingTimeAxis.IsTradingTime(point.Date))
            .Where(point => !UsesUsEasternIntradayAxis(snapshot)
                            || IntradayTradingTimeAxis.TryGetUsEasternXRatio(point.Date, out _))
            .OrderBy(point => point.Date)
            .ToArray();

    private static double YByValue(Rect plot, double value, double min, double max)
        => plot.Bottom - (value - min) / (max - min) * plot.Height;

    private static void ExpandRange(ref double min, ref double max)
    {
        if (Math.Abs(max - min) < 0.000001)
        {
            min -= Math.Max(0.01, Math.Abs(min) * 0.01);
            max += Math.Max(0.01, Math.Abs(max) * 0.01);
            return;
        }

        double padding = (max - min) * 0.08;
        min -= padding;
        max += padding;
    }

    private static void AddCenteredText(Canvas canvas, string message)
    {
        double width = Math.Max(1, canvas.ActualWidth);
        double height = Math.Max(1, canvas.ActualHeight);
        var text = new TextBlock
        {
            Text = message,
            Foreground = BrushFrom("#FFB347"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(text, Math.Max(10, width / 2 - 120));
        Canvas.SetTop(text, Math.Max(10, height / 2 - 12));
        canvas.Children.Add(text);
    }

    private static void AddText(Canvas canvas, string text, double x, double y, double size, Brush brush)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = size
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static void DrawIntradayAxisLabels(Canvas canvas, Rect plot, SecurityChartSnapshot snapshot, IReadOnlyList<IntradayPoint> points)
    {
        if (UsesUsEasternIntradayAxis(snapshot))
        {
            DrawUsEasternIntradayAxisLabels(canvas, plot);
            return;
        }

        foreach (IntradayAxisTick tick in IntradayTradingTimeAxis.StandardTicks)
        {
            double x = plot.Left + plot.Width * tick.Ratio;
            double left = tick.Label switch
            {
                "09:30" => x,
                "11:30" => x - 44,
                "13:00" => x + 5,
                "15:00" => x - 42,
                _ => x - 21
            };
            AddText(canvas, tick.Label, left, plot.Bottom + 5, 12, MutedBrush);
        }
    }

    private static void DrawUsEasternIntradayAxisLabels(Canvas canvas, Rect plot)
    {
        foreach (IntradayAxisTick tick in IntradayTradingTimeAxis.UsEasternTicks)
        {
            double x = plot.Left + plot.Width * tick.Ratio;
            double left = tick.Label switch
            {
                "09:30" => x,
                "16:00" => x - 42,
                _ => x - 21
            };
            AddText(canvas, tick.Label, left, plot.Bottom + 5, 12, MutedBrush);
        }

        AddText(canvas, "美东时间", plot.Left + plot.Width / 2 - 24, plot.Bottom + 5, 12, MutedBrush);
    }

    private static void AddPointMarker(Canvas canvas, Point point, Brush brush)
    {
        var marker = new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = brush,
            Stroke = BrushFrom("#071B2A"),
            StrokeThickness = 1
        };
        Canvas.SetLeft(marker, point.X - marker.Width / 2);
        Canvas.SetTop(marker, point.Y - marker.Height / 2);
        canvas.Children.Add(marker);
    }

    private static void AddQuoteTailMarker(Canvas canvas, Point point)
    {
        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = BrushFrom("#071B2A"),
            Stroke = BlueBrush,
            StrokeThickness = 1.8
        };
        Canvas.SetLeft(marker, point.X - marker.Width / 2);
        Canvas.SetTop(marker, point.Y - marker.Height / 2);
        canvas.Children.Add(marker);
    }

    private void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        Period = sender switch
        {
            Button button when button == IntradayButton => SecurityChartPeriod.Intraday,
            Button button when button == WeeklyButton => SecurityChartPeriod.Weekly,
            Button button when button == MonthlyButton => SecurityChartPeriod.Monthly,
            _ => SecurityChartPeriod.Daily
        };
        UpdateButtonState();
        PeriodChanged?.Invoke(this, new SecurityChartPeriodChangedEventArgs(Period, SubPanel));
    }

    private void SubPanelButton_Click(object sender, RoutedEventArgs e)
    {
        SubPanel = sender == MacdButton ? SecurityChartSubPanel.Macd : SecurityChartSubPanel.Volume;
        UpdateButtonState();
        PeriodChanged?.Invoke(this, new SecurityChartPeriodChangedEventArgs(Period, SubPanel));
    }

    private void UpdateButtonState()
    {
        IntradayButton.Background = Period == SecurityChartPeriod.Intraday ? SelectedButtonBrush : NormalButtonBrush;
        DailyButton.Background = Period == SecurityChartPeriod.Daily ? SelectedButtonBrush : NormalButtonBrush;
        WeeklyButton.Background = Period == SecurityChartPeriod.Weekly ? SelectedButtonBrush : NormalButtonBrush;
        MonthlyButton.Background = Period == SecurityChartPeriod.Monthly ? SelectedButtonBrush : NormalButtonBrush;
        VolumeButton.Background = SubPanel == SecurityChartSubPanel.Volume ? SelectedButtonBrush : NormalButtonBrush;
        MacdButton.Background = SubPanel == SecurityChartSubPanel.Macd ? SelectedButtonBrush : NormalButtonBrush;
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => DrawCharts();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        base.OnClosed(e);
    }

    private static string FormatNumber(double? value)
        => value.HasValue ? value.Value.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') : "--";

    private static string FormatSignedPercent(double? value)
        => value.HasValue ? (value.Value > 0 ? "+" : string.Empty) + value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "--";

    private static Brush SignedBrush(double? value)
        => value > 0 ? RedBrush : value < 0 ? GreenBrush : TextBrush;

    private static Brush IntradayVolumeBrush(IntradayVolumeColorKind color)
        => color switch
        {
            IntradayVolumeColorKind.Up => RedBrush,
            IntradayVolumeColorKind.Down => GreenBrush,
            _ => NeutralVolumeBrush
        };

    private static Brush KLineVolumeBrush(KLineVolumeColorKind color)
        => color == KLineVolumeColorKind.Up ? RedBrush : GreenBrush;

    private static SolidColorBrush BrushFrom(string hex)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

public sealed record SecurityChartPeriodChangedEventArgs(
    SecurityChartPeriod Period,
    SecurityChartSubPanel SubPanel);
