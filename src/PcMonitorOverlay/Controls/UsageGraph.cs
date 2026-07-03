using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace PcMonitorOverlay.Controls;

public sealed class UsageGraph : FrameworkElement
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(MediaBrush),
            typeof(UsageGraph),
            new FrameworkPropertyMetadata(
                WpfBrushes.Cyan,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private const int MaxPoints = 60;
    private readonly Queue<double?> _values = new();

    public MediaBrush AccentBrush
    {
        get => (MediaBrush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public void AddValue(double? value)
    {
        _values.Enqueue(value.HasValue ? Math.Clamp(value.Value, 0, 100) : null);

        while (_values.Count > MaxPoints)
        {
            _values.Dequeue();
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawGrid(drawingContext, width, height);

        var points = BuildPoints(width, height);
        if (points.Count < 2)
        {
            DrawEmptyState(drawingContext, width, height);
            return;
        }

        DrawArea(drawingContext, points, height);
        DrawLine(drawingContext, points);
    }

    private void DrawGrid(DrawingContext drawingContext, double width, double height)
    {
        var gridPen = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(38, 255, 255, 255)), 1);
        gridPen.Freeze();

        for (var i = 1; i < 4; i++)
        {
            var y = height * i / 4d;
            drawingContext.DrawLine(gridPen, new WpfPoint(0, y), new WpfPoint(width, y));
        }

        var thresholdPen = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(80, 255, 116, 97)), 1);
        thresholdPen.Freeze();
        var thresholdY = height * 0.2d;
        drawingContext.DrawLine(thresholdPen, new WpfPoint(0, thresholdY), new WpfPoint(width, thresholdY));
    }

    private List<WpfPoint> BuildPoints(double width, double height)
    {
        var samples = _values.ToList();
        var points = new List<WpfPoint>(samples.Count);
        var denominator = Math.Max(MaxPoints - 1, 1);
        var startIndex = MaxPoints - samples.Count;

        for (var i = 0; i < samples.Count; i++)
        {
            if (!samples[i].HasValue)
            {
                continue;
            }

            var x = width * (startIndex + i) / denominator;
            var y = height - (height * samples[i]!.Value / 100d);
            points.Add(new WpfPoint(x, y));
        }

        return points;
    }

    private void DrawArea(DrawingContext drawingContext, IReadOnlyList<WpfPoint> points, double height)
    {
        var color = System.Windows.Media.Colors.Cyan;
        if (AccentBrush is SolidColorBrush solid)
        {
            color = solid.Color;
        }

        var fill = new SolidColorBrush(WpfColor.FromArgb(42, color.R, color.G, color.B));
        fill.Freeze();

        var figure = new PathFigure { StartPoint = new WpfPoint(points[0].X, height) };
        foreach (var point in points)
        {
            figure.Segments.Add(new LineSegment(point, true));
        }

        figure.Segments.Add(new LineSegment(new WpfPoint(points[^1].X, height), true));
        figure.IsClosed = true;

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        drawingContext.DrawGeometry(fill, null, geometry);
    }

    private void DrawLine(DrawingContext drawingContext, IReadOnlyList<WpfPoint> points)
    {
        var linePen = new WpfPen(AccentBrush, 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i], true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, linePen, geometry);
    }

    private static void DrawEmptyState(DrawingContext drawingContext, double width, double height)
    {
        var pen = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(80, 255, 255, 255)), 1);
        pen.Freeze();
        drawingContext.DrawLine(pen, new WpfPoint(0, height * 0.5), new WpfPoint(width, height * 0.5));
    }
}
