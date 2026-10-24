using System.Windows;
using System.Windows.Media;

namespace GrumphySwarm.App.Controls;

/// <summary>
/// Compact system battery indicator: outlined body + charge nub + fill bar.
/// Green while healthy, amber below 20%, red below 10% — mirroring the
/// reference pill's "Charging 75%" style. Shows an empty outline when the
/// machine has no battery (no value is invented).
/// </summary>
public sealed class SystemBatteryControl : FrameworkElement
{
    private static readonly Brush HealthyBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush LowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush BodyBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xF5, 0xF5, 0xF7));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(int?),
        typeof(SystemBatteryControl),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty IsChargingProperty = DependencyProperty.Register(
        nameof(IsCharging),
        typeof(bool),
        typeof(SystemBatteryControl),
        new PropertyMetadata(false, OnVisualChanged));

    public int? Percent
    {
        get => (int?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool IsCharging
    {
        get => (bool)GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SystemBatteryControl)d).InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bodyHeight = height;
        var nubWidth = Math.Max(1.6, height * 0.18);
        var bodyWidth = width - nubWidth - 1;
        var bodyRect = new Rect(0, 0, bodyWidth, bodyHeight);

        // Body outline.
        var outline = new Pen(BodyBrush, 1.3);
        drawingContext.DrawRoundedRectangle(null, outline, bodyRect, 3, 3);

        // Nub.
        drawingContext.DrawRoundedRectangle(
            BodyBrush,
            null,
            new Rect(bodyRect.Right + 1, height * 0.28, nubWidth, height * 0.44),
            1,
            1);

        // Charge fill.
        if (Percent is { } percent)
        {
            var fillBrush = percent < 10 ? LowBrush : percent < 20 ? AmberBrush : HealthyBrush;
            var inset = 2.2;
            var fillWidth = Math.Max(0, (bodyWidth - inset * 2) * Math.Clamp(percent, 0, 100) / 100.0);
            if (fillWidth > 0.5)
            {
                drawingContext.DrawRoundedRectangle(
                    fillBrush,
                    null,
                    new Rect(inset, inset, fillWidth, bodyHeight - inset * 2),
                    1.6,
                    1.6);
            }

            // Charging bolt overlay (subtle, drawn as a small polygon).
            if (IsCharging)
            {
                var cx = bodyRect.Left + bodyWidth / 2;
                var cy = height / 2;
                var bolt = new StreamGeometry();
                using (var context = bolt.Open())
                {
                    context.BeginFigure(new Point(cx + 1.2, cy - 4.4), true, true);
                    context.LineTo(new Point(cx - 2.2, cy + 0.5), true, false);
                    context.LineTo(new Point(cx - 0.2, cy + 0.5), true, false);
                    context.LineTo(new Point(cx - 1.2, cy + 4.4), true, false);
                    context.LineTo(new Point(cx + 2.2, cy - 0.5), true, false);
                    context.LineTo(new Point(cx + 0.2, cy - 0.5), true, false);
                }

                bolt.Freeze();
                drawingContext.DrawGeometry(Brushes.Black, null, bolt);
            }
        }
    }
}
