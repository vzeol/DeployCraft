using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedule window: triggers list and editor.</summary>
public partial class ScheduleWindow
{
    // ---------- Déclencheurs ----------

    private static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private void BuildEditorChoices()
    {
        foreach (var d in WeekOrder)
        {
            var box = new CheckBox { Content = TriggerCalendar.DayName(d), Tag = d, Margin = new Thickness(0, 0, 12, 6), MinWidth = 90 };
            box.Click += Editor_Changed;
            DaysPanel.Children.Add(box);
        }
        for (var i = 1; i <= 31; i++) MonthDayBox.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i });
        MonthDayBox.Items.Add(new ComboBoxItem { Content = "dernier jour", Tag = 0 });
        for (var i = 1; i <= 5; i++) MonthWeekBox.Items.Add(new ComboBoxItem { Content = TriggerCalendar.WeekNames[i - 1], Tag = i });
        foreach (var d in WeekOrder) MonthWeekDayBox.Items.Add(new ComboBoxItem { Content = TriggerCalendar.DayName(d), Tag = d });
    }

    private TriggerItem? Selected => TriggersList.SelectedItem as TriggerItem;

    private void AddTrigger(TriggerType type)
    {
        // Prochaine heure pile, comme point de départ.
        var item = new TriggerItem(ScheduleEditing.NewTrigger(type, DateTime.Now));
        _triggers.Add(item);
        TriggersList.SelectedItem = item;
    }

    private void AddOnce_Click(object sender, RoutedEventArgs e) => AddTrigger(TriggerType.Once);
    private void AddInterval_Click(object sender, RoutedEventArgs e) => AddTrigger(TriggerType.Interval);
    private void AddDaily_Click(object sender, RoutedEventArgs e) => AddTrigger(TriggerType.Daily);
    private void AddWeekly_Click(object sender, RoutedEventArgs e) => AddTrigger(TriggerType.Weekly);
    private void AddMonthly_Click(object sender, RoutedEventArgs e) => AddTrigger(TriggerType.Monthly);

    private void DuplicateTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var json = System.Text.Json.JsonSerializer.Serialize(item.Trigger, Api.Json);
        var copy = new TriggerItem(System.Text.Json.JsonSerializer.Deserialize<ScheduleTrigger>(json, Api.Json)!);
        _triggers.Add(copy);
        TriggersList.SelectedItem = copy;
    }

    private void DeleteTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var i = _triggers.IndexOf(item);
        _triggers.Remove(item);
        TriggersList.SelectedIndex = Math.Min(i, _triggers.Count - 1);
    }

    private void TriggersList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowEditor();

    /// <summary>Remplit l'éditeur avec le déclencheur sélectionné et n'affiche que ses réglages.</summary>
    private void ShowEditor()
    {
        NoTriggerText.Visibility = _triggers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteTriggerButton.IsEnabled = DuplicateTriggerButton.IsEnabled = Selected != null;
        Editor.Visibility = Selected == null ? Visibility.Hidden : Visibility.Visible;
        if (Selected is not { Trigger: var t }) return;

        _loading = true;
        EditorTitle.Text = ScheduleEditing.TypeText(t.Type);
        TrigEnabled.IsChecked = t.Enabled;
        IntervalPanel.Visibility = Show(t.Type == TriggerType.Interval);
        WeeklyPanel.Visibility = Show(t.Type == TriggerType.Weekly);
        MonthlyPanel.Visibility = Show(t.Type == TriggerType.Monthly);
        EndPanel.Visibility = Show(t.Type != TriggerType.Once);
        StartLabel.Text = t.Type == TriggerType.Once ? "Le" : "Début";

        IntDays.Text = (t.IntervalMinutes / 1440).ToString();
        IntHours.Text = (t.IntervalMinutes % 1440 / 60).ToString();
        IntMinutes.Text = (t.IntervalMinutes % 60).ToString();
        WindowBox.IsChecked = t.UseTimeWindow;
        WinFrom.Text = TriggerCalendar.Time(t.WindowStart);
        WinTo.Text = TriggerCalendar.Time(t.WindowEnd);
        foreach (CheckBox box in DaysPanel.Children) box.IsChecked = t.Days.Contains((DayOfWeek)box.Tag);
        MonthByWeek.IsChecked = t.MonthWeek != null;
        MonthByDay.IsChecked = t.MonthWeek == null;
        Pick(MonthDayBox, t.MonthDay);
        Pick(MonthWeekBox, t.MonthWeek ?? 1);
        Pick(MonthWeekDayBox, t.MonthWeekDay);
        StartDate.SelectedDate = t.Start.Date;
        StartTime.Text = TriggerCalendar.Time(t.Start.TimeOfDay);
        EndBox.IsChecked = t.End != null;
        var end = t.End ?? t.Start.Date.AddDays(7) + t.Start.TimeOfDay;
        EndDate.SelectedDate = end.Date;
        EndTime.Text = TriggerCalendar.Time(end.TimeOfDay);
        DescriptionBox.Text = t.Description ?? "";
        _loading = false;
        UpdateEditorState();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Pick(ComboBox box, object value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value)) ?? box.Items[0];

    private static T Chosen<T>(ComboBox box, T fallback) => box.SelectedItem is ComboBoxItem { Tag: T v } ? v : fallback;

    /// <summary>Reporte l'éditeur dans le déclencheur sélectionné (les valeurs mal saisies sont ignorées).</summary>
    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not { Trigger: var t } item) return;
        t.Enabled = TrigEnabled.IsChecked == true;
        var minutes = ScheduleEditing.ParseInt(IntDays.Text) * 1440 + ScheduleEditing.ParseInt(IntHours.Text) * 60 + ScheduleEditing.ParseInt(IntMinutes.Text);
        if (minutes > 0) t.IntervalMinutes = minutes;
        t.UseTimeWindow = WindowBox.IsChecked == true;
        if (ScheduleEditing.ParseTime(WinFrom.Text) is { } from) t.WindowStart = from;
        if (ScheduleEditing.ParseTime(WinTo.Text) is { } to) t.WindowEnd = to;
        t.Days = DaysPanel.Children.Cast<CheckBox>().Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag).ToList();
        t.MonthDay = Chosen(MonthDayBox, 1);
        t.MonthWeek = MonthByWeek.IsChecked == true ? Chosen(MonthWeekBox, 1) : null;
        t.MonthWeekDay = Chosen(MonthWeekDayBox, DayOfWeek.Monday);
        if (StartDate.SelectedDate is { } sd && ScheduleEditing.ParseTime(StartTime.Text) is { } st) t.Start = sd.Date + st;
        t.End = EndBox.IsChecked == true && EndDate.SelectedDate is { } ed && ScheduleEditing.ParseTime(EndTime.Text) is { } et ? ed.Date + et : null;
        t.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        item.Refresh();
        UpdateEditorState();
    }

    private void UpdateEditorState()
    {
        if (Selected is not { Trigger: var t }) return;
        WinFrom.IsEnabled = WinTo.IsEnabled = t.UseTimeWindow;
        EndDate.IsEnabled = EndTime.IsEnabled = EndBox.IsChecked == true;
        MonthDayBox.IsEnabled = MonthByDay.IsChecked == true;
        MonthWeekBox.IsEnabled = MonthWeekDayBox.IsEnabled = MonthByWeek.IsChecked == true;
        NextText.Text = ScheduleEditing.NextText(t, DateTime.Now);
    }
}
