using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public class TriggerCalendarTests
{
    private static readonly DateTime Monday = new(2026, 10, 12, 9, 0, 0); // Monday 12/10/2026, 9 am

    [Fact]
    public void Once_FiresOnlyBeforeItsStart()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Once, Start = Monday.AddHours(1) };

        Assert.Equal(Monday.AddHours(1), TriggerCalendar.Next(t, Monday));
        Assert.Null(TriggerCalendar.Next(t, Monday.AddHours(1)));
    }

    [Fact]
    public void Interval_StaysOnTheGridFromItsStart()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Interval, Start = Monday, IntervalMinutes = 15 };

        Assert.Equal(Monday.AddMinutes(30), TriggerCalendar.Next(t, Monday.AddMinutes(20)));
        Assert.Equal(Monday.AddMinutes(30), TriggerCalendar.Next(t, Monday.AddMinutes(15)));
    }

    [Fact]
    public void Interval_WithTimeWindow_JumpsToNextWindowStart()
    {
        var t = new ScheduleTrigger
        {
            Type = TriggerType.Interval,
            Start = Monday,
            IntervalMinutes = 90,
            UseTimeWindow = true,
            WindowStart = new(8, 0, 0),
            WindowEnd = new(18, 0, 0),
        };

        // 1 h 30 grid started on Monday 9 am (... 6 pm, 7:30 pm ... 6 am, 7:30 am, 9 am): after 6:30 pm, the first
        // point in the 8 am – 6 pm range is Tuesday 9 am (8 am, start of the range, is not on the grid).
        Assert.Equal(new DateTime(2026, 10, 13, 9, 0, 0), TriggerCalendar.Next(t, Monday.Date.AddHours(18).AddMinutes(30)));
    }

    [Fact]
    public void NightWindow_CrossesMidnight()
    {
        Assert.True(TriggerCalendar.InWindow(new(23, 0, 0), new(22, 0, 0), new(6, 0, 0)));
        Assert.True(TriggerCalendar.InWindow(new(5, 0, 0), new(22, 0, 0), new(6, 0, 0)));
        Assert.False(TriggerCalendar.InWindow(new(12, 0, 0), new(22, 0, 0), new(6, 0, 0)));
    }

    [Fact]
    public void Weekly_PicksNextCheckedDay()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Weekly, Start = Monday, Days = [DayOfWeek.Wednesday, DayOfWeek.Friday] };

        Assert.Equal(new DateTime(2026, 10, 14, 9, 0, 0), TriggerCalendar.Next(t, Monday));
        Assert.Equal(new DateTime(2026, 10, 16, 9, 0, 0), TriggerCalendar.Next(t, new DateTime(2026, 10, 14, 9, 0, 0)));
    }

    [Fact]
    public void Monthly_LastFriday()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Monthly, Start = Monday, MonthWeek = 5, MonthWeekDay = DayOfWeek.Friday };

        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), TriggerCalendar.Next(t, Monday));
    }

    [Fact]
    public void Monthly_Day31_FallsBackToLastDayOfShortMonth()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Monthly, Start = Monday, MonthDay = 31 };

        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0), TriggerCalendar.Next(t, new DateTime(2026, 11, 1)));
    }

    [Fact]
    public void EndDate_StopsTheTrigger()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Daily, Start = Monday, End = Monday.AddDays(1).AddHours(-1) };

        Assert.Null(TriggerCalendar.Next(t, Monday));
    }

    [Fact]
    public void DisabledTrigger_NeverFires()
    {
        var t = new ScheduleTrigger { Type = TriggerType.Daily, Start = Monday, Enabled = false };

        Assert.Null(TriggerCalendar.Next(t, Monday.AddDays(-1)));
    }
}
