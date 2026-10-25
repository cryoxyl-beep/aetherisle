using IoDirectory = System.IO.Directory;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.App.Controls;

/// <summary>
/// Animated weather icon playing the official Meteocons Lottie animations
/// (basmilius/meteocons, MIT) via LottieSharp — pure WPF rendering, safe in
/// the island's transparent window. The Lottie JSONs are embedded resources,
/// extracted once to %LOCALAPPDATA%\GrumphySwarm\weather-icons for the player.
/// If the Lottie player is unavailable on this machine, the control falls
/// back to a small native vector animation so the card never goes blank.
/// </summary>
public sealed class WeatherIconControl : Grid
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(WeatherIconKind),
        typeof(WeatherIconControl),
        new PropertyMetadata(WeatherIconKind.Unknown, OnKindChanged));

    // Meteocons Lottie slugs per icon kind.
    private static string SlugFor(WeatherIconKind kind) => kind switch
    {
        WeatherIconKind.Clear => "clear-day",
        WeatherIconKind.ClearNight => "clear-night",
        WeatherIconKind.PartlyCloudy => "partly-cloudy-day",
        WeatherIconKind.Cloudy => "cloudy",
        WeatherIconKind.Fog => "fog-day",
        WeatherIconKind.Drizzle => "drizzle",
        WeatherIconKind.Rain => "rain",
        WeatherIconKind.Snow => "snow",
        WeatherIconKind.Thunder => "thunderstorms",
        _ => "cloudy",
    };

    private static readonly string IconCacheDir = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GrumphySwarm",
        "weather-icons");

    // Vector-fallback palette (amCharts geometry).
    private static readonly SolidColorBrush SunOrange = Freeze(Color.FromRgb(0xFF, 0xA5, 0x00));
    private static readonly SolidColorBrush CloudLight = Freeze(Color.FromRgb(0x91, 0xC0, 0xF8));
    private static readonly SolidColorBrush CloudDark = Freeze(Color.FromRgb(0x57, 0xA0, 0xEE));
    private static readonly SolidColorBrush CloudStroke = Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush SnowWhite = Freeze(Color.FromRgb(0xE6, 0xEE, 0xF8));

    // The amCharts cloud silhouette (fallback rendering only).
    private const string CloudPath =
        "M47.7,35.4 C47.7,30.8 44,27.2 39.5,27.2 C38.5,27.2 37.6,27.4 36.7,27.7 "
        + "C36.4,24.3 33.6,21.5 30.1,21.5 C26.4,21.5 23.4,24.5 23.4,28.2 "
        + "C23.4,29 23.6,29.8 23.8,30.5 C23.5,30.4 23.1,30.4 22.8,30.4 "
        + "C19.1,30.4 16.1,33.4 16.1,37.1 C16.1,40.7 19,43.7 22.6,43.8 L39.8,43.8 "
        + "C44.2,43.3 47.7,39.8 47.7,35.4 Z";

    // amCharts crescent moon (fallback rendering only).
    private const string MoonPath =
        "M14.5,13.2 C14.5,9.5 16.5,6.3 19.5,4.5 C18,3.6 16.3,3.2 14.5,3.2 "
        + "C9,3.2 4.5,7.7 4.5,13.2 C4.5,18.7 9,23.2 14.5,23.2 "
        + "C16.3,23.2 18,22.7 19.5,21.9 C16.5,20.2 14.5,16.9 14.5,13.2 Z";

    public WeatherIconKind Kind
    {
        get => (WeatherIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public WeatherIconControl()
    {
        Loaded += (_, _) => Rebuild();
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WeatherIconControl)d).Rebuild();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Rebuild()
    {
        Children.Clear();

        if (!IsLoaded || Kind == WeatherIconKind.Unknown)
        {
            return;
        }

        if (TryBuildLottie(out var view))
        {
            Children.Add(view);
            return;
        }

        var canvas = new Canvas { Width = 64, Height = 64 };
        Build(canvas);
        Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
    }

    /// <summary>Extracts the embedded Meteocons JSON and plays it. Never throws.</summary>
    private bool TryBuildLottie(out FrameworkElement? view)
    {
        view = null;

        try
        {
            var slug = SlugFor(Kind);
            var fileName = ExtractIcon(slug);
            if (fileName is null)
            {
                return false;
            }

            var lottie = new LottieSharp.WPF.LottieAnimationView
            {
                FileName = fileName,
                AutoPlay = true,
                Repeat = LottieSharp.WPF.RepeatMode.Restart,
                RepeatCount = int.MaxValue,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            lottie.PlayAnimation();
            view = lottie;
            return true;
        }
        catch
        {
            // Player unavailable on this machine — vector fallback renders.
            return false;
        }
    }

    /// <summary>Writes an embedded Lottie JSON to the cache dir; returns its path.</summary>
    private static string? ExtractIcon(string slug)
    {
        try
        {
            IoDirectory.CreateDirectory(IconCacheDir);
            var target = IoPath.Combine(IconCacheDir, slug + ".json");
            if (!IoFile.Exists(target))
            {
                using var stream = System.Windows.Application.GetResourceStream(
                    new Uri($"pack://application:,,,/Resources/weather/{slug}.json")).Stream;
                using var output = IoFile.Create(target);
                stream.CopyTo(output);
            }

            return target;
        }
        catch
        {
            return null;
        }
    }

    private void Build(Canvas canvas)
    {
        switch (Kind)
        {
            case WeatherIconKind.Clear:
                AddSun(canvas, 32, 32, 1);
                break;
            case WeatherIconKind.ClearNight:
                AddMoon(canvas);
                break;
            case WeatherIconKind.PartlyCloudy:
                AddSun(canvas, 20, 24, 0.7);
                AddCloud(canvas, front: true);
                break;
            case WeatherIconKind.Cloudy:
                AddCloud(canvas, front: false, back: true);
                break;
            case WeatherIconKind.Fog:
                AddCloud(canvas, front: true, lifted: true);
                AddFogLines(canvas);
                break;
            case WeatherIconKind.Drizzle:
                AddCloud(canvas, front: true);
                AddRain(canvas, drops: 2, width: 2.5, durationMs: 1100);
                break;
            case WeatherIconKind.Rain:
                AddCloud(canvas, front: true);
                AddRain(canvas, drops: 3, width: 3, durationMs: 850);
                break;
            case WeatherIconKind.Snow:
                AddCloud(canvas, front: true);
                AddSnow(canvas);
                break;
            case WeatherIconKind.Thunder:
                AddCloud(canvas, front: false, back: true);
                AddBolt(canvas);
                break;
        }
    }

    /// <summary>Sun disc + eight rays; the ray group spins slowly (amCharts day icon).</summary>
    private static void AddSun(Canvas canvas, double centerX, double centerY, double scale)
    {
        var sun = new Canvas();

        var rays = new Canvas();
        for (var angle = 0; angle < 360; angle += 45)
        {
            var ray = new Line
            {
                X1 = 0,
                Y1 = 9,
                X2 = 0,
                Y2 = 12,
                StrokeThickness = 2,
                Stroke = SunOrange,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(angle),
            };
            rays.Children.Add(ray);
        }

        var spin = new RotateTransform();
        rays.RenderTransform = spin;
        spin.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(12))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });

        var disc = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = SunOrange,
            Stroke = SunOrange,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(disc, -6);
        Canvas.SetTop(disc, -6);

        sun.Children.Add(rays);
        sun.Children.Add(disc);

        var group = new TransformGroup();
        if (scale != 1)
        {
            group.Children.Add(new ScaleTransform(scale, scale));
        }

        group.Children.Add(new TranslateTransform(centerX, centerY));
        sun.RenderTransform = group;
        canvas.Children.Add(sun);
    }

    private static void AddMoon(Canvas canvas)
    {
        var moon = new Path
        {
            Data = Geometry.Parse(MoonPath),
            Fill = SunOrange,
            Stroke = SunOrange,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(moon, 22);
        Canvas.SetTop(moon, 16);
        moon.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.75, 1, TimeSpan.FromSeconds(2.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        canvas.Children.Add(moon);
    }

    /// <summary>
    /// The amCharts cloud. The front cloud drifts left-right (translate
    /// -5→10→-5 over 7 s); an optional smaller back cloud sits behind it.
    /// </summary>
    private static void AddCloud(Canvas canvas, bool front, bool back = false, bool lifted = false)
    {
        if (back)
        {
            var backCloud = new Path
            {
                Data = Geometry.Parse(CloudPath),
                Fill = CloudLight,
                Stroke = CloudStroke,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                RenderTransform = new TransformGroup
                {
                    // Matches amCharts "translate(-10,-6), scale(0.6)".
                    Children = { new TranslateTransform(-10, -6), new ScaleTransform(0.6, 0.6) },
                },
            };
            canvas.Children.Add(backCloud);
        }

        var drift = new TranslateTransform();
        var cloud = new Path
        {
            Data = Geometry.Parse(CloudPath),
            Fill = CloudDark,
            Stroke = CloudStroke,
            StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = drift,
        };
        Canvas.SetLeft(cloud, 0);
        Canvas.SetTop(cloud, lifted ? 4 : 8);

        drift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-5, 10, TimeSpan.FromSeconds(7))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });

        canvas.Children.Add(cloud);
    }

    /// <summary>Falling rain lines: translate down while fading, staggered.</summary>
    private static void AddRain(Canvas canvas, int drops, double width, int durationMs)
    {
        for (var i = 0; i < drops; i++)
        {
            var fall = new TranslateTransform();
            var drop = new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = 0,
                Y2 = 8,
                StrokeThickness = width,
                Stroke = CloudLight,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = fall,
            };
            Canvas.SetLeft(drop, 16 + (i * 7));
            Canvas.SetTop(drop, 44);

            var begin = TimeSpan.FromMilliseconds(i * (durationMs / 2.2));
            fall.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, 9, TimeSpan.FromMilliseconds(durationMs))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = begin,
                });
            drop.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(durationMs))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = begin,
                });

            canvas.Children.Add(drop);
        }
    }

    /// <summary>Falling snow dots.</summary>
    private static void AddSnow(Canvas canvas)
    {
        for (var i = 0; i < 3; i++)
        {
            var fall = new TranslateTransform();
            var flake = new Ellipse
            {
                Width = 3.2,
                Height = 3.2,
                Fill = SnowWhite,
                RenderTransform = fall,
            };
            Canvas.SetLeft(flake, 17 + (i * 7));
            Canvas.SetTop(flake, 45);

            var begin = TimeSpan.FromMilliseconds(i * 420);
            fall.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, 9, TimeSpan.FromSeconds(1.6))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = begin,
                });
            flake.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(1.6))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = begin,
                });

            canvas.Children.Add(flake);
        }
    }

    /// <summary>Thunderbolt flash (amCharts thunder icon).</summary>
    private static void AddBolt(Canvas canvas)
    {
        var bolt = new Polygon
        {
            Points = new PointCollection
            {
                new(14.3, -2.9), new(20.5, -2.9), new(16.4, 4.3),
                new(20.3, 4.3), new(11.5, 14.6), new(14.9, 6.9), new(11.1, 6.9),
            },
            Fill = SunOrange,
            Stroke = CloudStroke,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(bolt, 14);
        Canvas.SetTop(bolt, 38);

        bolt.BeginAnimation(
            OpacityProperty,
            new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1.8),
                RepeatBehavior = RepeatBehavior.Forever,
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(0, TimeSpan.Zero),
                    new DiscreteDoubleKeyFrame(1, TimeSpan.FromSeconds(0.15)),
                    new DiscreteDoubleKeyFrame(1, TimeSpan.FromSeconds(0.5)),
                    new DiscreteDoubleKeyFrame(0, TimeSpan.FromSeconds(0.65)),
                    new DiscreteDoubleKeyFrame(1, TimeSpan.FromSeconds(0.8)),
                    new DiscreteDoubleKeyFrame(0, TimeSpan.FromSeconds(1.05)),
                },
            });

        canvas.Children.Add(bolt);
    }

    /// <summary>Two fog lines drifting in opposite directions beneath the cloud.</summary>
    private static void AddFogLines(Canvas canvas)
    {
        for (var i = 0; i < 2; i++)
        {
            var drift = new TranslateTransform();
            var line = new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = 24,
                Y2 = 0,
                StrokeThickness = 2.4,
                Stroke = i == 0 ? CloudLight : SnowWhite,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = drift,
            };
            Canvas.SetLeft(line, 14);
            Canvas.SetTop(line, 48 + (i * 6));

            drift.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(i == 0 ? 4 : 5))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                });

            canvas.Children.Add(line);
        }
    }
}
