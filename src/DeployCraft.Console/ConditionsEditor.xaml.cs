using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Onglet Conditions, commun au paquet et à ses étapes : modifie directement l'objet chargé.</summary>
public partial class ConditionsEditor : UserControl
{
    private Conditions _conditions = new();
    private bool _loading;

    public ConditionsEditor() => InitializeComponent();

    /// <summary>Affiche les conditions à modifier ; <paramref name="forPackage"/> adapte l'explication.</summary>
    public void Load(Conditions conditions, bool forPackage)
    {
        _loading = true;
        _conditions = conditions;
        Intro.Text = forPackage
            ? "Le paquet n'est installé que sur les postes qui remplissent toutes ces conditions. Les autres sont comptés réussis, sans rien exécuter (étapes « Ignoré »)."
            : "L'étape n'est exécutée que sur les postes qui remplissent toutes ces conditions. Sinon elle est sautée (« Ignoré ») et le paquet continue.";

        Win10Box.IsChecked = conditions.Windows.HasFlag(WindowsVersions.Windows10);
        Win11Box.IsChecked = conditions.Windows.HasFlag(WindowsVersions.Windows11);
        ServerBox.IsChecked = conditions.Windows.HasFlag(WindowsVersions.Server);
        OlderBox.IsChecked = conditions.Windows.HasFlag(WindowsVersions.Older);
        ArchBox.SelectedIndex = (int)conditions.Architecture;
        PowerShellBox.SelectedItem = PowerShellBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is string v && v == conditions.MinPowerShell?.ToString()) ?? PowerShellBox.Items[0];
        LoggedOnBox.SelectedIndex = (int)conditions.LoggedOn;
        FileBox.SelectedIndex = (int)conditions.File;
        FilePathBox.Text = conditions.FilePath ?? "";
        RegistryBox.SelectedIndex = (int)conditions.Registry;
        RegistryKeyBox.Text = conditions.RegistryKey ?? "";
        RegistryValueBox.Text = conditions.RegistryValue ?? "";
        UpdateEnabled();
        _loading = false;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized) return;
        var windows = WindowsVersions.None;
        if (Win10Box.IsChecked == true) windows |= WindowsVersions.Windows10;
        if (Win11Box.IsChecked == true) windows |= WindowsVersions.Windows11;
        if (ServerBox.IsChecked == true) windows |= WindowsVersions.Server;
        if (OlderBox.IsChecked == true) windows |= WindowsVersions.Older;
        _conditions.Windows = windows;
        _conditions.Architecture = (ArchitectureCondition)Math.Max(0, ArchBox.SelectedIndex);
        _conditions.MinPowerShell = (PowerShellBox.SelectedItem as ComboBoxItem)?.Tag is string v ? int.Parse(v) : null;
        _conditions.LoggedOn = (LoggedOnCondition)Math.Max(0, LoggedOnBox.SelectedIndex);
        _conditions.File = (PresenceCondition)Math.Max(0, FileBox.SelectedIndex);
        _conditions.FilePath = Clean(FilePathBox.Text);
        _conditions.Registry = (PresenceCondition)Math.Max(0, RegistryBox.SelectedIndex);
        _conditions.RegistryKey = Clean(RegistryKeyBox.Text);
        _conditions.RegistryValue = Clean(RegistryValueBox.Text);
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        FilePathBox.IsEnabled = FileBox.SelectedIndex > 0;
        RegistryKeyBox.IsEnabled = RegistryValueBox.IsEnabled = RegistryBox.SelectedIndex > 0;
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
