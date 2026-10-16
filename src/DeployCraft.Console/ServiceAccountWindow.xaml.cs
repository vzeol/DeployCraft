using System.Windows;

namespace DeployCraft.Console;

/// <summary>Choix du compte du service : Système local, ou un compte administrateur du serveur avec son mot de passe.</summary>
public partial class ServiceAccountWindow : Window
{
    private const string LocalSystem = "LocalSystem";

    public ServiceAccountWindow(string current, bool system)
    {
        InitializeComponent();
        if (!system) NameBox.Text = current;
        (system ? SystemBox : AccountBox).IsChecked = true;
    }

    private void Choice_Changed(object sender, RoutedEventArgs e)
    {
        if (AccountFields == null) return;
        AccountFields.IsEnabled = AccountBox.IsChecked == true;
        if (AccountFields.IsEnabled) NameBox.Focus();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var account = SystemBox.IsChecked == true ? LocalSystem : NameBox.Text.Trim();
        OkButton.IsEnabled = false;
        try
        {
            await Api.SetServiceAccount(account, SystemBox.IsChecked == true ? "" : PasswordBox.Password);
            MessageDialog.Show(this, "Compte enregistré : le service redémarre. La console se reconnecte d'elle-même dans quelques secondes.",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            OkButton.IsEnabled = true;
        }
    }
}
