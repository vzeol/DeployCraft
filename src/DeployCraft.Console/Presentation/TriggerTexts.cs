using System.Globalization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Texts describing a schedule trigger, in the console language (list of triggers, schedule rows).</summary>
public static class TriggerTexts
{
    public static string Describe(ScheduleTrigger t)
    {
        var at = TriggerCalendar.Time(t.Start.TimeOfDay);
        var text = t.Type switch
        {
            TriggerType.Once => L.Format("Trigger_Once", t.Start, at),
            TriggerType.Interval => L.Format("Trigger_Interval", Interval(t.IntervalMinutes), t.Start, at)
                                    + (t.UseTimeWindow ? L.Format("Trigger_Window", TriggerCalendar.Time(t.WindowStart), TriggerCalendar.Time(t.WindowEnd)) : ""),
            TriggerType.Daily => L.Format("Trigger_Daily", at, t.Start),
            TriggerType.Weekly => L.Format("Trigger_Weekly", DaysText(t.Days), at, t.Start),
            _ => L.Format("Trigger_Monthly", MonthText(t), at, t.Start),
        };
        if (t.Type != TriggerType.Once && t.End is { } end) text += L.Format("Trigger_Until", end, TriggerCalendar.Time(end.TimeOfDay));
        if (!t.Enabled) text += L.Get("Trigger_Disabled");
        return text;
    }

    public static string Interval(int minutes)
    {
        var parts = new List<string>();
        if (minutes / 1440 > 0) parts.Add(L.Format("Trigger_Days", minutes / 1440));
        if (minutes % 1440 / 60 > 0) parts.Add(L.Format("Trigger_Hours", minutes % 1440 / 60));
        if (minutes % 60 > 0 || parts.Count == 0) parts.Add(L.Format("Trigger_Minutes", minutes % 60));
        return string.Join(" ", parts);
    }

    /// <summary>Day name in the console language ("lundi", "Monday").</summary>
    public static string DayName(DayOfWeek d) => CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(d);

    /// <summary>Week of the month, 1 to 5 (5 = last).</summary>
    public static string WeekName(int week) => L.Get(WeekKeys[Math.Clamp(week, 1, 5) - 1]);

    private static readonly string[] WeekKeys = ["Trigger_Week1", "Trigger_Week2", "Trigger_Week3", "Trigger_Week4", "Trigger_Week5"];

    private static string DaysText(List<DayOfWeek> days) => days.Count == 0
        ? L.Get("Trigger_NoDay")
        : string.Join(", ", days.OrderBy(d => ((int)d + 6) % 7).Select(DayName));

    private static string MonthText(ScheduleTrigger t) => t.MonthWeek is { } w
        ? L.Format("Trigger_MonthWeekDay", WeekName(w), DayName(t.MonthWeekDay))
        : t.MonthDay <= 0 ? L.Get("Trigger_MonthLastDay") : L.Format("Trigger_MonthDay", t.MonthDay);
}
