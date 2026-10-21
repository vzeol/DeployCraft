using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Rules and texts of the schedule window that do not depend on the window.</summary>
public static class ScheduleEditing
{
    /// <summary>New trigger of this type: next full hour (interval: now), this weekday, this day of the month.</summary>
    public static ScheduleTrigger NewTrigger(TriggerType type, DateTime now)
    {
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        var t = new ScheduleTrigger { Type = type, Start = start };
        if (type == TriggerType.Interval) { t.Start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0); t.IntervalMinutes = 60; }
        if (type == TriggerType.Weekly) t.Days = [start.DayOfWeek];
        if (type == TriggerType.Monthly) t.MonthDay = start.Day;
        return t;
    }

    public static string TypeText(TriggerType t) => t switch
    {
        TriggerType.Once => L.Get("ScheduleEditing_Once"),
        TriggerType.Interval => L.Get("ScheduleEditing_Interval"),
        TriggerType.Daily => L.Get("ScheduleEditing_Daily"),
        TriggerType.Weekly => L.Get("ScheduleEditing_Weekly"),
        _ => L.Get("ScheduleEditing_Monthly"),
    };

    public static string NextText(ScheduleTrigger t, DateTime now) =>
        !t.Enabled ? L.Get("ScheduleEditing_TriggerDisabled")
        : TriggerCalendar.Next(t, now) is { } n ? L.Format("ScheduleEditing_NextRun", n)
        : L.Get("ScheduleEditing_NoMoreRuns");

    /// <summary>Time typed as "8:30" or "8h30"; null when unreadable or not within a day.</summary>
    public static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParse(text.Trim().Replace('h', ':'), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    /// <summary>Positive number, or 0 when empty or unreadable.</summary>
    public static int ParseInt(string text) => int.TryParse(text.Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>First trigger the window refuses to save, with the reason (the service checks the rest).</summary>
    public static (int Index, string Message)? TriggerProblem(IReadOnlyList<ScheduleTrigger> triggers)
    {
        for (var i = 0; i < triggers.Count; i++)
        {
            var t = triggers[i];
            if (t.Type == TriggerType.Weekly && t.Days.Count == 0) return (i, L.Get("ScheduleEditing_WeeklyNeedsDay"));
            if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start) return (i, L.Get("ScheduleEditing_EndAfterStart"));
        }
        return null;
    }

    public static string HistorySummary(IReadOnlyCollection<HistoryRow> rows) =>
        rows.Count == 0 ? L.Get("ScheduleEditing_NoDeploymentDone") : L.Format("ScheduleEditing_HistorySummary", rows.Count(r => r.Succeeded), rows.Count);
}
