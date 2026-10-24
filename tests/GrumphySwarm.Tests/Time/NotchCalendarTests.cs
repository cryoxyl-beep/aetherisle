using System.Globalization;
using GrumphySwarm.Core.Time;

namespace GrumphySwarm.Tests.Time;

public class NotchCalendarTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Week_containing_a_saturday_starts_on_that_monday()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 24), EnUs); // Saturday

        Assert.Equal(7, model.Days.Count);
        Assert.Equal(19, model.Days[0].DayNumber); // Monday Oct 19
        Assert.Equal(25, model.Days[6].DayNumber); // Sunday Oct 25
    }

    [Fact]
    public void Today_is_flagged_exactly_once()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 24), EnUs);

        var todays = model.Days.Where(d => d.IsToday).ToList();
        var today = Assert.Single(todays);
        Assert.Equal(24, today.DayNumber);
        Assert.Equal(5, model.Days.ToList().IndexOf(today)); // Saturday in a Monday-first week
    }

    [Fact]
    public void Days_before_today_are_marked_past()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 24), EnUs);

        Assert.True(model.Days.Take(5).All(d => d.IsPast && !d.IsToday));
        Assert.True(model.Days.Skip(5).All(d => !d.IsPast));
    }

    [Fact]
    public void Labels_use_shortest_day_names()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 24), EnUs);

        Assert.Equal("M", model.Days[0].WeekdayLabel);
        Assert.Equal("S", model.Days[5].WeekdayLabel);
        Assert.Equal("S", model.Days[6].WeekdayLabel);
    }

    [Fact]
    public void Month_and_full_date_are_culture_formatted()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 24), EnUs);

        Assert.Equal("Oct", model.MonthLabel);
        Assert.Equal("Saturday, October 24", model.TodayFullDate);
    }

    [Fact]
    public void Midnight_of_a_monday_produces_a_full_week()
    {
        var model = NotchCalendar.Build(new DateTime(2026, 10, 19), EnUs); // Monday

        Assert.Equal(7, model.Days.Count);
        Assert.All(model.Days, day => Assert.False(day.IsPast));
        Assert.Equal(25, model.Days[6].DayNumber);
    }
}

public class NotchClockTests
{
    [Fact]
    public void Time_is_12h_with_ampm()
    {
        Assert.Equal("8:45 PM", NotchClock.FormatTime(new DateTime(2026, 10, 24, 20, 45, 0), CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("12:05 AM", NotchClock.FormatTime(new DateTime(2026, 10, 24, 0, 5, 0), CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("11:59 AM", NotchClock.FormatTime(new DateTime(2026, 10, 24, 11, 59, 0), CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Short_date_is_compact()
    {
        Assert.Equal("Sat, Oct 24", NotchClock.FormatShortDate(new DateTime(2026, 10, 24), CultureInfo.GetCultureInfo("en-US")));
    }
}
