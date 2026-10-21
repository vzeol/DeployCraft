using System.Text.Json;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Packages: create, edit, duplicate, rename, delete, import / export, deploy.</summary>
public partial class MainWindow
{
    // ---------- Paquets ----------

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
        _packages = await Api.Packages();
        if (saved.FolderId is { } folder) _expandedFolders.Add(folder);
        RebuildPackageNodes(saved.Id);
        SelectPackageNode(saved.Id);
        if (_packages.FirstOrDefault(p => p.Id == saved.Id) is { } fresh) ShowPackage(fresh);
        await RefreshAsync(packages: false);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } source) return;
        try
        {
            var copy = await Api.Package(source.Id);
            copy.Id = 0;
            copy.Name += " (copie)";
            await AfterPackageSaved(await Api.SavePackage(copy));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Import / export ----------

    private const string ExportFilter = "Paquets DeployCraft (*.json)|*.json";

    private async void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is { } p) await ExportAsync([p.Id], p.Name);
    }

    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_packages.Count > 0) await ExportAsync(_packages.Select(p => p.Id), "Paquets DeployCraft");
    }

    /// <summary>Enregistre les paquets (et les paquets imbriqués qu'ils utilisent) dans un fichier .json.</summary>
    private async Task ExportAsync(IEnumerable<long> ids, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter",
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
                ? $"{export.Packages.Count} paquet(s) exporté(s), dont {nested} paquet(s) imbriqué(s) ajouté(s)"
                : $"{export.Packages.Count} paquet(s) exporté(s)";
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(this, $"Impossible d'écrire le fichier : {ex.Message}", "Exporter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer des paquets",
            Filter = "Exports de paquets (*.json;*.xml)|*.json;*.xml|Tous les fichiers|*.*",
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
        if (InputDialog.Ask(this, "Renommer", "Nouveau nom du paquet :", selected.Name) is not { } name || name == selected.Name) return;
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
        if (MessageDialog.Show(this, $"Supprimer le paquet « {p.Name} » ?\nL'historique de ses déploiements est conservé.",
                "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeletePackage(p.Id);
            _deploymentsNode.IsSelected = true;
            await RefreshAsync(packages: true);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Déploiement ----------

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

        // On affiche le nouveau déploiement là où l'utilisateur se trouve.
        if (_view == View.Package && SelectedPackage?.Id == d.PackageId) PackageDeployments.Select(d.Id);
        else
        {
            _deploymentsNode.IsSelected = true;
            AllDeployments.Select(d.Id);
        }
        await RefreshAsync(packages: false);
    }
}
