using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Aide › À propos: console and service versions, license, project page.</summary>
public partial class AboutWindow : Window
{
    private static readonly string? Version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3);

    public AboutWindow()
    {
        InitializeComponent();
        ConsoleVersion.Text = AboutInfo.Version(Version);
        Notice.Text = AboutInfo.Notice;
        ProjectLink.NavigateUri = new Uri(AboutInfo.ProjectUrl);
        LicenseLink.NavigateUri = new Uri(AboutInfo.LicenseUrl);
        Loaded += async (_, _) => await LoadServiceAsync();
    }

    private async Task LoadServiceAsync()
    {
        try
        {
            var s = await Api.Status();
            ServiceVersion.Text = AboutInfo.ServiceText(s.Version, s.Machine, null);
            ConnectedAs.Text = s.ConnectedAs ?? "";
        }
        catch (ApiException ex)
        {
            ServiceVersion.Text = AboutInfo.ServiceText(null, null, ex.AccessDenied ? "Accès refusé" : "Service injoignable");
            ConnectedAs.Text = Environment.UserDomainName + "\\" + Environment.UserName;
        }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { NavigateUri: { } uri })
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(AboutInfo.Summary(Version, ServiceVersion.Text, ConnectedAs.Text,
            Environment.OSVersion.VersionString, RuntimeInformation.FrameworkDescription));
}
