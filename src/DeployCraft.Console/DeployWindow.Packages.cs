using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Deploy once window: packages and options.</summary>
public partial class DeployWindow
{
    // ---------- Paquets ----------

    private void AddPackage(Package package)
    {
        if (_packages.Any(p => p.Package.Id == package.Id)) return;
        _packages.Add(new PackageItem(package, ""));
        UpdatePackages();
    }

    /// <summary>Renumbers the list, recomputes the offered steps and the "paramètre du paquet" labels.</summary>
    private void UpdatePackages()
    {
        var selected = (PackagesList.SelectedItem as PackageItem)?.Package.Id;
        var items = _packages.Select((p, i) => new PackageItem(p.Package, WindowInputs.PackageLine(i, p.Package))).ToList();
        _packages.Clear();
        foreach (var item in items) _packages.Add(item);
        PackagesList.SelectedItem = _packages.FirstOrDefault(p => p.Package.Id == selected);

        Title = _packages.Count == 1 ? $"Déployer une fois — {_packages[0].Package.Name}" : "Déployer une fois";
        PackagesHint.Visibility = _packages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // The start step applies to the first package, nested packages replaced by their steps (as at launch).
        var choices = DeployEditing.StepChoices(_packages.FirstOrDefault()?.Package, id => _library.FirstOrDefault(p => p.Id == id));
        StepChoices.Clear();
        foreach (var choice in choices) StepChoices.Add(choice);
        foreach (var t in _targets)
        {
            if (_packages.Count > 0 && t.StartStep > choices.Count) t.StartStep = 1;
            t.Refresh();
        }

        FillOptions();
        UpdateCount();
    }

    private void AddPackage_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddPackageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in _library.Where(p => _packages.All(x => x.Package.Id != p.Id)))
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) => AddPackage(p);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(aucun autre paquet)", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void MovePackage(int delta)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        var i = _packages.IndexOf(item);
        var j = i + delta;
        if (j < 0 || j >= _packages.Count) return;
        _packages.Move(i, j);
        UpdatePackages();
    }

    private void PackageUp_Click(object sender, RoutedEventArgs e) => MovePackage(-1);
    private void PackageDown_Click(object sender, RoutedEventArgs e) => MovePackage(1);

    private void RemovePackage_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        _packages.Remove(item);
        UpdatePackages();
    }

    // ---------- Options ----------

    private bool _filling;

    private List<Credential> _credentials = [];

    private async Task FillCredentialsAsync()
    {
        _filling = true;
        _credentials = await CredentialsWindow.Fill(CredentialBox, CredentialsWindow.Chosen(CredentialBox));
        _filling = false;
        UpdateHints();
    }

    /// <summary>"Paramètre du paquet (...)": the value common to the chosen packages, or "selon chaque paquet".</summary>
    private string PackageSetting<T>(Func<Package, T> get, Func<T, string> text) =>
        WindowInputs.PackageSetting(_packages.Select(p => get(p.Package)), text);

    private void FillOptions()
    {
        _filling = true;
        var runAs = (RunAsBox.SelectedItem as ComboBoxItem)?.Tag;
        var copy = (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag;

        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.RunAs ?? _defaults.RunAs, Ui.RunAsText), Tag = null });
        foreach (var mode in Enum.GetValues<RunAsMode>()) RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode });
        RunAsBox.SelectedItem = RunAsBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, runAs)) ?? RunAsBox.Items[0];

        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.CopyMode ?? _defaults.CopyMode, Ui.CopyModeText), Tag = null });
        foreach (var mode in Enum.GetValues<CopyMode>()) CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode });
        CopyModeBox.SelectedItem = CopyModeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, copy)) ?? CopyModeBox.Items[0];

        _filling = false;
        UpdateHints();
    }

    private RunAsMode? ChosenRunAs => (RunAsBox.SelectedItem as ComboBoxItem)?.Tag as RunAsMode?;
    private CopyMode? ChosenCopyMode => (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag as CopyMode?;

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) UpdateHints();
    }

    private void UpdateHints()
    {
        var modes = ChosenRunAs is { } r ? [r] : _packages.Select(p => p.Package.RunAs ?? _defaults.RunAs).Distinct().ToList();
        RunAsHint.Text = DeployEditing.RunAsHint(modes, _credentials.Count > 0);

        var copies = ChosenCopyMode is { } c ? [c] : _packages.Select(p => p.Package.CopyMode ?? _defaults.CopyMode).Distinct().ToList();
        CopyModeHint.Text = DeployEditing.CopyModeHint(copies);
    }

    private async void ManageCredentials_Click(object sender, RoutedEventArgs e)
    {
        new CredentialsWindow { Owner = this }.ShowDialog();
        try
        {
            await FillCredentialsAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
