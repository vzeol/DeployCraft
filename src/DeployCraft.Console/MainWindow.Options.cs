using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Options, help, tools and new version of DeployCraft.</summary>
public partial class MainWindow
{
    // ---------- Options et aide ----------

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
        // Les Préférences changées se voient tout de suite dans les Détails du paquet affiché.
        try
        {
            await RefreshPackagesAsync();
            if (SelectedPackage is { } p && _view == View.Package) ShowPackage(p);
        }
        catch (ApiException) { /* déjà signalé dans le bandeau */ }
    }

    private async void Credentials_Click(object sender, RoutedEventArgs e)
    {
        new CredentialsWindow { Owner = this }.ShowDialog();
        try
        {
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private WelcomeWindow? _welcome;

    /// <summary>Assistant de démarrage, non modal : une seule fenêtre, ramenée devant si elle est déjà ouverte.</summary>
    private void Welcome_Click(object sender, RoutedEventArgs e)
    {
        if (_welcome != null)
        {
            _welcome.Activate();
            return;
        }
        _welcome = new WelcomeWindow(this);
        _welcome.Closed += async (_, _) =>
        {
            _welcome = null;
            await RefreshAsync(packages: true);
        };
        _welcome.Show();
    }

    /// <summary>Affiche la bibliothèque de paquets (depuis l'assistant de démarrage).</summary>
    public void OpenLibrary() => _libraryNode.IsSelected = true;
    /// <summary>F1 : l'aide, ouverte sur le sujet de la vue affichée.</summary>
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.Show(this, _view switch
    {
        View.Deployments => "suivi",
        View.Package => "paquets",
        View.Schedules => "planifications",
        View.Library => "bibliotheque",
        _ => "accueil",
    });

    private void ConsoleUsers_Click(object sender, RoutedEventArgs e) => new ConsoleUsersWindow { Owner = this }.ShowDialog();
    private void BackgroundService_Click(object sender, RoutedEventArgs e) => new ServiceWindow { Owner = this }.ShowDialog();

    private void AdminCmd_Click(object sender, RoutedEventArgs e) => StartElevated("cmd.exe");
    private void AdminPowerShell_Click(object sender, RoutedEventArgs e) => StartElevated("powershell.exe");

    private void StartElevated(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception) { /* demande d'élévation refusée */ }
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    // ---------- Nouvelle version ----------

    private UpdateInfo? _update;
    private string? _serverMachine;
    private bool _installing;

    /// <summary>
    /// L'installeur met à jour le service et la console ensemble : il ne se lance que depuis la console du serveur,
    /// et seulement si la release fournit son empreinte.
    /// </summary>
    private bool CanInstallHere(UpdateInfo update) =>
        update.SetupUrl != null && string.Equals(_serverMachine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private void ShowUpdate(UpdateInfo? update)
    {
        _update = update;
        if (update != null)
        {
            var here = CanInstallHere(update);
            UpdateText.Text = $"DeployCraft {update.Version} est disponible (vous avez la {UpdateCheck.CurrentVersion})." +
                              (here ? "" : " Installez-la sur le serveur, en administrateur : le service et la console sont mis à jour ensemble.");
            UpdateInstall.Visibility = here && !_installing ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateBar.Visibility = update == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Au démarrage (et par Aide) : propose d'installer la nouvelle version tout de suite. Non = rappel au prochain démarrage.</summary>
    private async Task ProposeUpdateAsync(UpdateInfo update)
    {
        if (!CanInstallHere(update) || _installing) return;
        var running = 0;
        try
        {
            running = (await Api.Deployments()).Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
        }
        catch (ApiException) { /* rien de plus à signaler */ }
        var text = $"Une nouvelle version de DeployCraft est disponible : {update.Version} (vous avez la {UpdateCheck.CurrentVersion}).\n\n" +
                   "Voulez-vous l'installer maintenant ? La console se ferme, le service redémarre, puis la console se rouvre (une minute environ)." +
                   (running > 0 ? $"\n\nAttention : {running} déploiement(s) en cours ou en attente seront interrompus." : "");
        if (MessageDialog.Show(this, text, "Nouvelle version", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await InstallUpdateAsync(update);
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        _installing = true;
        ShowUpdate(update);
        string file;
        try
        {
            file = await UpdateCheck.DownloadSetupAsync(update, new Progress<int>(p => UpdateText.Text = $"Téléchargement de DeployCraft {update.Version}… {p} %"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _installing = false;
            ShowUpdate(update);
            MessageDialog.Show(this, $"Téléchargement de la nouvelle version impossible : {ex.Message}", "Nouvelle version", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            // Installation sans questions, avec sa barre de progression ; RELAUNCH rouvre la console à la fin.
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _installing = false;
            ShowUpdate(update);
            MessageDialog.Show(this, "Installation annulée : l'autorisation administrateur est nécessaire pour mettre à jour le service.",
                "Nouvelle version", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Application.Current.Shutdown();
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_update != null && CanInstallHere(_update) && !_installing) await ProposeUpdateAsync(_update);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var update = await UpdateCheck.FindAsync();
        ShowUpdate(update);
        if (update == null)
            MessageDialog.Show(this, $"Aucune nouvelle version trouvée (version {UpdateCheck.CurrentVersion}, ou pas d'accès à Internet).",
                "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Information);
        else await ProposeUpdateAsync(update);
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e)
    {
        if (_update is { Url.Length: > 0 } u) Process.Start(new ProcessStartInfo(u.Url) { UseShellExecute = true });
    }

    private void UpdateHide_Click(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
