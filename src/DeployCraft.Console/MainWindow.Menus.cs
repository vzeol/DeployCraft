using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeployCraft.Console;

/// <summary>Keyboard shortcuts, menus and right-click menus of the main window.</summary>
public partial class MainWindow
{
    // ---------- Raccourcis clavier ----------

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Help_Click(this, e);
            e.Handled = true;
            return;
        }
        // Pas de raccourci pendant la saisie dans une zone de texte (sauf F5).
        if (e.Key != Key.F5 && Keyboard.FocusedElement is TextBox) return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Ctrl+R sur la grille des postes : laissé au panneau (redéploie les postes sélectionnés).
        if (key == Key.R && ctrl && CurrentPanel.TargetsFocused) return;
        // Sans accès au service, seul F5 (réessayer) reste disponible.
        if (!_connected && key != Key.F5) return;

        Action? action = (key, ctrl, ctrlShift, alt) switch
        {
            (Key.F5, _, _, _) => () => Refresh_Click(this, e),
            (Key.D0 or Key.NumPad0, true, _, _) => () => ViewHome_Click(this, e),
            (Key.D1 or Key.NumPad1, true, _, _) => () => ViewDeployments_Click(this, e),
            (Key.D2 or Key.NumPad2, true, _, _) => () => ViewSchedules_Click(this, e),
            (Key.D3 or Key.NumPad3, true, _, _) => () => ViewLibrary_Click(this, e),
            (Key.D4 or Key.NumPad4, true, _, _) => () => ViewPackages_Click(this, e),
            (Key.F, true, _, _) => () => ViewFilter_Click(this, e),
            (Key.N, true, _, _) => () => NewPackage_Click(this, e),
            (Key.N, _, true, _) => () => NewSchedule_Click(this, e),
            (Key.F, _, true, _) => () => NewFolder_Click(this, e),
            (Key.O, true, _, _) => () => EditPackage_Click(this, e),
            (Key.D, true, _, _) => () => DeployOnce_Click(this, e),
            (Key.R, true, _, _) => () => Redeploy_Click(this, e),
            (Key.R, _, true, _) => () => RedeployFailed_Click(this, e),
            (Key.Insert, _, _, true) => () => Duplicate_Click(this, e),
            (Key.OemComma, true, _, _) => () => Settings_Click(this, e),
            (Key.F2, _, _, _) when Keyboard.Modifiers == ModifierKeys.None => () => Rename_Click(this, e),
            (Key.X, true, _, _) when TreeHasFocus => () => CutOrCopyNode(cut: true),
            (Key.C, true, _, _) when TreeHasFocus => () => CutOrCopyNode(cut: false),
            (Key.V, true, _, _) when TreeHasFocus => () => _ = PasteNodeAsync(),
            (Key.Delete, _, _, _) when Keyboard.Modifiers == ModifierKeys.None => () => Delete_Click(this, e),
            _ => null,
        };
        if (action == null) return;
        action();
        e.Handled = true;
    }

    /// <summary>Active ou grise les entrées des menus Fichier et Édition selon la sélection.</summary>
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        var package = _connected && SelectedPackage != null;
        MenuNewPackage.IsEnabled = MenuNewSchedule.IsEnabled = _connected;
        MenuNewFolder.IsEnabled = _connected;
        MenuDuplicate.IsEnabled = MenuOpen.IsEnabled = MenuDeployOnce.IsEnabled = MenuExport.IsEnabled = package;
        var folder = _connected && SelectedFolder != null;
        MenuRename.IsEnabled = package || folder;
        MenuImport.IsEnabled = _connected;
        MenuExportAll.IsEnabled = _connected && _packages.Count > 0;
        var panel = CurrentPanel;
        MenuAbort.IsEnabled = panel.CanAbort;
        MenuRedeploy.IsEnabled = panel.CanRedeploy;
        MenuRedeployFailed.IsEnabled = MenuRedeployFromStep.IsEnabled = panel.CanRedeployFailed;
        MenuDelete.IsEnabled = TreeHasFocus ? package || folder : panel.CanDelete;
    }

    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(Nav);
        Ui.SelectOnRightClick(StepsGrid);

        Ui.ContextMenu(Nav, () =>
        {
            if (SelectedPackage != null)
                return
                [
                    new("Déployer une fois…", () => DeployOnce_Click(this, new()), Gesture: "Ctrl+D"),
                    new("Modifier…", () => EditPackage_Click(this, new()), Gesture: "Ctrl+O"),
                    new("Ajouter à une planification…", () => _ = AttachToScheduleAsync()),
                    Ui.Separator,
                    new("Couper", () => CutOrCopyNode(cut: true), Gesture: "Ctrl+X"),
                    new("Copier", () => CutOrCopyNode(cut: false), Gesture: "Ctrl+C"),
                    new("Coller", () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    new("Dupliquer", () => Duplicate_Click(this, new()), Gesture: "Alt+Inser"),
                    new("Renommer", () => Rename_Click(this, new()), Gesture: "F2"),
                    new("Exporter…", () => ExportPackage_Click(this, new())),
                    Ui.Separator,
                    new("Supprimer", () => DeletePackage_Click(this, new()), Gesture: "Suppr"),
                ];
            if (SelectedFolder is { } folder)
                return
                [
                    new("Nouveau paquet…", () => NewPackage_Click(this, new()), Gesture: "Ctrl+N"),
                    new("Nouveau dossier…", () => NewFolder_Click(this, new()), Gesture: "Ctrl+Maj+F"),
                    Ui.Separator,
                    new("Couper", () => CutOrCopyNode(cut: true), Gesture: "Ctrl+X"),
                    new("Coller", () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    new("Renommer", () => _ = RenameFolderAsync(folder), Gesture: "F2"),
                    new("Supprimer", () => _ = DeleteFolderAsync(folder), Gesture: "Suppr"),
                ];
            if (Nav.SelectedItem == _packagesNode)
                return
                [
                    new("Nouveau paquet…", () => NewPackage_Click(this, new()), Gesture: "Ctrl+N"),
                    new("Nouveau dossier…", () => NewFolder_Click(this, new()), Gesture: "Ctrl+Maj+F"),
                    new("Coller", () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    Ui.Separator,
                    new("Importer des paquets…", () => Import_Click(this, new())),
                    new("Exporter tous les paquets…", () => ExportAll_Click(this, new()), _packages.Count > 0),
                ];
            return [new("Actualiser", () => Refresh_Click(this, new()), Gesture: "F5")];
        });

        Ui.ContextMenu(StepsGrid, () => SelectedPackage != null
            ? [new("Modifier le paquet…", () => EditPackage_Click(this, new()))]
            : []);

        Ui.SelectOnRightClick(SchedulesGrid);
        Ui.ContextMenu(SchedulesGrid, () =>
        {
            if (SelectedSchedule is not { } s) return [new("Nouvelle planification…", () => NewSchedule_Click(this, new()), Gesture: "Ctrl+Maj+N")];
            return
            [
                new("Démarrer maintenant", () => RunSchedule_Click(this, new())),
                new("Modifier…", () => EditSchedule_Click(this, new())),
                Ui.Separator,
                new(s.Enabled ? "Désactiver" : "Activer", () => ToggleSchedule(s)),
                new("Dupliquer", () => DuplicateSchedule(s)),
                Ui.Separator,
                new("Supprimer", () => DeleteSchedule(s), Gesture: "Suppr"),
            ];
        });

        Ui.SelectOnRightClick(LibraryGrid);
        Ui.ContextMenu(LibraryGrid, () =>
        {
            var rows = SelectedLibraryRows;
            var entries = new List<Ui.MenuEntry>();
            if (rows.Count > 0)
            {
                entries.Add(new(LibraryDownloadText.Text, () => LibraryDownload_Click(this, new()), rows.Any(r => r.CanDownload)));
                if (rows.Count == 1 && rows[0].Item.PackageId is { } packageId)
                    entries.Add(new("Afficher le paquet", () => SelectPackageNode(packageId)));
                entries.Add(Ui.Separator);
            }
            entries.Add(new("Actualiser le catalogue", () => LibraryRefresh_Click(this, new())));
            return entries;
        });

        Ui.SelectOnRightClick(PackageSchedulesGrid);
        Ui.ContextMenu(PackageSchedulesGrid, () => PackageSchedulesGrid.SelectedItem is ScheduleRow row
            ?
            [
                new("Modifier…", () => EditPackageSchedule_Click(this, new())),
                new("Retirer ce paquet de la planification", () => _ = DetachFromScheduleAsync(row.Id)),
                Ui.Separator,
                new("Ajouter à une autre planification…", () => _ = AttachToScheduleAsync()),
                new("Nouvelle planification…", () => NewScheduleForPackage_Click(this, new())),
            ]
            :
            [
                new("Ajouter à une planification…", () => _ = AttachToScheduleAsync()),
                new("Nouvelle planification…", () => NewScheduleForPackage_Click(this, new())),
            ]);
    }

    // ---------- Édition (agit sur le déploiement sélectionné) ----------

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanAbort) CurrentPanel.AbortDeployment();
    }

    private void Redeploy_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeploy) CurrentPanel.Redeploy();
    }

    private void RedeployFailed_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeployFailed) CurrentPanel.RedeployFailed();
    }

    private void RedeployFromStep_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeployFailed) CurrentPanel.RedeployFromFailedStep();
    }

    /// <summary>Suppr : le paquet si l'arbre a le focus, la planification si sa grille l'a, sinon le déploiement sélectionné.</summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TreeHasFocus && SelectedFolder is { } folder) _ = DeleteFolderAsync(folder);
        else if (TreeHasFocus) DeletePackage_Click(sender, e);
        else if (SchedulesGrid.IsKeyboardFocusWithin && SelectedSchedule is { } s) DeleteSchedule(s);
        else if (CurrentPanel.CanDelete) CurrentPanel.DeleteSelected();
    }
}
