using System.ComponentModel;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Trigger shown in the list; the text follows the editor's changes.</summary>
public class TriggerItem(ScheduleTrigger trigger) : INotifyPropertyChanged
{
    public ScheduleTrigger Trigger { get; } = trigger;
    public string Text => TriggerCalendar.Describe(Trigger) + (string.IsNullOrWhiteSpace(Trigger.Description) ? "" : $" — {Trigger.Description}");
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Text)));
    public event PropertyChangedEventHandler? PropertyChanged;
}
