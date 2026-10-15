using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Réglages hors ligne (réveil, nouveaux essais), communs au paquet et aux Préférences.</summary>
public partial class OfflineEditor : UserControl
{
    private OfflineSettings _settings = new();
    private bool _loading;

    public OfflineEditor() => InitializeComponent();

    public void Load(OfflineSettings settings)
    {
        _loading = true;
        _settings = settings;
        WakeBox.IsChecked = settings.WakeOnLan;
        WakeMinutesBox.Text = settings.WakeWaitMinutes.ToString();
        RetryBox.IsChecked = settings.Retry;
        AttemptsBox.Text = settings.RetryAttempts.ToString();
        IntervalBox.Text = settings.RetryIntervalMinutes.ToString();
        UpdateEnabled();
        _loading = false;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized) return;
        _settings.WakeOnLan = WakeBox.IsChecked == true;
        if (int.TryParse(WakeMinutesBox.Text, out var wait)) _settings.WakeWaitMinutes = wait;
        _settings.Retry = RetryBox.IsChecked == true;
        if (int.TryParse(AttemptsBox.Text, out var attempts)) _settings.RetryAttempts = attempts;
        if (int.TryParse(IntervalBox.Text, out var interval)) _settings.RetryIntervalMinutes = interval;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        WakeMinutesBox.IsEnabled = WakeBox.IsChecked == true;
        AttemptsBox.IsEnabled = IntervalBox.IsEnabled = RetryBox.IsChecked == true;
    }
}
