using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Schedule window, with tabs: triggers, targets, packages, options,
/// target history (success and number of failures per computer and per package).
/// </summary>
public partial class ScheduleWindow : Window
{
    private readonly Schedule _schedule;
    private readonly ObservableCollection<TriggerItem> _triggers = [];
    private readonly ObservableCollection<string> _targets = [];
    private readonly ObservableCollection<PackageItem> _packages = [];
    private List<Package> _library = [];
    /// <summary>Preferences: value of packages left on "default".</summary>
    private DeploymentDefaults _defaults = new();
    private bool _loading;

    /// <summary>Saved schedule (after OK).</summary>
    public Schedule? Saved { get; private set; }

    public ScheduleWindow(Schedule schedule)
    {
        InitializeComponent();
        _schedule = schedule;
        TriggersList.ItemsSource = _triggers;
        TargetsList.ItemsSource = _targets;
        PackagesList.ItemsSource = _packages;

        Title = schedule.Id == 0 ? L.Get("ScheduleWindow_NewSchedule") : L.Format("ScheduleWindow_ScheduleNumber", schedule.Id);
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
            new(L.Get("ScheduleWindow_PasteList"), PasteNames, Clipboard.ContainsText(), "Ctrl+V"),
            new("Active Directory…", () => Ad_Click(this, new())),
            new(L.Get("ScheduleWindow_TextFile"), () => TextFile_Click(this, new())),
            Ui.Separator,
            new(L.Get("ScheduleWindow_RemoveItem"), () => Remove_Click(this, new()), TargetsList.SelectedItems.Count > 0, L.Get("ScheduleWindow_DeleteKey")),
            new(L.Get("ScheduleWindow_RemoveAll"), () => Clear_Click(this, new()), _targets.Count > 0),
        ]);
        Ui.SelectOnRightClick(HistoryGrid);
        Ui.ContextMenu(HistoryGrid, () =>
        [
            new(L.Get("ScheduleWindow_ClearRedeploy"), () => ClearHistory_Click(this, new()), HistoryGrid.SelectedItems.Count > 0),
            new(L.Get("ScheduleWindow_ClearAll"), () => ClearAllHistory_Click(this, new()), HistoryGrid.Items.Count > 0),
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
                _packages.Add(new PackageItem(_library.FirstOrDefault(p => p.Id == id) ?? new Package { Id = id, Name = L.Format("ScheduleWindow_DeletedPackage", id) }, ""));
            UpdatePackages();
            FillOptions();
            await LoadHistoryAsync();
            NameBox.Focus();
            NameBox.SelectAll();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Saving ----------

    /// <summary>Copies the window into a schedule object; null (with a message) if an input is invalid.</summary>
    private Schedule? Collect()
    {
        if (!string.IsNullOrWhiteSpace(ComputerBox.Text)) { AddNames(ComputerBox.Text); ComputerBox.Clear(); }
        var name = NameBox.Text.Trim();
        if (name.Length == 0) return Invalid(L.Get("ScheduleWindow_NeedName"), null, NameBox);
        if (_packages.Count == 0) return Invalid(L.Get("ScheduleWindow_NeedPackage"), 2, null);
        if (_targets.Count == 0) return Invalid(L.Get("ScheduleWindow_NeedTarget"), 1, ComputerBox);
        int? failures = null, queue = null;
        if (StopFailBox.IsChecked == true)
        {
            if (ScheduleEditing.ParseInt(FailCountBox.Text) is var n and > 0) failures = n;
            else return Invalid(L.Get("ScheduleWindow_NeedFailureCount"), 3, FailCountBox);
        }
        if (QueueBox.IsChecked == true)
        {
            if (ScheduleEditing.ParseInt(QueueMinutesBox.Text) is var m and > 0) queue = m;
            else return Invalid(L.Get("ScheduleWindow_NeedQueueDelay"), 3, QueueMinutesBox);
        }
        if (ScheduleEditing.TriggerProblem(_triggers.Select(t => t.Trigger).ToList()) is var (index, message))
        {
            TriggersList.SelectedItem = _triggers[index];
            return Invalid(message, 0, null);
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
        MessageDialog.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
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
