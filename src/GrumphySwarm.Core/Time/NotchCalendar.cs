using System.Globalization;

namespace GrumphySwarm.Core.Time;

/// <summary>One day cell of the mini-calendar week strip.</summary>
/// <param name="WeekdayLabel">Single letter, e.g. "M" for Monday (culture-specific).</param>
/// <param name="DayNumber">Day of month (1..31).</param>
/// <param name="IsToday">True for the current day.</param>
/// <param name="IsPast">True for days earlier than today.</param>
public sealed record CalendarDay(string WeekdayLabel, int DayNumber, bool IsToday, bool IsPast);

/// <summary>View model for the notch calendar module (pure data, no WPF dependency).</summary>
/// <param name="MonthLabel">Abbreviated month name, e.g. "Oct".</param>
/// <param name="TodayFullDate">Human date line, e.g. "Friday, October 24".</param>
/// <param name="Days">The Monday-first week containing today, exactly 7 entries.</param>
public sealed record NotchCalendarModel(
    string MonthLabel,
    string TodayFullDate,
    IReadOnlyList<CalendarDay> Days);

/// <summary>
/// Builds the mini-calendar week strip around a given date. Pure and culture-
/// aware so it is trivially unit-testable and correct for any locale.
/// </summary>
public static class NotchCalendar
{
    public static NotchCalendarModel Build(DateTime date, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        var today = date.Date;

        // Monday of the week containing `today` (DayOfWeek.Sunday == 0).
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysFromMonday);

        var dayNames = culture.DateTimeFormat.ShortestDayNames; // index 0 = Sunday
        var days = new CalendarDay[7];
        for (var i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            var weekdayIndex = ((int)day.DayOfWeek + 6) % 7; // Monday-first index
            var label = dayNames[(int)day.DayOfWeek];
            days[i] = new CalendarDay(
                WeekdayLabel: label[..1].ToUpper(culture),
                DayNumber: day.Day,
                IsToday: day == today,
                IsPast: day < today);
        }

        var monthLabel = culture.DateTimeFormat.AbbreviatedMonthNames[today.Month - 1].TrimEnd('.');
        var fullDate = today.ToString("dddd, MMMM d", culture);

        return new NotchCalendarModel(monthLabel, fullDate, days);
    }
}
