using System.Globalization;

namespace GrumphySwarm.Core.Time;

/// <summary>Clock text formatting for the pill and the expanded clock module.</summary>
public static class NotchClock
{
    /// <summary>Compact 12-hour time with AM/PM, e.g. "8:45 PM" (culture-aware).</summary>
    public static string FormatTime(DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return now.ToString("h:mm tt", culture);
    }

    /// <summary>Short weekday + date line for the collapsed pill tooltip, e.g. "Fri, Oct 24".</summary>
    public static string FormatShortDate(DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return now.ToString("ddd, MMM d", culture);
    }
}
