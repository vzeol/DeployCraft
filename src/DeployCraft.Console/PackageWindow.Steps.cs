using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Package editor: step toolbar, clipboard and right-click menus.</summary>
public partial class PackageWindow
{
    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(Tree);
        Ui.SelectOnRightClick(StepsGrid);

        Ui.MenuEntry[] AddEntries() =>
        [
            new("Nouvelle étape : Installer", () => AddInstall_Click(this, new())),
            new("Nouvelle étape : Commande", () => AddCommand_Click(this, new())),
            new("Nouvelle étape : PowerShell", () => AddPowerShell_Click(this, new())),
            new("Nouvelle étape : Copie de fichiers", () => AddCopy_Click(this, new())),
            new("Nouvelle étape : Pause", () => AddPause_Click(this, new())),
            new("Nouvelle étape : Redémarrer", () => AddReboot_Click(this, new())),
            new("Nouvelle étape : Message", () => AddMessage_Click(this, new())),
            new("Nouvelle étape : Paquet imbriqué", () => AddNested_Click(this, new())),
            Ui.Separator,
            new("Coller", () => Paste_Click(this, new()), CanPaste, "Ctrl+V"),
        ];

        IEnumerable<Ui.MenuEntry> StepEntries(PackageStep step)
        {
            return
            [
                .. AddEntries(),
                new("Couper", () => { SelectStep(step); Cut_Click(this, new()); }, Gesture: "Ctrl+X"),
                new("Copier", () => { SelectStep(step); Copy_Click(this, new()); }, Gesture: "Ctrl+C"),
                new("Dupliquer", () => { SelectStep(step); Duplicate_Click(this, new()); }),
                new(step.Enabled ? "Désactiver" : "Activer", () => SetEnabled(step, !step.Enabled)),
                Ui.Separator,
                new("Monter", () => { SelectStep(step); Move(-1); }, CanMove(step, -1), "Ctrl+Haut"),
                new("Descendre", () => { SelectStep(step); Move(1); }, CanMove(step, 1), "Ctrl+Bas"),
                Ui.Separator,
                new("Supprimer", () => { SelectStep(step); DeleteStep_Click(this, new()); }, Gesture: "Suppr"),
            ];
        }

        Ui.ContextMenu(Tree, () => SelectedStep is { } step ? StepEntries(step)
            : Tree.SelectedItem == _stepsNode ? AddEntries()
            : []);

        Ui.ContextMenu(StepsGrid, () => StepsGrid.SelectedItem is StepRow row
            ? [new("Modifier", () => SelectStep(Package.Steps[row.Number - 1])), Ui.Separator, .. StepEntries(Package.Steps[row.Number - 1])]
            : AddEntries());

        // Preview: the tree uses the arrow keys itself (Ctrl+Up / Down would never reach KeyDown).
        Tree.PreviewKeyDown += (_, e) =>
        {
            var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
            if (e.Key == Key.Delete && SelectedStep != null) DeleteStep_Click(this, e);
            else if (ctrl && e.Key == Key.C && SelectedStep != null) Copy_Click(this, e);
            else if (ctrl && e.Key == Key.X && SelectedStep != null) Cut_Click(this, e);
            else if (ctrl && e.Key == Key.V && CanPaste) Paste_Click(this, e);
            else if (ctrl && e.Key == Key.Up && CanMove(SelectedStep, -1)) Move(-1);
            else if (ctrl && e.Key == Key.Down && CanMove(SelectedStep, 1)) Move(1);
            else return;
            e.Handled = true;
        };
    }

    private void UpdateButtons()
    {
        var step = SelectedStep;
        UpButton.IsEnabled = CanMove(step, -1);
        DownButton.IsEnabled = CanMove(step, 1);
        CutButton.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = step != null;
        PasteButton.IsEnabled = CanPaste;
    }

    // ---------- Barre d'outils ----------

    private void NewStep_Click(object sender, RoutedEventArgs e)
    {
        NewStepButton.ContextMenu.PlacementTarget = NewStepButton;
        NewStepButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        NewStepButton.ContextMenu.IsOpen = true;
    }

    private void AddInstall_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Install));
    private void AddCommand_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Command));
    private void AddPowerShell_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.PowerShell));
    private void AddCopy_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.CopyFiles));
    private void AddPause_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Pause));
    private void AddReboot_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Reboot));
    private void AddMessage_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Message));
    private void AddNested_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Nested));

    private Package? FindPackage(long id) => _library.FirstOrDefault(p => p.Id == id);

    private void Nested_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStep || SelectedStep is not { Type: StepType.Nested } step || NestedBox.SelectedItem is not Package nested) return;
        step.NestedPackageId = nested.Id;
        StepName_TextChanged(this, null!);
    }

    private void AddStep(PackageStep step)
    {
        // Insérée après l'étape sélectionnée, sinon à la fin.
        StepList.InsertAfter(Package.Steps, SelectedStep, step);
        RebuildSteps(step);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private bool CanMove(PackageStep? step, int delta) => StepList.CanMove(Package.Steps, step, delta);

    private void Move(int delta)
    {
        if (SelectedStep is not { } step || !CanMove(step, delta)) return;
        StepList.Move(Package.Steps, step, delta);
        RebuildSteps(step);
        // The tree is rebuilt: keyboard focus goes back to the moved step, so Ctrl+Up / Down can be repeated.
        (Tree.SelectedItem as TreeViewItem)?.Focus();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        AddStep(StepList.Duplicate(step));
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyStep();

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (CopyStep()) DeleteStep_Click(sender, e);
    }

    private bool CopyStep()
    {
        if (SelectedStep is not { } step) return false;
        try
        {
            Clipboard.SetDataObject(new DataObject(StepFormat, JsonSerializer.Serialize(step, Api.Json)), copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageDialog.Show(this, "Presse-papiers occupé par une autre application : réessayez.", "Paquet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        UpdateButtons();
        return true;
    }

    private static bool CanPaste
    {
        get
        {
            try { return Clipboard.ContainsData(StepFormat); }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.GetData(StepFormat) is not string json) return;
            if (JsonSerializer.Deserialize<PackageStep>(json, Api.Json) is { } step) AddStep(step);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or JsonException)
        {
            MessageDialog.Show(this, "Impossible de coller l'étape : " + ex.Message, "Paquet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        RebuildSteps(StepList.Remove(Package.Steps, step));
        if (Package.Steps.Count == 0) _propertiesNode.IsSelected = true;
    }
}
