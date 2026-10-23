using System.Text.Json;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Packages: create, edit, duplicate, rename, delete, import / export, deploy.</summary>
public partial class MainWindow
{
    // ---------- Packages ----------

    private async void NewPackage_Click(object sender, RoutedEventArgs e)
    {
        var window = new PackageWindow(new Package { Steps = [PackageWindow.NewStep(StepType.Install)], FolderId = CurrentFolderId }) { Owner = this };
        window.ShowDialog();
        if (window.Saved) await AfterPackageSaved(window.Package);
    }

    private async void EditPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } selected) return;
        try
        {
            var window = new PackageWindow(await Api.Package(selected.Id)) { Owner = this };
            window.ShowDialog();
            if (window.Saved) await AfterPackageSaved(window.Package);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task AfterPackageSaved(Package saved)
    {
        var fresh = await _packageList.AfterSavedAsync(saved);
        RebuildPackageNodes(saved.Id);
        SelectPackageNode(saved.Id);
        if (fresh != null) ShowPackage(fresh);
        await RefreshAsync(packages: false);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } source) return;
        try
        {
            var copy = await Api.Package(source.Id);
            copy.Id = 0;
            copy.Name += " " + L.Get("MainWindow_CopySuffix");
            await AfterPackageSaved(await Api.SavePackage(copy));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Import / export ----------

    private static string ExportFilter => L.Get("MainWindow_ExportFilter");

    private async void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is { } p) await ExportAsync([p.Id], p.Name);
    }

    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_packageList.Packages.Count > 0) await ExportAsync(_packageList.Packages.Select(p => p.Id), L.Get("MainWindow_ExportDefaultName"));
    }

    /// <summary>Saves the packages (and the nested packages they use) to a .json file.</summary>
    private async Task ExportAsync(IEnumerable<long> ids, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.Get("MainWindow_ExportTitle"),
            Filter = ExportFilter,
            FileName = string.Concat(defaultName.Split(System.IO.Path.GetInvalidFileNameChars())) + ".json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var export = await Api.ExportPackages(ids);
            await System.IO.File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(export, Api.JsonIndented));
            var nested = export.Packages.Count - ids.Distinct().Count();
            StatusRunning.Text = nested > 0
                ? L.Format("MainWindow_ExportedWithNested", export.Packages.Count, nested)
                : L.Format("MainWindow_Exported", export.Packages.Count);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(this, L.Format("MainWindow_ExportWriteFailed", ex.Message), L.Get("MainWindow_ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.Get("MainWindow_ImportPackages"),
            Filter = L.Get("MainWindow_ImportFilter"),
        };
        if (dialog.ShowDialog(this) != true || ImportWindow.ReadFile(this, dialog.FileName) is not { } content) return;
        var window = new ImportWindow(System.IO.Path.GetFileName(dialog.FileName), content) { Owner = this };
        if (window.ShowDialog() != true) return;
        await RefreshAsync(packages: true);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFolder is { } folder)
        {
            await RenameFolderAsync(folder);
            return;
        }
        if (SelectedPackage is not { } selected) return;
        if (InputDialog.Ask(this, L.Get("MainWindow_Rename"), L.Get("MainWindow_RenamePackagePrompt"), selected.Name) is not { } name || name == selected.Name) return;
        try
        {
            var package = await Api.Package(selected.Id);
            package.Name = name;
            await AfterPackageSaved(await Api.SavePackage(package));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeletePackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } p) return;
        if (MessageDialog.Show(this, L.Format("MainWindow_DeletePackageConfirm", p.Name),
                L.Get("MainWindow_Delete"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeletePackage(p.Id);
            _deploymentsNode.IsSelected = true;
            await RefreshAsync(packages: true);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Deployment ----------

    private void DeployOnce_Click(object sender, RoutedEventArgs e)
    {
        Deploy(SelectedPackage?.Id, []);
    }

    private async void Deploy(long? packageId, IReadOnlyList<TargetRequest> targets)
    {
        var window = new DeployWindow(packageId, targets) { Owner = this };
        var ok = window.ShowDialog() == true;
        if (window.ScheduleRequest is { } schedule) { OpenSchedule(schedule); return; }
        if (!ok || window.Deployments.FirstOrDefault() is not { } d) return;

        // The new deployment is shown where the user currently is.
        if (_view == MainView.Package && SelectedPackage?.Id == d.PackageId) PackageDeployments.Select(d.Id);
        else
        {
            _deploymentsNode.IsSelected = true;
            AllDeployments.Select(d.Id);
        }
        await RefreshAsync(packages: false);
    }
}
