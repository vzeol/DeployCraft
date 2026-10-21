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
    public string Enabled => Schedule.Enabled ? "Oui" : "Non";
    public string Triggers => Schedule.Triggers.Count == 0 ? "(aucun : démarrage manuel)" : string.Join(" ; ", Schedule.Triggers.Select(TriggerCalendar.Describe));
    public DateTime? NextUtc => Schedule.NextRunUtc;
    public string Next => Schedule.Enabled ? Ui.LocalTime(NextUtc) is { Length: > 0 } t ? t : "—" : "Désactivée";

    /// <summary>Countdown to the next run (the service checks every 15 s).</summary>
    public string Countdown
    {
        get
        {
            if (!Schedule.Enabled || NextUtc is not { } next) return "";
            var left = next - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return "imminent";
            if (left.TotalMinutes < 1) return $"dans {left.Seconds} s";
            if (left.TotalHours < 1) return $"dans {(int)left.TotalMinutes} min {left.Seconds:00} s";
            if (left.TotalDays < 1) return $"dans {(int)left.TotalHours} h {left.Minutes:00} min";
            return $"dans {(int)left.TotalDays} j {left.Hours} h";
        }
    }
    public DateTime? LastUtc => Schedule.LastRunUtc;
    public string Last => Ui.LocalTime(LastUtc);
    public string Result => Schedule.LastResult ?? "";
}
