using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Line of a schedule list, with its countdown.</summary>
public record ScheduleRow(Schedule Schedule) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Advances the displayed countdown.</summary>
    public void Tick() => PropertyChanged?.Invoke(this, new(nameof(Countdown)));

    public long Id => Schedule.Id;
    public string Name => Schedule.Name;
    public string Enabled => Schedule.Enabled ? L.Get("ScheduleRow_Yes") : L.Get("ScheduleRow_No");
    public string Triggers => Schedule.Triggers.Count == 0 ? L.Get("ScheduleRow_NoTriggerManual") : string.Join(" ; ", Schedule.Triggers.Select(TriggerTexts.Describe));
    public DateTime? NextUtc => Schedule.NextRunUtc;
    public string Next => Schedule.Enabled ? Ui.LocalTime(NextUtc) is { Length: > 0 } t ? t : "—" : L.Get("ScheduleRow_Disabled");

    /// <summary>Countdown to the next run (the service checks every 15 s).</summary>
    public string Countdown
    {
        get
        {
            if (!Schedule.Enabled || NextUtc is not { } next) return "";
            var left = next - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return L.Get("ScheduleRow_Imminent");
            if (left.TotalMinutes < 1) return L.Format("ScheduleRow_InSeconds", left.Seconds);
            if (left.TotalHours < 1) return L.Format("ScheduleRow_InMinutes", (int)left.TotalMinutes, left.Seconds);
            if (left.TotalDays < 1) return L.Format("ScheduleRow_InHours", (int)left.TotalHours, left.Minutes);
            return L.Format("ScheduleRow_InDays", (int)left.TotalDays, left.Hours);
        }
    }
    public DateTime? LastUtc => Schedule.LastRunUtc;
    public string Last => Ui.LocalTime(LastUtc);
    public string Result => Schedule.LastResult ?? "";
}
