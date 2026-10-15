using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Déclencheur affiché dans la liste ; le texte suit les modifications de l'éditeur.</summary>
public class TriggerItem(ScheduleTrigger trigger) : INotifyPropertyChanged
{
    public ScheduleTrigger Trigger { get; } = trigger;
    public string Text => TriggerCalendar.Describe(Trigger) + (string.IsNullOrWhiteSpace(Trigger.Description) ? "" : $" — {Trigger.Description}");
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Text)));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public record HistoryRow(string Computer, long PackageId, string PackageName, bool Succeeded, int Failures, DateTime? LastUtc)
{
    public string SuccessIcon => Succeeded ? Ui.StatusIcon("Successful") : "";
    public string Last => Ui.LocalTime(LastUtc);
}

/// <summary>
/// Fenêtre d'une planification, à onglets : déclencheurs, cibles, paquets, options,
/// historique des cibles (réussite et nombre d'échecs par poste et par paquet).
/// </summary>
public partial class ScheduleWindow : Window
{
    private readonly Schedule _schedule;
    private readonly ObservableCollection<TriggerItem> _triggers = [];
    private readonly ObservableCollection<string> _targets = [];
    private readonly ObservableCollection<PackageItem> _packages = [];
    private List<Package> _library = [];
    /// <summary>Préférences : valeur des paquets laissés « par défaut ».</summary>
    private DeploymentDefaults _defaults = new();
    private bool _loading;

    /// <summary>Planification enregistrée (après OK).</summary>
    public Schedule? Saved { get; private set; }

    public ScheduleWindow(Schedule schedule)
    {
        InitializeComponent();
        _schedule = schedule;
        TriggersList.ItemsSource = _triggers;
        TargetsList.ItemsSource = _targets;
        PackagesList.ItemsSource = _packages;

        Title = schedule.Id == 0 ? "Nouvelle planification" : $"Planification n° {schedule.Id}";
        NameBox.Text = schedule.Name;
        foreach (var t in schedule.Triggers) _triggers.Add(new TriggerItem(t));
        foreach (var c in schedule.Targets) _targets.Add(c);
        EnabledBox.IsChecked = schedule.Enabled;
        StopOnSuccessBox.IsChecked = schedule.StopOnSuccess;
        StopFailBox.IsChecked = schedule.StopAfterFailures != null;
        FailCountBox.Text = (schedule.StopAfterFailures ?? 3).ToString();
        QueueBox.IsChecked = schedule.QueueTimeoutMinutes != null;
        QueueMinutesBox.Text = (schedule.QueueTimeoutMinutes ?? 120).ToString();
        Options_Click(this, new());

        BuildEditorChoices();
        TriggersList.SelectedIndex = _triggers.Count > 0 ? 0 : -1;
        ShowEditor();
        UpdateTargets();

        Ui.SelectOnRightClick(TargetsList);
        Ui.ContextMenu(TargetsList, () =>
        [
            new("Coller une liste de postes", PasteNames, Clipboard.ContainsText(), "Ctrl+V"),
            new("Active Directory…", () => Ad_Click(this, new())),
            new("Fichier texte…", () => TextFile_Click(this, new())),
            Ui.Separator,
            new("Retirer", () => Remove_Click(this, new()), TargetsList.SelectedItems.Count > 0, "Suppr"),
            new("Tout retirer", () => Clear_Click(this, new()), _targets.Count > 0),
        ]);
        Ui.SelectOnRightClick(HistoryGrid);
        Ui.ContextMenu(HistoryGrid, () =>
        [
            new("Effacer (redéployer ce paquet sur ce poste)", () => ClearHistory_Click(this, new()), HistoryGrid.SelectedItems.Count > 0),
            new("Tout effacer", () => ClearAllHistory_Click(this, new()), HistoryGrid.Items.Count > 0),
        ]);

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _library = await Api.Packages();
            _defaults = await Api.Defaults();
            await CredentialsWindow.Fill(CredentialBox, _schedule.CredentialId);
            foreach (var id in _schedule.PackageIds)
                _packages.Add(new PackageItem(_library.FirstOrDefault(p => p.Id == id) ?? new Package { Id = id, Name = $"(paquet supprimé n° {id})" }, ""));
            UpdatePackages();
            FillOptions();
            await LoadHistoryAsync();
            NameBox.Focus();
            NameBox.SelectAll();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

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
        var now = DateTime.Now;
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        var t = new ScheduleTrigger { Type = type, Start = start };
        if (type == TriggerType.Interval) { t.Start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0); t.IntervalMinutes = 60; }
        if (type == TriggerType.Weekly) t.Days = [start.DayOfWeek];
        if (type == TriggerType.Monthly) t.MonthDay = start.Day;
        var item = new TriggerItem(t);
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

    private static string TypeText(TriggerType t) => t switch
    {
        TriggerType.Once => "Une fois",
        TriggerType.Interval => "Intervalle",
        TriggerType.Daily => "Quotidien",
        TriggerType.Weekly => "Hebdomadaire",
        _ => "Mensuel",
    };

    /// <summary>Remplit l'éditeur avec le déclencheur sélectionné et n'affiche que ses réglages.</summary>
    private void ShowEditor()
    {
        NoTriggerText.Visibility = _triggers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteTriggerButton.IsEnabled = DuplicateTriggerButton.IsEnabled = Selected != null;
        Editor.Visibility = Selected == null ? Visibility.Hidden : Visibility.Visible;
        if (Selected is not { Trigger: var t }) return;

        _loading = true;
        EditorTitle.Text = TypeText(t.Type);
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

    private static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParse(text.Trim().Replace('h', ':'), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    private static int ParseInt(string text) => int.TryParse(text.Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>Reporte l'éditeur dans le déclencheur sélectionné (les valeurs mal saisies sont ignorées).</summary>
    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not { Trigger: var t } item) return;
        t.Enabled = TrigEnabled.IsChecked == true;
        var minutes = ParseInt(IntDays.Text) * 1440 + ParseInt(IntHours.Text) * 60 + ParseInt(IntMinutes.Text);
        if (minutes > 0) t.IntervalMinutes = minutes;
        t.UseTimeWindow = WindowBox.IsChecked == true;
        if (ParseTime(WinFrom.Text) is { } from) t.WindowStart = from;
        if (ParseTime(WinTo.Text) is { } to) t.WindowEnd = to;
        t.Days = DaysPanel.Children.Cast<CheckBox>().Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag).ToList();
        t.MonthDay = Chosen(MonthDayBox, 1);
        t.MonthWeek = MonthByWeek.IsChecked == true ? Chosen(MonthWeekBox, 1) : null;
        t.MonthWeekDay = Chosen(MonthWeekDayBox, DayOfWeek.Monday);
        if (StartDate.SelectedDate is { } sd && ParseTime(StartTime.Text) is { } st) t.Start = sd.Date + st;
        t.End = EndBox.IsChecked == true && EndDate.SelectedDate is { } ed && ParseTime(EndTime.Text) is { } et ? ed.Date + et : null;
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
        var next = TriggerCalendar.Next(t, DateTime.Now);
        NextText.Text = !t.Enabled ? "Déclencheur désactivé."
            : next is { } n ? $"Prochain déclenchement : {n:dddd dd/MM/yyyy HH:mm}"
            : "Plus aucun déclenchement à venir.";
    }

    // ---------- Cibles ----------

    private void AddNames(string text)
    {
        foreach (var raw in text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim().TrimStart('\\');
            if (name.Length > 0 && !_targets.Contains(name, StringComparer.OrdinalIgnoreCase)) _targets.Add(name);
        }
        UpdateTargets();
    }

    private void UpdateTargets() => TargetsCount.Text = _targets.Count == 0 ? "Aucune cible" : $"{_targets.Count} poste(s)";

    private void PasteNames()
    {
        if (Clipboard.ContainsText()) AddNames(Clipboard.GetText());
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddNames(ComputerBox.Text);
        ComputerBox.Clear();
        ComputerBox.Focus();
    }

    private void ComputerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Add_Click(sender, e);
        e.Handled = true;
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        ChooseButton.ContextMenu.PlacementTarget = ChooseButton;
        ChooseButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ChooseButton.ContextMenu.IsOpen = true;
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteNames();

    private void Ad_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(_targets.ToList()) { Owner = this };
        if (picker.ShowDialog() != true) return;
        foreach (var removed in picker.Unchecked)
            if (_targets.FirstOrDefault(t => t.Equals(removed, StringComparison.OrdinalIgnoreCase)) is { } item) _targets.Remove(item);
        AddNames(string.Join(" ", picker.Selected));
    }

    private void TextFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Liste de postes", Filter = "Fichiers texte (*.txt;*.csv)|*.txt;*.csv|Tous les fichiers|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AddNames(File.ReadAllText(dialog.FileName));
        }
        catch (IOException ex) { Ui.Error(this, ex); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TargetsList.SelectedItems.Cast<string>().ToList()) _targets.Remove(item);
        UpdateTargets();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        UpdateTargets();
    }

    private void Targets_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteNames(); e.Handled = true; }
    }

    // ---------- Paquets ----------

    private void UpdatePackages()
    {
        var selected = (PackagesList.SelectedItem as PackageItem)?.Package.Id;
        var items = _packages.Select((p, i) => new PackageItem(p.Package,
            $"{i + 1}. {p.Package.Name} ({p.Package.Steps.Count} étape{(p.Package.Steps.Count > 1 ? "s" : "")})")).ToList();
        _packages.Clear();
        foreach (var item in items) _packages.Add(item);
        PackagesList.SelectedItem = _packages.FirstOrDefault(p => p.Package.Id == selected);
        PackagesCount.Text = _packages.Count == 0 ? "Aucun paquet" : $"{_packages.Count} paquet(s)";
        if (string.IsNullOrWhiteSpace(NameBox.Text) && _packages.Count > 0) NameBox.Text = _packages[0].Package.Name;
        FillOptions();
    }

    private void AddPackage_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddPackageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in _library.Where(p => _packages.All(x => x.Package.Id != p.Id)))
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) =>
            {
                _packages.Add(new PackageItem(p, ""));
                UpdatePackages();
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(aucun autre paquet)", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void MovePackage(int delta)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        var i = _packages.IndexOf(item);
        var j = i + delta;
        if (j < 0 || j >= _packages.Count) return;
        _packages.Move(i, j);
        UpdatePackages();
    }

    private void PackageUp_Click(object sender, RoutedEventArgs e) => MovePackage(-1);
    private void PackageDown_Click(object sender, RoutedEventArgs e) => MovePackage(1);

    private void RemovePackage_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        _packages.Remove(item);
        UpdatePackages();
    }

    // ---------- Options ----------

    private void FillOptions()
    {
        var runAs = RunAsBox.Items.Count > 0 ? (RunAsBox.SelectedItem as ComboBoxItem)?.Tag : _schedule.RunAs;
        var copy = CopyModeBox.Items.Count > 0 ? (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag : _schedule.CopyMode;

        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.RunAs ?? _defaults.RunAs, Ui.RunAsText), Tag = null });
        foreach (var mode in Enum.GetValues<RunAsMode>()) RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode });
        RunAsBox.SelectedItem = RunAsBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, runAs)) ?? RunAsBox.Items[0];

        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.CopyMode ?? _defaults.CopyMode, Ui.CopyModeText), Tag = null });
        foreach (var mode in Enum.GetValues<CopyMode>()) CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode });
        CopyModeBox.SelectedItem = CopyModeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, copy)) ?? CopyModeBox.Items[0];
    }

    private string PackageSetting<T>(Func<Package, T> get, Func<T, string> text)
    {
        var values = _packages.Select(p => get(p.Package)).Distinct().ToList();
        return values.Count == 1 ? $"Paramètre du paquet ({text(values[0])})" : "Paramètre de chaque paquet";
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        FailCountBox.IsEnabled = StopFailBox.IsChecked == true;
        QueueMinutesBox.IsEnabled = QueueBox.IsChecked == true;
    }

    // ---------- Historique des cibles ----------

    private async Task LoadHistoryAsync()
    {
        if (_schedule.Id == 0)
        {
            HistoryGrid.ItemsSource = null;
            HistoryCount.Text = "Aucun historique : la planification n'est pas encore enregistrée.";
            return;
        }
        var rows = (await Api.ScheduleHistory(_schedule.Id))
            .Select(h => new HistoryRow(h.Computer, h.PackageId, h.PackageName, h.Succeeded, h.Failures, h.LastUtc)).ToList();
        HistoryGrid.ItemsSource = rows;
        var ok = rows.Count(r => r.Succeeded);
        HistoryCount.Text = rows.Count == 0 ? "Aucun déploiement terminé pour l'instant." : $"{ok} réussi(s) sur {rows.Count} poste(s) × paquet";
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        try { await LoadHistoryAsync(); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var keys = HistoryGrid.SelectedItems.Cast<HistoryRow>().Select(r => new ScheduleHistoryKey(r.Computer, r.PackageId)).ToList();
        if (keys.Count == 0 || _schedule.Id == 0) return;
        await ClearHistoryAsync(keys);
    }

    private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_schedule.Id == 0 || HistoryGrid.Items.Count == 0) return;
        if (MessageBox.Show(this, "Effacer tout l'historique des cibles ?\nTous les postes recevront de nouveau les paquets au prochain déclenchement.",
                "Historique des cibles", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ClearHistoryAsync([]);
    }

    private async Task ClearHistoryAsync(List<ScheduleHistoryKey> keys)
    {
        try
        {
            await Api.ClearScheduleHistory(_schedule.Id, keys);
            await LoadHistoryAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Enregistrement ----------

    /// <summary>Recopie la fenêtre dans un objet planification ; null (avec message) si une saisie est invalide.</summary>
    private Schedule? Collect()
    {
        if (!string.IsNullOrWhiteSpace(ComputerBox.Text)) { AddNames(ComputerBox.Text); ComputerBox.Clear(); }
        var name = NameBox.Text.Trim();
        if (name.Length == 0) return Invalid("Donnez un nom à la planification.", null, NameBox);
        if (_packages.Count == 0) return Invalid("Ajoutez au moins un paquet.", 2, null);
        if (_targets.Count == 0) return Invalid("Ajoutez au moins une cible.", 1, ComputerBox);
        int? failures = null, queue = null;
        if (StopFailBox.IsChecked == true)
        {
            if (ParseInt(FailCountBox.Text) is var n and > 0) failures = n;
            else return Invalid("Indiquez un nombre d'échecs (1 ou plus).", 3, FailCountBox);
        }
        if (QueueBox.IsChecked == true)
        {
            if (ParseInt(QueueMinutesBox.Text) is var m and > 0) queue = m;
            else return Invalid("Indiquez un délai d'attente en minutes (1 ou plus).", 3, QueueMinutesBox);
        }
        foreach (var item in _triggers)
        {
            var t = item.Trigger;
            if (t.Type == TriggerType.Weekly && t.Days.Count == 0)
            {
                TriggersList.SelectedItem = item;
                return Invalid("Déclencheur hebdomadaire : cochez au moins un jour.", 0, null);
            }
            if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start)
            {
                TriggersList.SelectedItem = item;
                return Invalid("La fin d'un déclencheur doit être après son début.", 0, null);
            }
        }

        return new Schedule
        {
            Id = _schedule.Id,
            Name = name,
            Enabled = EnabledBox.IsChecked == true,
            Triggers = _triggers.Select(t => t.Trigger).ToList(),
            PackageIds = _packages.Select(p => p.Package.Id).ToList(),
            Targets = _targets.ToList(),
            RunAs = (RunAsBox.SelectedItem as ComboBoxItem)?.Tag as RunAsMode?,
            CopyMode = (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag as CopyMode?,
            CredentialId = CredentialsWindow.Chosen(CredentialBox),
            StopOnSuccess = StopOnSuccessBox.IsChecked == true,
            StopAfterFailures = failures,
            QueueTimeoutMinutes = queue,
        };
    }

    private Schedule? Invalid(string message, int? tab, Control? focus)
    {
        if (tab is { } i) Tabs.SelectedIndex = i;
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        focus?.Focus();
        return null;
    }

    private async Task<bool> SaveAsync()
    {
        if (Collect() is not { } schedule) return false;
        try
        {
            Saved = await Api.SaveSchedule(schedule);
            return true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            return false;
        }
    }

    private async void ManageCredentials_Click(object sender, RoutedEventArgs e)
    {
        new CredentialsWindow { Owner = this }.ShowDialog();
        try
        {
            await CredentialsWindow.Fill(CredentialBox, CredentialsWindow.Chosen(CredentialBox));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync()) DialogResult = true;
    }
}
