using System.Globalization;
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

    private readonly PenTracker Tracker = new();

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

        // Progress arc + the percentage written INSIDE the ring.
        if (Percent is { } percent)
        {
            if (percent >= 99.5)
            {
                // A 360° arc is degenerate (start == end renders nothing), so a
                // full battery draws the complete circle directly.
                drawingContext.DrawEllipse(null, fillPen, center, radius, radius);
            }
            else
            {
                Tracker.Geometry(radius, percent, out var geometry);
                drawingContext.PushTransform(new TranslateTransform(center.X, center.Y));
                drawingContext.DrawGeometry(null, fillPen, geometry);
                drawingContext.Pop();
            }

            DrawPercentText(drawingContext, center, radius, percent);
        }
    }

    /// <summary>Big number + smaller % centered inside the ring (reference style).</summary>
    private void DrawPercentText(DrawingContext drawingContext, Point center, double innerRadius, int percent)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);

        var numberSize = Math.Max(7.5, innerRadius * 0.78);
        var percentSize = numberSize * 0.52;

        var number = new FormattedText(
            percent.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            numberSize,
            Brushes.White,
            pixelsPerDip);

        var sign = new FormattedText(
            "%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            percentSize,
            Brushes.White,
            pixelsPerDip);

        var totalWidth = number.Width + sign.Width + 1;
        var startX = center.X - (totalWidth / 2);

        // Number baseline-centered; % tucked after it, slightly raised.
        drawingContext.DrawText(number, new Point(startX, center.Y - (number.Height / 2)));
        drawingContext.DrawText(sign, new Point(startX + number.Width + 1, center.Y - (number.Height / 2) + (number.Height - sign.Height) * 0.55));
    }

    /// <summary>
    /// Caches the arc geometry per (radius, percent). The geometry is REBUILT
    /// as a new frozen instance on change — a frozen StreamGeometry can never
    /// be reopened, and mutating a shared one made the whole island's render
    /// pass throw (the "island randomly disappears" bug).
    /// </summary>
    private sealed class PenTracker
    {
        private int _cachedPercent = -1;
        private double _cachedRadius;
        private StreamGeometry? _geometry;

        public void Geometry(double radius, int percent, out StreamGeometry geometry)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (_geometry is not null && _cachedPercent == percent && Math.Abs(_cachedRadius - radius) < 0.01)
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

            var built = new StreamGeometry();
            using (var context = built.Open())
            {
                if (percent == 0)
                {
                    // Degenerate: a zero-length line keeps the round cap visible.
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
            }

            built.Freeze();

            _geometry = built;
            _cachedPercent = percent;
            _cachedRadius = radius;
            geometry = built;
        }
    }
}
