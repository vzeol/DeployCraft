using System.IO;
using System.Windows;
using System.Windows.Input;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Schedule window: target computers.</summary>
public partial class ScheduleWindow
{
    // ---------- Cibles ----------

    private void AddNames(string text)
    {
        foreach (var name in ScheduleEditing.ComputerNames(text))
            if (!_targets.Contains(name, StringComparer.OrdinalIgnoreCase)) _targets.Add(name);
        UpdateTargets();
    }

    private void UpdateTargets() => TargetsCount.Text = _targets.Count == 0 ? "Aucune cible" : $"{_targets.Count} poste(s)";

    private void PasteNames()
    {
        if (Clipboard.ContainsText()) AddNames(Clipboard.GetText());
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddNames(ComputerBox.Text);
        ComputerBox.Clear();
        ComputerBox.Focus();
    }

    private void ComputerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Add_Click(sender, e);
        e.Handled = true;
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        ChooseButton.ContextMenu.PlacementTarget = ChooseButton;
        ChooseButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ChooseButton.ContextMenu.IsOpen = true;
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteNames();

    private void Ad_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(_targets.ToList()) { Owner = this };
        if (picker.ShowDialog() != true) return;
        foreach (var removed in picker.Unchecked)
            if (_targets.FirstOrDefault(t => t.Equals(removed, StringComparison.OrdinalIgnoreCase)) is { } item) _targets.Remove(item);
        AddNames(string.Join(" ", picker.Selected));
    }

    private void TextFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Liste de postes", Filter = "Fichiers texte (*.txt;*.csv)|*.txt;*.csv|Tous les fichiers|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AddNames(File.ReadAllText(dialog.FileName));
        }
        catch (IOException ex) { Ui.Error(this, ex); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TargetsList.SelectedItems.Cast<string>().ToList()) _targets.Remove(item);
        UpdateTargets();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        UpdateTargets();
    }

    private void Targets_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteNames(); e.Handled = true; }
    }
}
