using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DeployCraft.Console;

/// <summary>Options › Service en arrière-plan : état du service, redémarrage, compte Windows sous lequel il tourne.</summary>
public partial class ServiceWindow : Window
{
    private ServiceState? _state;

    public ServiceWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _state = await Api.Service();
        }
        catch (ApiException ex)
        {
            StateIcon.Text = ""; // croix
            StateIcon.Foreground = (Brush)FindResource("KoBrush");
            StateText.Text = "Service injoignable";
            Hint.Text = ex.Message;
            RestartButton.IsEnabled = false;
            ChangeLink.Visibility = Visibility.Collapsed;
            return;
        }

        var info = _state.Info;
        StateIcon.Text = info.Elevated ? "" : ""; // coche, ou avertissement
        StateIcon.Foreground = (Brush)FindResource(info.Elevated ? "OkBrush" : "WarnBrush");
        StateText.Text = $"En marche depuis le {Ui.LocalTime(info.StartedUtc)}" + (info.IsService ? "" : " (lancé à la main, pas en service Windows)");
        ServicesLink.Visibility = _state.Local ? Visibility.Visible : Visibility.Collapsed;
        AccountText.Text = info.LocalSystem ? $"Système local ({info.Account})" : info.Account;

        var canChange = _state.CanEdit && _state.Local && info.IsService;
        RestartButton.IsEnabled = _state.CanEdit && info.IsService;
        ChangeLink.Visibility = canChange ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = !info.Elevated ? "Ce compte n'est pas administrateur du serveur : les déploiements échoueront. Choisissez Système local ou un compte administrateur."
            : !_state.CanEdit ? "Seuls les administrateurs de ce serveur peuvent redémarrer le service ou changer son compte."
            : !info.IsService ? "Instance lancée à la main : elle se redémarre et se configure en dehors de DeployCraft."
            : !_state.Local ? "Le compte se change depuis la console du serveur : le mot de passe ne passe pas par le réseau."
            : "";
    }

    private void OpenServices_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageDialog.Show(this, "Redémarrer le service DeployCraft ? Les déploiements en cours seront interrompus ; la console se reconnecte d'elle-même.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.RestartService();
            Close();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private void ChangeAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_state == null) return;
        if (new ServiceAccountWindow(_state.Info.Account, _state.Info.LocalSystem) { Owner = this }.ShowDialog() == true) Close();
    }

    private void ConsoleUsers_Click(object sender, RoutedEventArgs e) => new ConsoleUsersWindow { Owner = this }.ShowDialog();
}
