using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Package editor: package properties and options.</summary>
public partial class PackageWindow
{
    // ---------- Propriétés ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Package.Name = NameBox.Text;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = string.IsNullOrWhiteSpace(Package.Name) ? "Nouveau paquet" : $"{Package.Name} — Paquet";

    /// <summary>Délai qu'auront les étapes sans délai propre.</summary>
    private int PackageTimeout => Package.TimeoutMinutes ?? _defaults.TimeoutMinutes;

    /// <summary>Options et Hors ligne : chaque liste commence par « Par défaut (valeur des Préférences) ».</summary>
    private void FillOptions()
    {
        _loadingOptions = true;
        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = $"Par défaut ({Ui.RunAsText(_defaults.RunAs)})", Tag = null, IsSelected = Package.RunAs == null });
        foreach (var mode in Enum.GetValues<RunAsMode>())
            RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode, IsSelected = Package.RunAs == mode });
        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = $"Par défaut ({Ui.CopyModeText(_defaults.CopyMode)})", Tag = null, IsSelected = Package.CopyMode == null });
        foreach (var mode in Enum.GetValues<CopyMode>())
            CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode, IsSelected = Package.CopyMode == mode });
        TimeoutBox.Text = Package.TimeoutMinutes?.ToString() ?? "";
        TimeoutHint.Text = $"Pour les étapes sans délai propre. Vide : valeur des Préférences ({_defaults.TimeoutMinutes} min).";
        UpdateRunAsHint();

        OfflineDefaultBox.Content = "Utiliser les réglages des Préférences (Options › Préférences › Hors ligne)";
        OfflineDefaultBox.IsChecked = Package.Offline == null;
        PackageOffline.Load(Package.Offline ?? Clone(_defaults.Offline));
        PackageOffline.IsEnabled = Package.Offline != null;
        _loadingOptions = false;
    }

    private void UpdateRunAsHint() =>
        RunAsHint.Text = Ui.RunAsHint(Package.RunAs ?? _defaults.RunAs);

    private void RunAsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingOptions || RunAsBox.SelectedItem is not ComboBoxItem item) return;
        Package.RunAs = item.Tag as RunAsMode?;
        UpdateRunAsHint();
    }

    private void CopyModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingOptions && CopyModeBox.SelectedItem is ComboBoxItem item) Package.CopyMode = item.Tag as CopyMode?;
        UpdateStepWarnings();
    }

    private void TimeoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingOptions) return;
        var text = TimeoutBox.Text.Trim();
        if (text.Length == 0) Package.TimeoutMinutes = null;
        else if (int.TryParse(text, out var minutes)) Package.TimeoutMinutes = minutes;
    }

    /// <summary>Case cochée : le paquet suit les Préférences ; décochée : il garde ses propres réglages, partis de ceux des Préférences.</summary>
    private void OfflineDefault_Click(object sender, RoutedEventArgs e)
    {
        Package.Offline = OfflineDefaultBox.IsChecked == true ? null : Clone(_defaults.Offline);
        PackageOffline.Load(Package.Offline ?? Clone(_defaults.Offline));
        PackageOffline.IsEnabled = Package.Offline != null;
    }
}
