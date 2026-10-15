using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Préférences. Les identifiants ont leur propre fenêtre (Options › Identifiants).</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Réglages du serveur (null tant qu'ils ne sont pas chargés) et leur état à l'ouverture.</summary>
    private DeploymentDefaults? _defaults;
    private string _loaded = "";
    private bool _closing;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == Theme.Current);
        Loaded += async (_, _) => await LoadDefaultsAsync();
    }

    // ---------- Interface ----------

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string choice } && choice != Theme.Current)
            Theme.Set(choice);
    }

    // ---------- Déploiement et Hors ligne (réglages du serveur) ----------

    private async Task LoadDefaultsAsync()
    {
        try
        {
            _defaults = await Api.Defaults();
        }
        catch (ApiException ex)
        {
            DeploymentPage.ToolTip = DefaultOffline.ToolTip = ex.Message;
            return;
        }
        _loaded = JsonSerializer.Serialize(_defaults, Api.Json);
        foreach (var mode in Enum.GetValues<RunAsMode>())
            DefaultRunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode, IsSelected = _defaults.RunAs == mode });
        foreach (var mode in Enum.GetValues<CopyMode>())
            DefaultCopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode, IsSelected = _defaults.CopyMode == mode });
        DefaultTimeoutBox.Text = _defaults.TimeoutMinutes.ToString();
        DefaultOffline.Load(_defaults.Offline);
        DeploymentPage.IsEnabled = DefaultOffline.IsEnabled = true;
    }

    /// <summary>Les réglages du serveur modifiés sont enregistrés à la fermeture ; une erreur garde la fenêtre ouverte.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing || _defaults == null) return;
        if (DefaultRunAsBox.SelectedItem is ComboBoxItem { Tag: RunAsMode runAs }) _defaults.RunAs = runAs;
        if (DefaultCopyModeBox.SelectedItem is ComboBoxItem { Tag: CopyMode copy }) _defaults.CopyMode = copy;
        if (!int.TryParse(DefaultTimeoutBox.Text.Trim(), out var timeout) || timeout < 1)
        {
            e.Cancel = true;
            Tabs.SelectedIndex = 1;
            MessageDialog.Show(this, "Délai max : saisissez un nombre de minutes (1 ou plus).", "Préférences", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _defaults.TimeoutMinutes = timeout;
        if (JsonSerializer.Serialize(_defaults, Api.Json) == _loaded) return;

        e.Cancel = true; // fermeture reprise après l'enregistrement asynchrone
        try
        {
            await Api.SaveDefaults(_defaults);
            _closing = true;
            Close();
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
        }
    }
}
