using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Options, help, tools and new version of DeployCraft.</summary>
public partial class MainWindow
{
    // ---------- Options and help ----------

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
        // Changed Preferences show up immediately in the Details of the displayed package.
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

    /// <summary>Startup wizard, non-modal: a single window, brought to the front if already open.</summary>
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

    /// <summary>Shows the package library (from the startup wizard).</summary>
    public void OpenLibrary() => _libraryNode.IsSelected = true;
    /// <summary>F1: help, opened on the topic of the displayed view.</summary>
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

    // ---------- New version ----------

    private UpdateInfo? _update;
    private string? _serverMachine;
    private bool _installing;

    /// <summary>
    /// The installer updates the service and the console together: it only starts from the server's console,
    /// and only if the release provides its fingerprint.
    /// </summary>
    private bool CanInstallHere(UpdateInfo update) =>
        update.SetupUrl != null && string.Equals(_serverMachine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private void ShowUpdate(UpdateInfo? update)
    {
        _update = update;
        if (update != null)
        {
            var here = CanInstallHere(update);
            UpdateText.Text = L.Format("MainWindow_UpdateAvailable", update.Version, UpdateCheck.CurrentVersion) +
                              (here ? "" : " " + L.Get("MainWindow_UpdateInstallOnServer"));
            UpdateInstall.Visibility = here && !_installing ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateBar.Visibility = update == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>At startup (and via Help): offers to install the new version right away. No = reminder at the next startup.</summary>
    private async Task ProposeUpdateAsync(UpdateInfo update)
    {
        if (!CanInstallHere(update) || _installing) return;
        var running = 0;
        try
        {
            running = (await Api.Deployments()).Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
        }
        catch (ApiException) { /* rien de plus à signaler */ }
        var text = L.Format("MainWindow_UpdateProposal", update.Version, UpdateCheck.CurrentVersion) +
                   (running > 0 ? "\n\n" + L.Format("MainWindow_UpdateRunningWarning", running) : "");
        if (MessageDialog.Show(this, text, L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await InstallUpdateAsync(update);
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        _installing = true;
        ShowUpdate(update);
        string file;
        try
        {
            file = await UpdateCheck.DownloadSetupAsync(update, new Progress<int>(p => UpdateText.Text = L.Format("MainWindow_UpdateDownloading", update.Version, p)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _installing = false;
            ShowUpdate(update);
            MessageDialog.Show(this, L.Format("MainWindow_UpdateDownloadFailed", ex.Message), L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            // Silent install, with its progress bar; RELAUNCH reopens the console at the end.
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _installing = false;
            ShowUpdate(update);
            MessageDialog.Show(this, L.Get("MainWindow_UpdateInstallCancelled"),
                L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageDialog.Show(this, L.Format("MainWindow_UpdateNoneFound", UpdateCheck.CurrentVersion),
                L.Get("MainWindow_UpdateTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        else await ProposeUpdateAsync(update);
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e)
    {
        if (_update is { Url.Length: > 0 } u) Process.Start(new ProcessStartInfo(u.Url) { UseShellExecute = true });
    }

    private void UpdateHide_Click(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
