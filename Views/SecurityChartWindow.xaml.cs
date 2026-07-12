using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private static readonly SolidColorBrush Ma5Brush = BrushFrom("#E5E7EB");
    private static readonly SolidColorBrush Ma10Brush = BrushFrom("#FACC15");
    private static readonly SolidColorBrush Ma20Brush = BrushFrom("#F472B6");
    private static readonly SolidColorBrush Ma60Brush = BrushFrom("#22D3EE");

    private readonly ChartSecurityInfo _security;
    private readonly ChartViewportStore _viewportStore = new();
    private readonly Dictionary<int, bool> _movingAverageVisibility = new()
    {
        [5] = true,
        [10] = true,
        [20] = true,
        [60] = true
    };
    private SecurityChartSnapshot? _snapshot;
    private IReadOnlyList<TradeLogRecord> _tradeLogs = Array.Empty<TradeLogRecord>();
    private IReadOnlyList<StrategyConfigRecord> _strategies = Array.Empty<StrategyConfigRecord>();
    private IReadOnlyDictionary<int, MovingAverageSeries> _movingAverageSeries =
        new Dictionary<int, MovingAverageSeries>();
    private IReadOnlyList<ChartTradeMarker> _tradeMarkers = Array.Empty<ChartTradeMarker>();
    private ChartCrosshairState _crosshair = ChartCrosshairState.Hidden;
    private Line? _mainCrosshairVertical;
    private Line? _subCrosshairVertical;
    private Line? _mainCrosshairHorizontal;
    private TextBlock? _crosshairPriceText;
    private bool _isDragging;
    private Point _dragOrigin;
    private ChartViewportState? _dragOriginViewport;
    private double _visiblePriceMin;
    private double _visiblePriceMax;
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

    public void UpdateTradeContext(
        IReadOnlyList<TradeLogRecord> tradeLogs,
        IReadOnlyList<StrategyConfigRecord> strategies)
    {
        _tradeLogs = tradeLogs?.ToArray() ?? Array.Empty<TradeLogRecord>();
        _strategies = strategies?.ToArray() ?? Array.Empty<StrategyConfigRecord>();
    }

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
        if (snapshot.Period != SecurityChartPeriod.Intraday)
        {
            _viewportStore.Reconcile(snapshot.Period, snapshot.KLines.Count);
            _movingAverageSeries = MovingAverageSeriesBuilder.BuildDefault(snapshot.KLines);
            _tradeMarkers = ChartTradeMarkerBuilder.Build(
                snapshot.Security,
                snapshot.Period,
                snapshot.KLines,
                _tradeLogs,
                _strategies);
        }
        else
        {
            _movingAverageSeries = new Dictionary<int, MovingAverageSeries>();
            _tradeMarkers = Array.Empty<ChartTradeMarker>();
        }

        _crosshair = ChartCrosshairState.Hidden;
        UpdateHeader(snapshot);
        UpdateButtonState();
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
        string depth = snapshot.Period != SecurityChartPeriod.Intraday && snapshot.HistoryDepth is { } historyDepth
            ? BuildHistoryDepthFooter(historyDepth)
            : string.Empty;
        return main + "；" + volume + previousClose + tail + depth;
    }

    private static string BuildHistoryDepthFooter(ChartHistoryDepthInfo depth)
    {
        string range = depth.EarliestDate.HasValue && depth.LatestDate.HasValue
            ? $"｜{depth.EarliestDate:yyyy-MM} 至 {depth.LatestDate:yyyy-MM}"
            : string.Empty;
        return $"；日K {depth.DailyCount}根｜周K {depth.WeeklyCount}根｜月K {depth.MonthlyCount}根{range}";
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
        if (Period != SecurityChartPeriod.Intraday)
        {
            EnsureCrosshairElements();
            UpdateMovingAverageLegend(snapshot, ResolveLegendGlobalIndex(snapshot));
            if (_crosshair.IsVisible)
            {
                UpdateCrosshairOverlay(snapshot);
            }
            else
            {
                CrosshairInfoBorder.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            MovingAverageLegendText.Text = string.Empty;
            CrosshairInfoBorder.Visibility = Visibility.Collapsed;
        }
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
        IntradayPoint? quoteCloseDisplay = points.LastOrDefault(point => point.IsQuoteCloseDisplayPoint
                                                                          && snapshot.Security.InstrumentType != ChartInstrumentType.Index);
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

        if (quoteCloseDisplay is not null)
        {
            Point quotePoint = new(
                XByIntradayPoint(plot, snapshot, quoteCloseDisplay.Time, points.Length - 1, points.Length),
                YByValue(plot, quoteCloseDisplay.Price, min, max));
            AddQuoteTailMarker(MainChartCanvas, quotePoint);
            AddText(MainChartCanvas, FormatNumber(quoteCloseDisplay.Price), Math.Min(plot.Right - 62, quotePoint.X + 6), quotePoint.Y - 18, 13, TextBrush);
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
        if (!TryResolveVisibleKLines(snapshot, out ChartVisibleRange range, out KLinePoint[] points))
        {
            DrawGrid(MainChartCanvas);
            AddCenteredText(MainChartCanvas, snapshot.MainStatus.Message);
            return;
        }

        DrawGrid(MainChartCanvas);
        double width = MainChartCanvas.ActualWidth;
        double height = MainChartCanvas.ActualHeight;
        Rect plot = PlotRect(width, height);
        double min = points.Min(point => point.Low);
        double max = points.Max(point => point.High);
        IncludeVisibleMovingAveragesInRange(range, ref min, ref max);
        ExpandRange(ref min, ref max);
        ReserveTradeMarkerSpace(range, ref min, ref max);
        _visiblePriceMin = min;
        _visiblePriceMax = max;
        double step = plot.Width / Math.Max(1, points.Length);
        double bodyWidth = Math.Clamp(step * 0.56, 2, 10);

        for (int i = 0; i < points.Length; i++)
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

        DrawMovingAverages(plot, range, min, max);
        DrawTradeMarkers(plot, range, points, min, max);
        KLinePoint latest = points[^1];
        AddText(MainChartCanvas, latest.Date.ToString("MM-dd", CultureInfo.InvariantCulture), plot.Right - 42, plot.Bottom + 5, 12, MutedBrush);
        AddText(MainChartCanvas, FormatNumber(latest.Close), plot.Right - 62, YByValue(plot, latest.Close, min, max) - 18, 13, TextBrush);
    }

    private bool TryResolveVisibleKLines(
        SecurityChartSnapshot snapshot,
        out ChartVisibleRange range,
        out KLinePoint[] points)
    {
        if (snapshot.Period == SecurityChartPeriod.Intraday || snapshot.KLines.Count == 0)
        {
            range = new ChartVisibleRange(0, 0);
            points = Array.Empty<KLinePoint>();
            return false;
        }

        ChartViewportState state = _viewportStore.Reconcile(snapshot.Period, snapshot.KLines.Count);
        range = ChartViewportCalculator.ResolveVisibleRange(state);
        points = snapshot.KLines
            .Skip(range.StartIndex)
            .Take(range.Count)
            .ToArray();
        return points.Length > 0;
    }

    private void IncludeVisibleMovingAveragesInRange(ChartVisibleRange range, ref double min, ref double max)
    {
        foreach ((int period, MovingAverageSeries series) in _movingAverageSeries)
        {
            if (!_movingAverageVisibility.GetValueOrDefault(period))
            {
                continue;
            }

            foreach (double? value in series.Values.Skip(range.StartIndex).Take(range.Count))
            {
                if (value is not { } current || !IsFinite(current))
                {
                    continue;
                }

                min = Math.Min(min, current);
                max = Math.Max(max, current);
            }
        }
    }

    private void ReserveTradeMarkerSpace(ChartVisibleRange range, ref double min, ref double max)
    {
        bool hasBuy = _tradeMarkers.Any(marker => marker.MarkerType == ChartTradeMarkerType.B
                                                  && marker.KLineIndex >= range.StartIndex
                                                  && marker.KLineIndex < range.EndExclusive);
        bool hasSell = _tradeMarkers.Any(marker => marker.MarkerType == ChartTradeMarkerType.S
                                                   && marker.KLineIndex >= range.StartIndex
                                                   && marker.KLineIndex < range.EndExclusive);
        double markerPadding = Math.Max(0.01, (max - min) * 0.045);
        if (hasBuy)
        {
            min -= markerPadding;
        }

        if (hasSell)
        {
            max += markerPadding;
        }
    }

    private void DrawMovingAverages(Rect plot, ChartVisibleRange range, double min, double max)
    {
        foreach (int period in MovingAverageSeriesBuilder.DefaultPeriods)
        {
            if (!_movingAverageVisibility.GetValueOrDefault(period)
                || !_movingAverageSeries.TryGetValue(period, out MovingAverageSeries? series))
            {
                continue;
            }

            Polyline? line = null;
            for (int visibleIndex = 0; visibleIndex < range.Count; visibleIndex++)
            {
                int globalIndex = range.StartIndex + visibleIndex;
                double? value = globalIndex < series.Values.Count ? series.Values[globalIndex] : null;
                if (value is not { } current || !IsFinite(current))
                {
                    line = null;
                    continue;
                }

                if (line is null)
                {
                    line = new Polyline
                    {
                        Stroke = MovingAverageBrush(period),
                        StrokeThickness = 1.25,
                        Opacity = 0.94
                    };
                    MainChartCanvas.Children.Add(line);
                }

                line.Points.Add(new Point(
                    XByBarCenter(plot, visibleIndex, range.Count),
                    YByValue(plot, current, min, max)));
            }
        }
    }

    private void DrawTradeMarkers(
        Rect plot,
        ChartVisibleRange range,
        IReadOnlyList<KLinePoint> visiblePoints,
        double min,
        double max)
    {
        foreach (ChartTradeMarker marker in _tradeMarkers.Where(marker =>
                     marker.KLineIndex >= range.StartIndex && marker.KLineIndex < range.EndExclusive))
        {
            int visibleIndex = marker.KLineIndex - range.StartIndex;
            KLinePoint point = visiblePoints[visibleIndex];
            double x = XByBarCenter(plot, visibleIndex, visiblePoints.Count);
            bool isBuy = marker.MarkerType == ChartTradeMarkerType.B;
            double referenceY = YByValue(plot, isBuy ? point.Low : point.High, min, max);
            double y = isBuy
                ? Math.Min(plot.Bottom - 17, referenceY + 3)
                : Math.Max(plot.Top + 1, referenceY - 19);
            var label = new TextBlock
            {
                Text = marker.MarkerType.ToString(),
                Foreground = isBuy ? RedBrush : GreenBrush,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, Math.Clamp(x - 5, plot.Left, plot.Right - 11));
            Canvas.SetTop(label, y);
            MainChartCanvas.Children.Add(label);
        }
    }

    private MacdPoint[] ResolveVisibleMacd(SecurityChartSnapshot snapshot)
    {
        if (!TryResolveVisibleKLines(snapshot, out _, out KLinePoint[] visible))
        {
            return Array.Empty<MacdPoint>();
        }

        var dates = visible.Select(point => point.Date).ToHashSet();
        return snapshot.Macd
            .Where(point => dates.Contains(point.Date))
            .OrderBy(point => point.Date)
            .ToArray();
    }

    private int ResolveLegendGlobalIndex(SecurityChartSnapshot snapshot)
    {
        if (!TryResolveVisibleKLines(snapshot, out ChartVisibleRange range, out _))
        {
            return -1;
        }

        int visibleIndex = _crosshair.IsVisible
            ? Math.Clamp(_crosshair.VisibleKLineIndex, 0, range.Count - 1)
            : range.Count - 1;
        return range.StartIndex + visibleIndex;
    }

    private void UpdateMovingAverageLegend(SecurityChartSnapshot snapshot, int globalIndex)
    {
        MovingAverageLegendText.Inlines.Clear();
        if (snapshot.Period == SecurityChartPeriod.Intraday || globalIndex < 0)
        {
            return;
        }

        foreach (int period in MovingAverageSeriesBuilder.DefaultPeriods)
        {
            double? value = _movingAverageSeries.TryGetValue(period, out MovingAverageSeries? series)
                            && globalIndex < series.Values.Count
                ? series.Values[globalIndex]
                : null;
            MovingAverageLegendText.Inlines.Add(new Run($"MA{period} {FormatPrice(value)}  ")
            {
                Foreground = MovingAverageBrush(period)
            });
        }
    }

    private static Brush MovingAverageBrush(int period)
        => period switch
        {
            5 => Ma5Brush,
            10 => Ma10Brush,
            20 => Ma20Brush,
            _ => Ma60Brush
        };

    private static double XByBarCenter(Rect plot, int visibleIndex, int visibleCount)
    {
        double step = plot.Width / Math.Max(1, visibleCount);
        return plot.Left + step * visibleIndex + step / 2;
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

        if (!TryResolveVisibleKLines(snapshot, out _, out KLinePoint[] points)
            || points.All(point => !point.Volume.HasValue))
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
            : ResolveVisibleMacd(snapshot);
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
        => snapshot.Security.InstrumentType == ChartInstrumentType.Index
            ? !point.IsQuoteTail
            : !point.IsQuoteCloseDisplayPoint;

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

    private void EnsureCrosshairElements()
    {
        _mainCrosshairVertical ??= CreateCrosshairLine();
        _subCrosshairVertical ??= CreateCrosshairLine();
        _mainCrosshairHorizontal ??= CreateCrosshairLine();
        _crosshairPriceText ??= new TextBlock
        {
            Foreground = TextBrush,
            Background = BrushFrom("#D9071724"),
            FontSize = 11,
            Padding = new Thickness(3, 1, 3, 1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        AddIfMissing(MainChartCanvas, _mainCrosshairVertical);
        AddIfMissing(MainChartCanvas, _mainCrosshairHorizontal);
        AddIfMissing(MainChartCanvas, _crosshairPriceText);
        AddIfMissing(SubChartCanvas, _subCrosshairVertical);
        SetCrosshairVisibility(_crosshair.IsVisible ? Visibility.Visible : Visibility.Collapsed);
    }

    private void UpdateCrosshairOverlay(SecurityChartSnapshot snapshot)
    {
        if (!_crosshair.IsVisible
            || !TryResolveVisibleKLines(snapshot, out ChartVisibleRange range, out KLinePoint[] visiblePoints))
        {
            HideCrosshair();
            return;
        }

        EnsureCrosshairElements();
        int visibleIndex = Math.Clamp(_crosshair.VisibleKLineIndex, 0, visiblePoints.Length - 1);
        int globalIndex = range.StartIndex + visibleIndex;
        Rect mainPlot = PlotRect(MainChartCanvas.ActualWidth, MainChartCanvas.ActualHeight);
        Rect subPlot = PlotRect(SubChartCanvas.ActualWidth, SubChartCanvas.ActualHeight);
        double mainX = XByBarCenter(mainPlot, visibleIndex, visiblePoints.Length);
        double subX = XByBarCenter(subPlot, visibleIndex, visiblePoints.Length);
        double mouseY = Math.Clamp(_crosshair.MouseY, mainPlot.Top, mainPlot.Bottom);

        SetLine(_mainCrosshairVertical!, mainX, mainPlot.Top, mainX, mainPlot.Bottom);
        SetLine(_subCrosshairVertical!, subX, subPlot.Top, subX, subPlot.Bottom);
        SetLine(_mainCrosshairHorizontal!, mainPlot.Left, mouseY, mainPlot.Right, mouseY);
        SetCrosshairVisibility(Visibility.Visible);

        double price = _visiblePriceMax - (mouseY - mainPlot.Top) / Math.Max(1, mainPlot.Height)
            * (_visiblePriceMax - _visiblePriceMin);
        _crosshairPriceText!.Text = FormatPrice(price);
        Canvas.SetLeft(_crosshairPriceText, Math.Max(mainPlot.Left, mainPlot.Right - 58));
        Canvas.SetTop(_crosshairPriceText, Math.Clamp(mouseY - 10, mainPlot.Top, mainPlot.Bottom - 20));

        KLinePoint point = visiblePoints[visibleIndex];
        double? previousClose = globalIndex > 0 ? snapshot.KLines[globalIndex - 1].Close : null;
        double? changePercent = previousClose is > 0
            ? (point.Close / previousClose.Value - 1) * 100
            : null;
        CrosshairInfoText.Text =
            $"{point.Date:yyyy-MM-dd}  开 {FormatPrice(point.Open)}  高 {FormatPrice(point.High)}  " +
            $"低 {FormatPrice(point.Low)}  收 {FormatPrice(point.Close)}  涨跌 {FormatSignedPercent(changePercent)}  " +
            $"成交量 {FormatQuantity(point.Volume)}\n" +
            string.Join("  ", MovingAverageSeriesBuilder.DefaultPeriods.Select(period =>
                $"MA{period} {FormatPrice(MovingAverageValue(period, globalIndex))}"));
        CrosshairInfoBorder.Visibility = Visibility.Visible;
        UpdateMovingAverageLegend(snapshot, globalIndex);
    }

    private double? MovingAverageValue(int period, int globalIndex)
        => _movingAverageSeries.TryGetValue(period, out MovingAverageSeries? series)
           && globalIndex >= 0
           && globalIndex < series.Values.Count
            ? series.Values[globalIndex]
            : null;

    private void HideCrosshair()
    {
        _crosshair = ChartCrosshairState.Hidden;
        SetCrosshairVisibility(Visibility.Collapsed);
        CrosshairInfoBorder.Visibility = Visibility.Collapsed;
        if (_snapshot is { Period: not SecurityChartPeriod.Intraday } snapshot)
        {
            UpdateMovingAverageLegend(snapshot, ResolveLegendGlobalIndex(snapshot));
        }
    }

    private void SetCrosshairVisibility(Visibility visibility)
    {
        if (_mainCrosshairVertical is not null)
        {
            _mainCrosshairVertical.Visibility = visibility;
        }

        if (_subCrosshairVertical is not null)
        {
            _subCrosshairVertical.Visibility = visibility;
        }

        if (_mainCrosshairHorizontal is not null)
        {
            _mainCrosshairHorizontal.Visibility = visibility;
        }

        if (_crosshairPriceText is not null)
        {
            _crosshairPriceText.Visibility = visibility;
        }
    }

    private static Line CreateCrosshairLine()
        => new()
        {
            Stroke = BrushFrom("#A8C7D9"),
            StrokeThickness = 0.9,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            Opacity = 0.82,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

    private static void AddIfMissing(Canvas canvas, UIElement element)
    {
        if (!canvas.Children.Contains(element))
        {
            canvas.Children.Add(element);
        }
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }

    private void MainChartCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!TryGetCurrentViewport(out ChartViewportState state))
        {
            return;
        }

        Rect plot = PlotRect(MainChartCanvas.ActualWidth, MainChartCanvas.ActualHeight);
        Point position = e.GetPosition(MainChartCanvas);
        double anchorRatio = plot.Width <= 0 ? 1 : (position.X - plot.Left) / plot.Width;
        _viewportStore.Set(Period, ChartViewportCalculator.ZoomAt(state, anchorRatio, e.Delta));
        HideCrosshair();
        DrawCharts();
        e.Handled = true;
    }

    private void MainChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetCurrentViewport(out ChartViewportState state))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ResetCurrentViewport();
            e.Handled = true;
            return;
        }

        _dragOrigin = e.GetPosition(MainChartCanvas);
        _dragOriginViewport = state;
        _isDragging = MainChartCanvas.CaptureMouse();
        if (_isDragging)
        {
            HideCrosshair();
            e.Handled = true;
        }
    }

    private void MainChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!TryGetCurrentViewport(out ChartViewportState state)
            || _snapshot is not { Period: not SecurityChartPeriod.Intraday } snapshot)
        {
            return;
        }

        Point position = e.GetPosition(MainChartCanvas);
        if (_isDragging && _dragOriginViewport is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }

            Rect plot = PlotRect(MainChartCanvas.ActualWidth, MainChartCanvas.ActualHeight);
            double pixelsPerBar = plot.Width / Math.Max(1, _dragOriginViewport.VisibleCount);
            int barsTowardHistory = pixelsPerBar <= 0
                ? 0
                : (int)Math.Round((position.X - _dragOrigin.X) / pixelsPerBar, MidpointRounding.AwayFromZero);
            ChartViewportState next = ChartViewportCalculator.Pan(_dragOriginViewport, barsTowardHistory);
            if (next.VisibleStartIndex != state.VisibleStartIndex)
            {
                _viewportStore.Set(Period, next);
                DrawCharts();
            }

            return;
        }

        Rect mainPlot = PlotRect(MainChartCanvas.ActualWidth, MainChartCanvas.ActualHeight);
        if (!mainPlot.Contains(position))
        {
            HideCrosshair();
            return;
        }

        double ratio = Math.Clamp((position.X - mainPlot.Left) / Math.Max(1, mainPlot.Width), 0, 0.999999);
        int visibleIndex = Math.Clamp((int)Math.Floor(ratio * state.VisibleCount), 0, state.VisibleCount - 1);
        _crosshair = new ChartCrosshairState(true, visibleIndex, position.X, position.Y);
        UpdateCrosshairOverlay(snapshot);
    }

    private void MainChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    private void MainChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        EndDrag();
        HideCrosshair();
    }

    private void MainChartCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
        _dragOriginViewport = null;
    }

    private void EndDrag()
    {
        bool captured = Mouse.Captured == MainChartCanvas;
        _isDragging = false;
        _dragOriginViewport = null;
        if (captured)
        {
            MainChartCanvas.ReleaseMouseCapture();
        }
    }

    private bool TryGetCurrentViewport(out ChartViewportState state)
    {
        if (Period == SecurityChartPeriod.Intraday
            || _snapshot is null
            || _snapshot.Period != Period
            || _snapshot.KLines.Count == 0)
        {
            state = new ChartViewportState(0, 0, 0, true);
            return false;
        }

        state = _viewportStore.Reconcile(Period, _snapshot.KLines.Count);
        return state.VisibleCount > 0;
    }

    private void ResetCurrentViewport()
    {
        if (_snapshot is null || Period == SecurityChartPeriod.Intraday)
        {
            return;
        }

        _viewportStore.Reset(Period, _snapshot.KLines.Count);
        HideCrosshair();
        DrawCharts();
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        => ResetCurrentViewport();

    private void MovingAverageButton_Click(object sender, RoutedEventArgs e)
    {
        int period = sender switch
        {
            Button button when button == Ma5Button => 5,
            Button button when button == Ma10Button => 10,
            Button button when button == Ma20Button => 20,
            _ => 60
        };
        _movingAverageVisibility[period] = !_movingAverageVisibility.GetValueOrDefault(period);
        UpdateButtonState();
        DrawCharts();
    }

    private void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        EndDrag();
        HideCrosshair();
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
        bool kLinePeriod = Period != SecurityChartPeriod.Intraday;
        Ma5Button.IsEnabled = kLinePeriod;
        Ma10Button.IsEnabled = kLinePeriod;
        Ma20Button.IsEnabled = kLinePeriod;
        Ma60Button.IsEnabled = kLinePeriod;
        ResetViewButton.IsEnabled = kLinePeriod;
        Ma5Button.Background = kLinePeriod && _movingAverageVisibility[5] ? SelectedButtonBrush : NormalButtonBrush;
        Ma10Button.Background = kLinePeriod && _movingAverageVisibility[10] ? SelectedButtonBrush : NormalButtonBrush;
        Ma20Button.Background = kLinePeriod && _movingAverageVisibility[20] ? SelectedButtonBrush : NormalButtonBrush;
        Ma60Button.Background = kLinePeriod && _movingAverageVisibility[60] ? SelectedButtonBrush : NormalButtonBrush;
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
        EndDrag();
        _isClosed = true;
        _tradeLogs = Array.Empty<TradeLogRecord>();
        _strategies = Array.Empty<StrategyConfigRecord>();
        base.OnClosed(e);
    }

    private static string FormatNumber(double? value)
        => value.HasValue ? value.Value.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') : "--";

    private static string FormatSignedPercent(double? value)
        => value.HasValue ? (value.Value > 0 ? "+" : string.Empty) + value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "--";

    private static string FormatPrice(double? value)
        => value is { } current && IsFinite(current)
            ? current.ToString("0.####", CultureInfo.InvariantCulture)
            : "--";

    private static string FormatQuantity(double? value)
        => value is { } current && IsFinite(current)
            ? current.ToString("0.####", CultureInfo.InvariantCulture)
            : "--";

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

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
