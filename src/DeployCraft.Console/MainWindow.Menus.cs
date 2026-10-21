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
        // No shortcut while typing in a text box (except F5).
        if (e.Key != Key.F5 && Keyboard.FocusedElement is TextBox) return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Ctrl+R on the computers grid: left to the panel (redeploys the selected computers).
        if (key == Key.R && ctrl && CurrentPanel.TargetsFocused) return;
        // Without access to the service, only F5 (retry) remains available.
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

    /// <summary>Enables or greys out the entries of the Fichier and Édition menus according to the selection.</summary>
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
                    new(L.Get("MainWindow_DeployOnceDots"), () => DeployOnce_Click(this, new()), Gesture: "Ctrl+D"),
                    new(L.Get("MainWindow_CtxEdit"), () => EditPackage_Click(this, new()), Gesture: "Ctrl+O"),
                    new(L.Get("MainWindow_CtxAddToSchedule"), () => _ = AttachToScheduleAsync()),
                    Ui.Separator,
                    new(L.Get("MainWindow_CtxCut"), () => CutOrCopyNode(cut: true), Gesture: "Ctrl+X"),
                    new(L.Get("MainWindow_CtxCopy"), () => CutOrCopyNode(cut: false), Gesture: "Ctrl+C"),
                    new(L.Get("MainWindow_CtxPaste"), () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    new(L.Get("MainWindow_CtxDuplicate"), () => Duplicate_Click(this, new()), Gesture: L.Get("MainWindow_KeyAltInsert")),
                    new(L.Get("MainWindow_Rename"), () => Rename_Click(this, new()), Gesture: "F2"),
                    new(L.Get("MainWindow_CtxExport"), () => ExportPackage_Click(this, new())),
                    Ui.Separator,
                    new(L.Get("MainWindow_Delete"), () => DeletePackage_Click(this, new()), Gesture: L.Get("MainWindow_KeyDelete")),
                ];
            if (SelectedFolder is { } folder)
                return
                [
                    new(L.Get("MainWindow_CtxNewPackage"), () => NewPackage_Click(this, new()), Gesture: "Ctrl+N"),
                    new(L.Get("MainWindow_CtxNewFolder"), () => NewFolder_Click(this, new()), Gesture: L.Get("MainWindow_KeyCtrlShiftF")),
                    Ui.Separator,
                    new(L.Get("MainWindow_CtxCut"), () => CutOrCopyNode(cut: true), Gesture: "Ctrl+X"),
                    new(L.Get("MainWindow_CtxPaste"), () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    new(L.Get("MainWindow_Rename"), () => _ = RenameFolderAsync(folder), Gesture: "F2"),
                    new(L.Get("MainWindow_Delete"), () => _ = DeleteFolderAsync(folder), Gesture: L.Get("MainWindow_KeyDelete")),
                ];
            if (Nav.SelectedItem == _packagesNode)
                return
                [
                    new(L.Get("MainWindow_CtxNewPackage"), () => NewPackage_Click(this, new()), Gesture: "Ctrl+N"),
                    new(L.Get("MainWindow_CtxNewFolder"), () => NewFolder_Click(this, new()), Gesture: L.Get("MainWindow_KeyCtrlShiftF")),
                    new(L.Get("MainWindow_CtxPaste"), () => _ = PasteNodeAsync(), CanPaste, "Ctrl+V"),
                    Ui.Separator,
                    new(L.Get("MainWindow_CtxImportPackages"), () => Import_Click(this, new())),
                    new(L.Get("MainWindow_CtxExportAll"), () => ExportAll_Click(this, new()), _packages.Count > 0),
                ];
            return [new(L.Get("MainWindow_Refresh"), () => Refresh_Click(this, new()), Gesture: "F5")];
        });

        Ui.ContextMenu(StepsGrid, () => SelectedPackage != null
            ? [new(L.Get("MainWindow_CtxEditPackage"), () => EditPackage_Click(this, new()))]
            : []);

        Ui.SelectOnRightClick(SchedulesGrid);
        Ui.ContextMenu(SchedulesGrid, () =>
        {
            if (SelectedSchedule is not { } s) return [new(L.Get("MainWindow_NewScheduleDots"), () => NewSchedule_Click(this, new()), Gesture: L.Get("MainWindow_KeyCtrlShiftN"))];
            return
            [
                new(L.Get("MainWindow_StartNow"), () => RunSchedule_Click(this, new())),
                new(L.Get("MainWindow_CtxEdit"), () => EditSchedule_Click(this, new())),
                Ui.Separator,
                new(s.Enabled ? L.Get("MainWindow_CtxDisable") : L.Get("MainWindow_CtxEnable"), () => ToggleSchedule(s)),
                new(L.Get("MainWindow_CtxDuplicate"), () => DuplicateSchedule(s)),
                Ui.Separator,
                new(L.Get("MainWindow_Delete"), () => DeleteSchedule(s), Gesture: L.Get("MainWindow_KeyDelete")),
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
                    entries.Add(new(L.Get("MainWindow_CtxShowPackage"), () => SelectPackageNode(packageId)));
                entries.Add(Ui.Separator);
            }
            entries.Add(new(L.Get("MainWindow_RefreshCatalog"), () => LibraryRefresh_Click(this, new())));
            return entries;
        });

        Ui.SelectOnRightClick(PackageSchedulesGrid);
        Ui.ContextMenu(PackageSchedulesGrid, () => PackageSchedulesGrid.SelectedItem is ScheduleRow row
            ?
            [
                new(L.Get("MainWindow_CtxEdit"), () => EditPackageSchedule_Click(this, new())),
                new(L.Get("MainWindow_CtxRemoveFromSchedule"), () => _ = DetachFromScheduleAsync(row.Id)),
                Ui.Separator,
                new(L.Get("MainWindow_CtxAddToOtherSchedule"), () => _ = AttachToScheduleAsync()),
                new(L.Get("MainWindow_NewScheduleDots"), () => NewScheduleForPackage_Click(this, new())),
            ]
            :
            [
                new(L.Get("MainWindow_CtxAddToSchedule"), () => _ = AttachToScheduleAsync()),
                new(L.Get("MainWindow_NewScheduleDots"), () => NewScheduleForPackage_Click(this, new())),
            ]);
    }

    // ---------- Édition (acts on the selected deployment) ----------

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

    /// <summary>Delete: the package if the tree has focus, the schedule if its grid has it, otherwise the selected deployment.</summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TreeHasFocus && SelectedFolder is { } folder) _ = DeleteFolderAsync(folder);
        else if (TreeHasFocus) DeletePackage_Click(sender, e);
        else if (SchedulesGrid.IsKeyboardFocusWithin && SelectedSchedule is { } s) DeleteSchedule(s);
        else if (CurrentPanel.CanDelete) CurrentPanel.DeleteSelected();
    }
}
