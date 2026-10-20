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
        TriggerType.Once => "Une fois",
        TriggerType.Interval => "Intervalle",
        TriggerType.Daily => "Quotidien",
        TriggerType.Weekly => "Hebdomadaire",
        _ => "Mensuel",
    };

    public static string NextText(ScheduleTrigger t, DateTime now) =>
        !t.Enabled ? "Déclencheur désactivé."
        : TriggerCalendar.Next(t, now) is { } n ? $"Prochain déclenchement : {n:dddd dd/MM/yyyy HH:mm}"
        : "Plus aucun déclenchement à venir.";

    /// <summary>Time typed as "8:30" or "8h30"; null when unreadable or not within a day.</summary>
    public static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParse(text.Trim().Replace('h', ':'), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    /// <summary>Positive number, or 0 when empty or unreadable.</summary>
    public static int ParseInt(string text) => int.TryParse(text.Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>Computer names typed or pasted: separated by spaces, commas, semicolons, tabs or lines; leading \\ removed.</summary>
    public static IEnumerable<string> ComputerNames(string text) =>
        text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(raw => raw.Trim().TrimStart('\\'))
            .Where(name => name.Length > 0);

    /// <summary>First trigger the window refuses to save, with the reason (the service checks the rest).</summary>
    public static (int Index, string Message)? TriggerProblem(IReadOnlyList<ScheduleTrigger> triggers)
    {
        for (var i = 0; i < triggers.Count; i++)
        {
            var t = triggers[i];
            if (t.Type == TriggerType.Weekly && t.Days.Count == 0) return (i, "Déclencheur hebdomadaire : cochez au moins un jour.");
            if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start) return (i, "La fin d'un déclencheur doit être après son début.");
        }
        return null;
    }

    /// <summary>Line of a package in the schedule: position, name and number of steps.</summary>
    public static string PackageLine(int index, Package p) =>
        $"{index + 1}. {p.Name} ({p.Steps.Count} étape{(p.Steps.Count > 1 ? "s" : "")})";

    /// <summary>"Package setting" choice: the common value of the packages, or "each package" when they differ.</summary>
    public static string PackageSetting<T>(IEnumerable<T> values, Func<T, string> text)
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count == 1 ? $"Paramètre du paquet ({text(distinct[0])})" : "Paramètre de chaque paquet";
    }

    public static string HistorySummary(IReadOnlyCollection<HistoryRow> rows) =>
        rows.Count == 0 ? "Aucun déploiement terminé pour l'instant." : $"{rows.Count(r => r.Succeeded)} réussi(s) sur {rows.Count} poste(s) × paquet";
}
