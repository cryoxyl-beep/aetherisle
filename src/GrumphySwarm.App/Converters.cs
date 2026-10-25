using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GrumphySwarm.Core.Agents;
using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.App;

/// <summary>Glyph constants surfaced to XAML via x:Static.</summary>
public static class StatusGlyphs
{
    public static string EmptyMedia => StatusPresenters.MusicGlyph;

    public static string Previous => StatusPresenters.PreviousGlyph;

    public static string Next => StatusPresenters.NextGlyph;

    public static string Add => StatusPresenters.AddGlyph;
}

/// <summary>True → Collapsed, False/Null → Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>Maps an agent task-type id to its display name.</summary>
public sealed class TaskTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AgentTaskTypes.DisplayNameOf(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Playback fraction (0..1) → star GridLength for the filled progress segment.</summary>
public sealed class FractionToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = Math.Clamp(value as double? ?? 0, 0, 1);
        return new System.Windows.GridLength(Math.Max(0.0001, fraction), System.Windows.GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Playback fraction (0..1) → star GridLength for the remaining segment.</summary>
public sealed class FractionRemainderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = Math.Clamp(value as double? ?? 0, 0, 1);
        return new System.Windows.GridLength(Math.Max(0.0001, 1 - fraction), System.Windows.GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Live percent (double 0..100 or 0..1 fraction) → rounded int? for ring gauges.</summary>
public sealed class RoundedPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double number)
        {
            return null!;
        }

        // Values below 1 are treated as fractions (0..1 → 0..100).
        if (number is >= 0 and <= 1)
        {
            number *= 100;
        }

        return (int)Math.Clamp(Math.Round(number), 0, 100);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>App/process name → first letter, uppercased ("firefox" → "F").</summary>
public sealed class AppInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        return string.IsNullOrEmpty(name) ? "?" : char.ToUpper(name[0], culture).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible; null/empty → Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
