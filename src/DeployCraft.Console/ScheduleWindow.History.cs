using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedule window: target history (success and failures per computer and package).</summary>
public partial class ScheduleWindow
{
    // ---------- Target history ----------

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
        HistoryCount.Text = ScheduleEditing.HistorySummary(rows);
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
        if (MessageDialog.Show(this, "Effacer tout l'historique des cibles ?\nTous les postes recevront de nouveau les paquets au prochain déclenchement.",
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
}
