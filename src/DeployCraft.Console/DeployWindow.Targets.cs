using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Deploy once window: target computers, DNS check and start step.</summary>
public partial class DeployWindow
{
    // ---------- Targets ----------

    private void AddTarget(string name, int startStep)
    {
        var clean = name.Trim().TrimStart('\\');
        if (clean.Length == 0 || _targets.Any(t => t.Computer.Equals(clean, StringComparison.OrdinalIgnoreCase))) return;
        _targets.Add(new TargetItem(clean, startStep, this));
        _nameCheck.Stop();
        _nameCheck.Start();
    }

    /// <summary>Added computers are checked in batches, a moment after the last input.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _nameCheck = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private async void NameCheck_Tick(object? sender, EventArgs e)
    {
        _nameCheck.Stop();
        var pending = _targets.Where(t => t.Check == null).ToList();
        if (pending.Count == 0) return;
        try
        {
            var results = await Api.CheckNames(pending.Select(t => t.Computer));
            foreach (var target in pending)
                if (results.FirstOrDefault(r => r.Name.Equals(target.Computer, StringComparison.OrdinalIgnoreCase)) is { } result)
                    target.Check = result;
        }
        catch (ApiException) { } // service unreachable: the computers stay "en cours", without blocking the deployment
    }

    private void AddNames(string text)
    {
        foreach (var name in WindowInputs.ComputerNames(text)) AddTarget(name, 1);
        UpdateCount();
    }

    private void PasteNames()
    {
        if (Clipboard.ContainsText()) AddNames(Clipboard.GetText());
    }

    private void SetStartStep(int step)
    {
        foreach (var t in TargetsGrid.SelectedItems.Cast<TargetItem>()) t.StartStep = step;
    }

    private void UpdateCount()
    {
        CountText.Text = DeployEditing.CountText(_packages.Count, _targets.Count);
        DeployButton.IsEnabled = _targets.Count > 0 && _packages.Count > 0;
        ScheduleButton.IsEnabled = _packages.Count > 0;
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
        var picker = new AdPickerWindow(_targets.Select(t => t.Computer).ToList()) { Owner = this };
        if (picker.ShowDialog() != true) return;
        // The AD selection replaces the previous AD computers but keeps the manual entries.
        foreach (var removed in picker.Unchecked)
            if (_targets.FirstOrDefault(t => t.Computer.Equals(removed, StringComparison.OrdinalIgnoreCase)) is { } item) _targets.Remove(item);
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
        foreach (var item in TargetsGrid.SelectedItems.Cast<TargetItem>().ToList()) _targets.Remove(item);
        UpdateCount();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        UpdateCount();
    }

    private void Targets_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is ComboBox or ComboBoxItem) return; // choice of the start step in progress
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteNames(); e.Handled = true; }
    }
}
