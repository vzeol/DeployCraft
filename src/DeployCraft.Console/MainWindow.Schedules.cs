using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedules view and the schedules of a package.</summary>
public partial class MainWindow
{
    private bool _refreshingSchedules;

    private async Task RefreshSchedulesAsync(long? select = null)
    {
        var rows = (await Api.Schedules()).Select(s => new ScheduleRow(s)).ToList();
        var selected = select ?? SelectedSchedule?.Id;
        _refreshingSchedules = true;
        SchedulesGrid.ItemsSource = rows;
        SchedulesGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        _refreshingSchedules = false;
        var on = rows.Count(r => r.Schedule.Enabled);
        SchedulesSubtitle.Text = rows.Count == 0
            ? "Aucune planification : créez-en une pour déployer automatiquement (ou réessayer jusqu'à réussite)"
            : $"{rows.Count} planification(s), {on} activée(s) · en dessous, les déploiements {(SelectedSchedule is { } s ? $"de « {s.Name} »" : "de toutes les planifications")}";
        RunScheduleButton.IsEnabled = SelectedSchedule != null;
    }

    private async void SchedulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSchedules) return;
        RunScheduleButton.IsEnabled = SelectedSchedule != null;
        await RefreshAsync(packages: false);
    }

    // ---------- Schedules ----------

    private void NewSchedule_Click(object sender, RoutedEventArgs e)
    {
        var schedule = new Schedule();
        if (SelectedPackage is { } p && _view == View.Package) { schedule.PackageIds = [p.Id]; schedule.Name = p.Name; }
        OpenSchedule(schedule);
    }

    private void NewScheduleForPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is { } p) OpenSchedule(new Schedule { PackageIds = [p.Id], Name = p.Name });
    }

    /// <summary>Adds the selected package to an existing schedule, chosen from a list.</summary>
    private async Task AttachToScheduleAsync()
    {
        if (SelectedPackage is not { } p) return;
        try
        {
            var candidates = (await Api.Schedules()).Where(s => !s.PackageIds.Contains(p.Id)).ToList();
            if (candidates.Count == 0)
            {
                MessageDialog.Show(this, "Aucune planification où ajouter ce paquet (il fait déjà partie de toutes, ou il n'y en a aucune).",
                    "Planifications", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new SchedulePickerWindow(p.Name, candidates) { Owner = this };
            if (picker.ShowDialog() != true || picker.Selected is not { } chosen) return;
            var schedule = await Api.Schedule(chosen.Id);
            if (schedule.Attach(p.Id)) await Api.SaveSchedule(schedule);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task DetachFromScheduleAsync(long scheduleId)
    {
        if (SelectedPackage is not { } p) return;
        try
        {
            var schedule = await Api.Schedule(scheduleId);
            if (schedule.PackageIds.Count == 1 && schedule.PackageIds[0] == p.Id)
            {
                MessageDialog.Show(this, $"« {p.Name} » est le seul paquet de « {schedule.Name} » : désactivez ou supprimez plutôt la planification.",
                    "Planifications", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageDialog.Show(this, $"Retirer « {p.Name} » de la planification « {schedule.Name} » ?", "Planifications",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (schedule.Detach(p.Id)) await Api.SaveSchedule(schedule);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedule is not { } s) return;
        try { OpenSchedule(await Api.Schedule(s.Id)); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void EditPackageSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (PackageSchedulesGrid.SelectedItem is not ScheduleRow row) return;
        try { OpenSchedule(await Api.Schedule(row.Id)); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    /// <summary>Opens the window of a schedule; after saving, shows the schedules page.</summary>
    public async void OpenSchedule(Schedule schedule)
    {
        var window = new ScheduleWindow(schedule) { Owner = this };
        window.ShowDialog();
        if (window.Saved is not { } saved) return;
        if (_view != View.Package) await ShowSchedule(saved.Id);
        else await RefreshAsync(packages: false);
    }

    private async Task ShowSchedule(long id)
    {
        _schedulesNode.IsSelected = true;
        SwitchTo(View.Schedules);
        try { await RefreshSchedulesAsync(select: id); }
        catch (ApiException) { }
        await RefreshAsync(packages: false);
    }

    private async void RunSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedule is not { } s) return;
        try
        {
            var run = await Api.RunSchedule(s.Id);
            if (run.Deployments.Count == 0)
                MessageDialog.Show(this, run.Result ?? "Rien à déployer.", s.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void ToggleSchedule(Schedule s)
    {
        try
        {
            var full = await Api.Schedule(s.Id);
            full.Enabled = !full.Enabled;
            await Api.SaveSchedule(full);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DuplicateSchedule(Schedule s)
    {
        try
        {
            var copy = await Api.Schedule(s.Id);
            copy.Id = 0;
            copy.Name += " (copie)";
            copy.Enabled = false; // an active copy would double the deployments: it is enabled by hand
            await ShowSchedule((await Api.SaveSchedule(copy)).Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeleteSchedule(Schedule s)
    {
        if (MessageDialog.Show(this, $"Supprimer la planification « {s.Name} » ?\nSes déploiements passés restent dans l'historique.",
                "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteSchedule(s.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
