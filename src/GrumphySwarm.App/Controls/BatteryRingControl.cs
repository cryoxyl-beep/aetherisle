using System.Windows;
using System.Windows.Media;

namespace GrumphySwarm.App.Controls;

/// <summary>
/// Circular battery gauge: a subtle track ring plus a green progress arc that
/// starts at 12 o'clock and sweeps clockwise. Renders nothing when the device
/// does not report a percentage (a gray full ring signals "no data" without
/// inventing one).
/// </summary>
public sealed class BatteryRingControl : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(int?),
        typeof(BatteryRingControl),
        new PropertyMetadata(null, OnPercentChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(BatteryRingControl),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E))));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(BatteryRingControl),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))));

    private static readonly PenTracker Tracker = new();

    public int? Percent
    {
        get => (int?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((BatteryRingControl)d).InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var stroke = Math.Max(2.5, size * 0.12);
        var radius = (size - stroke) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var trackPen = new Pen(TrackBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fillPen = new Pen(FillBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        // Track: full circle.
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        // Progress arc.
        if (Percent is { } percent)
        {
            Tracker.Geometry(radius, percent, out var geometry);
            drawingContext.DrawGeometry(null, fillPen, geometry);
        }
    }

    /// <summary>Caches the arc geometry per (radius, percent bucket) to avoid per-frame allocations.</summary>
    private sealed class PenTracker
    {
        private int _cachedPercent = -1;
        private double _cachedRadius;
        private readonly StreamGeometry _geometry = new();

        public void Geometry(double radius, int percent, out StreamGeometry geometry)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (_cachedPercent == percent && Math.Abs(_cachedRadius - radius) < 0.01)
            {
                geometry = _geometry;
                return;
            }

            var sweep = 360.0 * percent / 100.0;
            var startAngle = -90; // 12 o'clock
            var endAngle = startAngle + sweep;

            var startRadians = startAngle * Math.PI / 180;
            var endRadians = endAngle * Math.PI / 180;
            var largeArc = sweep > 180;

            var context = _geometry.Open();
            if (percent == 0)
            {
                // Degenerate: a zero-length line keeps the round cap visible.
                var point = new Point(radius, 0);
                context.BeginFigure(new Point(radius, 0), false, false);
                context.LineTo(new Point(radius + 0.01, 0), true, false);
            }
            else
            {
                var startPoint = new Point(radius * Math.Cos(startRadians), radius * Math.Sin(startRadians));
                var endPoint = new Point(radius * Math.Cos(endRadians), radius * Math.Sin(endRadians));
                context.BeginFigure(startPoint, false, false);
                context.ArcTo(
                    endPoint,
                    new Size(radius, radius),
                    0,
                    largeArc,
                    SweepDirection.Clockwise,
                    true,
                    false);
            }

            context.Close();
            _geometry.Freeze();

            _cachedPercent = percent;
            _cachedRadius = radius;
            geometry = _geometry;
        }
    }
}
