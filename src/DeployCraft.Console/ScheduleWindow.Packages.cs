using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedule window: packages and options.</summary>
public partial class ScheduleWindow
{
    // ---------- Paquets ----------

    private void UpdatePackages()
    {
        var selected = (PackagesList.SelectedItem as PackageItem)?.Package.Id;
        var items = _packages.Select((p, i) => new PackageItem(p.Package, ScheduleEditing.PackageLine(i, p.Package))).ToList();
        _packages.Clear();
        foreach (var item in items) _packages.Add(item);
        PackagesList.SelectedItem = _packages.FirstOrDefault(p => p.Package.Id == selected);
        PackagesCount.Text = _packages.Count == 0 ? "Aucun paquet" : $"{_packages.Count} paquet(s)";
        if (string.IsNullOrWhiteSpace(NameBox.Text) && _packages.Count > 0) NameBox.Text = _packages[0].Package.Name;
        FillOptions();
    }

    private void AddPackage_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddPackageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in _library.Where(p => _packages.All(x => x.Package.Id != p.Id)))
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) =>
            {
                _packages.Add(new PackageItem(p, ""));
                UpdatePackages();
            };
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

    private void FillOptions()
    {
        var runAs = RunAsBox.Items.Count > 0 ? (RunAsBox.SelectedItem as ComboBoxItem)?.Tag : _schedule.RunAs;
        var copy = CopyModeBox.Items.Count > 0 ? (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag : _schedule.CopyMode;

        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.RunAs ?? _defaults.RunAs, Ui.RunAsText), Tag = null });
        foreach (var mode in Enum.GetValues<RunAsMode>()) RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode });
        RunAsBox.SelectedItem = RunAsBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, runAs)) ?? RunAsBox.Items[0];

        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.CopyMode ?? _defaults.CopyMode, Ui.CopyModeText), Tag = null });
        foreach (var mode in Enum.GetValues<CopyMode>()) CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode });
        CopyModeBox.SelectedItem = CopyModeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, copy)) ?? CopyModeBox.Items[0];
    }

    private string PackageSetting<T>(Func<Package, T> get, Func<T, string> text) =>
        ScheduleEditing.PackageSetting(_packages.Select(p => get(p.Package)), text);

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        FailCountBox.IsEnabled = StopFailBox.IsChecked == true;
        QueueMinutesBox.IsEnabled = QueueBox.IsChecked == true;
    }
}
