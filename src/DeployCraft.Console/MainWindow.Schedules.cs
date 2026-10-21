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
            ? L.Get("MainWindow_SchedulesEmpty")
            : SelectedSchedule is { } s
                ? L.Format("MainWindow_SchedulesSubtitleOne", rows.Count, on, s.Name)
                : L.Format("MainWindow_SchedulesSubtitleAll", rows.Count, on);
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
                MessageDialog.Show(this, L.Get("MainWindow_NoScheduleToAttach"),
                    L.Get("MainWindow_SchedulesTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageDialog.Show(this, L.Format("MainWindow_OnlyPackageInSchedule", p.Name, schedule.Name),
                    L.Get("MainWindow_SchedulesTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageDialog.Show(this, L.Format("MainWindow_DetachConfirm", p.Name, schedule.Name), L.Get("MainWindow_SchedulesTitle"),
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
                MessageDialog.Show(this, run.Result ?? L.Get("MainWindow_NothingToDeploy"), s.Name, MessageBoxButton.OK, MessageBoxImage.Information);
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
            copy.Name += " " + L.Get("MainWindow_CopySuffix");
            copy.Enabled = false; // an active copy would double the deployments: it is enabled by hand
            await ShowSchedule((await Api.SaveSchedule(copy)).Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeleteSchedule(Schedule s)
    {
        if (MessageDialog.Show(this, L.Format("MainWindow_DeleteScheduleConfirm", s.Name),
                L.Get("MainWindow_Delete"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteSchedule(s.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
