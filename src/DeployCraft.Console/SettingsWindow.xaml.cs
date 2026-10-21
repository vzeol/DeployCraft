using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Preferences. Credentials have their own window (Options › Identifiants).</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Server settings (null until loaded) and their state when opened.</summary>
    private DeploymentDefaults? _defaults;
    private string _loaded = "";
    private bool _closing;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == Theme.Current);
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == UiLanguage.Saved());
        Loaded += async (_, _) => await LoadDefaultsAsync();
    }

    // ---------- Interface ----------

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string choice } && choice != Theme.Current)
            Theme.Set(choice);
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string choice } && choice != UiLanguage.Saved())
            UiLanguage.Set(choice);
    }

    // ---------- Deployment and Offline (server settings) ----------

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
        _defaults.Performance ??= new();
        ParallelBox.Text = _defaults.Performance.ParallelTargets.ToString();
        CopyLimitBox.Text = _defaults.Performance.CopyLimitMbps.ToString();
        CleanupBox.IsChecked = _defaults.History.Cleanup;
        CleanupDaysBox.Text = _defaults.History.KeepDays.ToString();
        DefaultOffline.Load(_defaults.Offline);
        DeploymentPage.IsEnabled = DefaultOffline.IsEnabled = true;
    }

    /// <summary>Modified server settings are saved on close; an error keeps the window open.</summary>
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

        var performance = new PerformanceSettings
        {
            ParallelTargets = int.TryParse(ParallelBox.Text.Trim(), out var parallel) ? parallel : 0,
            CopyLimitMbps = int.TryParse(CopyLimitBox.Text.Trim(), out var limit) ? limit : -1,
        };
        if (PerformanceSettings.Validate(performance) is { } performanceError)
        {
            e.Cancel = true;
            Tabs.SelectedIndex = 1;
            MessageDialog.Show(this, char.ToUpper(performanceError[0]) + performanceError[1..], "Préférences", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _defaults.Performance = performance;

        // Unchecked box: an unreadable number of days does not prevent closing, the last valid value is kept.
        var cleanup = CleanupBox.IsChecked == true;
        var daysValid = int.TryParse(CleanupDaysBox.Text.Trim(), out var days) && HistorySettings.Validate(days) == null;
        if (cleanup && !daysValid)
        {
            e.Cancel = true;
            Tabs.SelectedIndex = 1;
            MessageDialog.Show(this, $"Historique : saisissez un nombre de jours entre {HistorySettings.MinDays} et {HistorySettings.MaxDays}.", "Préférences", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _defaults.History.Cleanup = cleanup;
        if (daysValid) _defaults.History.KeepDays = days;
        if (JsonSerializer.Serialize(_defaults, Api.Json) == _loaded) return;

        e.Cancel = true; // closing resumed after the asynchronous save
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
