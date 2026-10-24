using System.Globalization;

namespace GrumphySwarm.Core.Time;

/// <summary>Clock text formatting for the pill and the expanded clock module.</summary>
public static class NotchClock
{
    /// <summary>Compact 24-hour time, e.g. "20:45" (culture-aware separators).</summary>
    public static string FormatTime(DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return now.ToString("HH:mm", culture);
    }

    /// <summary>Short weekday + date line for the collapsed pill tooltip, e.g. "Fri, Oct 24".</summary>
    public static string FormatShortDate(DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return now.ToString("ddd, MMM d", culture);
    }
}
