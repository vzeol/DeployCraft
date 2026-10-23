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
            if (SelectedPackage is { } p && _view == MainView.Package) ShowPackage(p);
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
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.Show(this, _core.HelpTopic);

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

    private void ShowUpdate()
    {
        if (_updates.Update != null)
        {
            UpdateText.Text = _updates.AvailableText;
            UpdateInstall.Visibility = _updates.ShowInstall ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateBar.Visibility = _updates.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>At startup (and via Help): offers to install the new version right away. No = reminder at the next startup.</summary>
    private async Task ProposeUpdateAsync(UpdateInfo update)
    {
        var text = await _updates.ProposalAsync(update);
        if (text == null) return;
        if (MessageDialog.Show(this, text, L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await InstallUpdateAsync(update);
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        _updates.BeginInstall(update);
        ShowUpdate();
        string file;
        try
        {
            file = await UpdateCheck.DownloadSetupAsync(update, new Progress<int>(p => UpdateText.Text = MainWindowUpdates.DownloadText(update, p)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _updates.InstallFailed(update);
            ShowUpdate();
            MessageDialog.Show(this, MainWindowUpdates.DownloadFailedText(ex), L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            // Silent install, with its progress bar; RELAUNCH reopens the console at the end.
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _updates.InstallFailed(update);
            ShowUpdate();
            MessageDialog.Show(this, L.Get("MainWindow_UpdateInstallCancelled"),
                L.Get("MainWindow_NewVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Application.Current.Shutdown();
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Update is { } update) await ProposeUpdateAsync(update);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var update = await _updates.CheckAsync();
        ShowUpdate();
        if (update == null)
            MessageDialog.Show(this, _updates.NoneFoundText,
                L.Get("MainWindow_UpdateTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        else await ProposeUpdateAsync(update);
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Update is { Url.Length: > 0 } u) Process.Start(new ProcessStartInfo(u.Url) { UseShellExecute = true });
    }

    private void UpdateHide_Click(object sender, RoutedEventArgs e)
    {
        _updates.Hide();
        UpdateBar.Visibility = Visibility.Collapsed;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
