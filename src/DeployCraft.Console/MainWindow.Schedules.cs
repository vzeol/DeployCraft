using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedules view and the schedules of a package.</summary>
public partial class MainWindow
{
    private bool _refreshingSchedules;
    private readonly ScheduleList _scheduleList = new(Api.Schedules, Api.Schedule, Api.SaveSchedule, Api.DeleteSchedule, Api.RunSchedule);

    private async Task RefreshSchedulesAsync(long? select = null)
    {
        var result = await _scheduleList.RefreshAsync(() => SelectedSchedule?.Id, select);
        _refreshingSchedules = true;
        SchedulesGrid.ItemsSource = result.Rows;
        SchedulesGrid.SelectedItem = result.Selected;
        _refreshingSchedules = false;
        SchedulesSubtitle.Text = result.Subtitle;
        RunScheduleButton.IsEnabled = result.CanRun;
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
        OpenSchedule(ScheduleList.New(_view == View.Package ? SelectedPackage : null));
    }

    private void NewScheduleForPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is { } p) OpenSchedule(ScheduleList.New(p));
    }

    /// <summary>Adds the selected package to an existing schedule, chosen from a list.</summary>
    private async Task AttachToScheduleAsync()
    {
        if (SelectedPackage is not { } p) return;
        try
        {
            var candidates = await _scheduleList.CandidatesAsync(p.Id);
            if (candidates.Count == 0)
            {
                MessageDialog.Show(this, L.Get("MainWindow_NoScheduleToAttach"),
                    L.Get("MainWindow_SchedulesTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new SchedulePickerWindow(p.Name, candidates) { Owner = this };
            if (picker.ShowDialog() != true || picker.Selected is not { } chosen) return;
            await _scheduleList.AttachAsync(chosen.Id, p.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task DetachFromScheduleAsync(long scheduleId)
    {
        if (SelectedPackage is not { } p) return;
        try
        {
            if (!await _scheduleList.DetachAsync(scheduleId, p,
                text => MessageDialog.Show(this, text, L.Get("MainWindow_SchedulesTitle"), MessageBoxButton.OK, MessageBoxImage.Information),
                text => MessageDialog.Show(this, text, L.Get("MainWindow_SchedulesTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)) return;
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedule is not { } s) return;
        try { OpenSchedule(await _scheduleList.GetAsync(s.Id)); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void EditPackageSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (PackageSchedulesGrid.SelectedItem is not ScheduleRow row) return;
        try { OpenSchedule(await _scheduleList.GetAsync(row.Id)); }
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
            if (await _scheduleList.RunAsync(s.Id) is { } message)
                MessageDialog.Show(this, message, s.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void ToggleSchedule(Schedule s)
    {
        try
        {
            await _scheduleList.ToggleAsync(s.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DuplicateSchedule(Schedule s)
    {
        try
        {
            await ShowSchedule((await _scheduleList.DuplicateAsync(s.Id)).Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeleteSchedule(Schedule s)
    {
        try
        {
            if (!await _scheduleList.DeleteAsync(s, text => MessageDialog.Show(this, text,
                L.Get("MainWindow_Delete"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)) return;
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
