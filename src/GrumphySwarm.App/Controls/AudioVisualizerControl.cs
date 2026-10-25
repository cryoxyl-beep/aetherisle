using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GrumphySwarm.App.Services.Audio;

namespace GrumphySwarm.App.Controls;

/// <summary>Visualizer presentation styles.</summary>
public enum VisualizerMode
{
    /// <summary>Vertical bars along the bottom edge (expanded player).</summary>
    LinearBars,

    /// <summary>Lines radiating outward from a center circle (collapsed pill).</summary>
    RadialLines,
}

/// <summary>
/// Real-time audio visualizer fed by <see cref="AudioSpectrumMonitor"/> (WASAPI
/// loopback + FFT). Renders rounded bars (linear) or lines radiating from a
/// center circle (radial); values are smoothed with a fast attack / slow decay
/// so rapid changes read naturally. Refreshes at ~30 fps via a render-priority
/// dispatcher timer and skips work entirely while not visible.
/// </summary>
public sealed class AudioVisualizerControl : FrameworkElement
{
    public static readonly DependencyProperty MonitorProperty = DependencyProperty.Register(
        nameof(Monitor),
        typeof(AudioSpectrumMonitor),
        typeof(AudioVisualizerControl),
        new PropertyMetadata(null, OnMonitorChanged));

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount),
        typeof(int),
        typeof(AudioVisualizerControl),
        new PropertyMetadata(14, OnBarCountChanged));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush),
        typeof(Brush),
        typeof(AudioVisualizerControl),
        new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode),
        typeof(VisualizerMode),
        typeof(AudioVisualizerControl),
        new PropertyMetadata(VisualizerMode.LinearBars, OnModeChanged));

    public AudioSpectrumMonitor? Monitor
    {
        get => (AudioSpectrumMonitor?)GetValue(MonitorProperty);
        set => SetValue(MonitorProperty, value);
    }

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public VisualizerMode Mode
    {
        get => (VisualizerMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private readonly DispatcherTimer _timer;
    private double[] _display = new double[14];
    private double _lastRenderTicks;

    public AudioVisualizerControl()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => UpdateTimerState();
        UpdateTimerState();
    }

    private static void OnMonitorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AudioVisualizerControl)d).UpdateTimerState();

    private static void OnBarCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AudioVisualizerControl)d;
        control._display = new double[Math.Max(1, control.BarCount)];
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AudioVisualizerControl)d).InvalidateVisual();

    private void UpdateTimerState()
    {
        var shouldRun = IsVisible && Monitor is not null;
        if (shouldRun != _timer.IsEnabled)
        {
            if (shouldRun)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    /// <summary>Sub-samples the analyzer's bands into this control's bar range.</summary>
    private static double SampleBands(double[] bands, double from, double to)
    {
        var start = (int)Math.Clamp(from * bands.Length, 0, bands.Length - 1);
        var end = (int)Math.Clamp(to * bands.Length, start + 1, bands.Length);
        var max = 0.0;
        for (var i = start; i < end; i++)
        {
            max = Math.Max(max, bands[i]);
        }

        return max;
    }

    private void Refresh()
    {
        // Throttle against composition ticks that arrive faster than 30 fps.
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastRenderTicks < 30)
        {
            return;
        }

        _lastRenderTicks = nowTicks;

        var bands = Monitor?.LatestBands;
        var count = _display.Length;

        for (var i = 0; i < count; i++)
        {
            double target;
            if (bands is null || bands.Length == 0)
            {
                target = 0;
            }
            else
            {
                // Spread bars across the low 80% of the spectrum, where music lives.
                var from = 0.02 + 0.78 * i / count;
                var to = 0.02 + 0.78 * (i + 1) / count;

                // Sensitivity curve: real music bands sit low in the analyzer's
                // normalized range, so lift them — square-root-ish expansion plus
                // gain makes the visualizer visibly dance at normal volumes.
                target = Math.Clamp(Math.Pow(SampleBands(bands, from, to), 0.68) * 1.45, 0, 1);
            }

            // Fast attack, slow decay.
            _display[i] = target > _display[i]
                ? target
                : _display[i] * 0.82;
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

        if (Mode == VisualizerMode.RadialLines)
        {
            RenderRadial(drawingContext, width, height);
        }
        else
        {
            RenderLinear(drawingContext, width, height);
        }
    }

    private void RenderLinear(DrawingContext drawingContext, double width, double height)
    {
        var count = _display.Length;
        var gap = Math.Max(1.5, width * 0.06);
        var barWidth = (width - gap * (count - 1)) / count;
        var brush = BarBrush;

        for (var i = 0; i < count; i++)
        {
            var barHeight = Math.Max(2, _display[i] * (height - 2));
            var x = i * (barWidth + gap);
            var y = height - barHeight;
            var radius = Math.Min(barWidth / 2, 1.6);
            drawingContext.DrawRoundedRectangle(
                brush,
                null,
                new Rect(x, y, barWidth, barHeight),
                radius,
                radius);
        }
    }

    private void RenderRadial(DrawingContext drawingContext, double width, double height)
    {
        var count = _display.Length;
        var size = Math.Min(width, height);
        var center = new Point(width / 2, height / 2);
        var innerRadius = size * 0.26;
        var maxExtension = size * 0.22;
        var brush = BarBrush;

        // Subtle inner circle (opacity lives on the brush, Pen has none).
        var circleBrush = brush.Clone();
        circleBrush.Opacity = 0.35;
        circleBrush.Freeze();
        var circlePen = new Pen(circleBrush, 1.1);
        drawingContext.DrawEllipse(null, circlePen, center, innerRadius, innerRadius);

        // Radial lines evenly spread around the circle (Siri-style waves).
        var pen = new Pen(brush, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        for (var i = 0; i < count; i++)
        {
            var angle = (2 * Math.PI * i / count) - Math.PI / 2;
            var length = Math.Max(1.2, _display[i] * maxExtension);
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            drawingContext.DrawLine(
                pen,
                new Point(center.X + cos * innerRadius, center.Y + sin * innerRadius),
                new Point(center.X + cos * (innerRadius + length), center.Y + sin * (innerRadius + length)));
        }
    }
}
