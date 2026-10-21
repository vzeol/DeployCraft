using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeployCraft.Console;

/// <summary>One step of the wizard: done or not, and what its button does.</summary>
public record WelcomeStep(string Title, string Text, string Action, bool Done, bool Optional, Func<Task> Run)
{
    public string Mark => Done ? Ui.StatusIcon("Successful") : Optional ? Ui.IconSettings : ""; // checkmark, settings, empty circle
    public Brush MarkBrush => (Brush)Application.Current.Resources[Done ? "OkBrush" : Optional ? "MutedBrush" : "Accent"];
}

/// <summary>
/// First-start wizard (Aide › Assistant de démarrage): deployment credentials, library folder,
/// first package, preferences. Opens by itself as long as the server has neither credentials nor package.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly MainWindow _main;

    public WelcomeWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Loaded += async (_, _) => await LoadAsync();
        // Modeless: you can work in the console (library...) and come back; the state is re-read on return.
        Activated += async (_, _) =>
        {
            if (IsLoaded && !_loading) await LoadAsync();
        };
    }

    private bool _loading;

    /// <summary>The server has nothing configured yet: the wizard opens when the console starts.</summary>
    public static async Task<bool> NeededAsync()
    {
        try
        {
            return (await Api.Credentials()).Count == 0 && (await Api.Packages()).Count == 0;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var status = await Api.Status();
            var credentials = await Api.Credentials();
            var library = await Api.Library();
            var packages = await Api.Packages();

            ServiceWarning.Text = status.Elevated ? ""
                : L.Get("Welcome_ServiceWarning");
            ServiceWarning.Visibility = status.Elevated ? Visibility.Collapsed : Visibility.Visible;

            var steps = new List<WelcomeStep>
            {
                new(L.Get("Welcome_Step1Title"),
                    credentials.Count == 0
                        ? L.Get("Welcome_Step1TextNone")
                        : L.Format("Welcome_Step1Text", credentials.Count, (credentials.FirstOrDefault(c => c.IsDefault) ?? credentials[0]).Account),
                    credentials.Count == 0 ? L.Get("Welcome_Add") : L.Get("Welcome_Manage"), credentials.Count > 0, false,
                    () => { new CredentialsWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; }),
                new(L.Get("Welcome_Step2Title"),
                    library.DefaultFolder
                        ? L.Get("Welcome_Step2TextDefault")
                        : L.Format("Welcome_Step2Text", library.Folder),
                    L.Get("Welcome_Choose"), !library.DefaultFolder, false, ChooseLibraryFolderAsync),
                new(L.Get("Welcome_Step3Title"),
                    packages.Count == 0
                        ? L.Get("Welcome_Step3TextNone")
                        : L.Format("Welcome_Step3Text", packages.Count),
                    L.Get("Welcome_Library"), packages.Count > 0, false,
                    () => { _main.OpenLibrary(); _main.Activate(); return Task.CompletedTask; }),
                new(L.Get("Welcome_Step4Title"),
                    L.Get("Welcome_Step4Text"),
                    L.Get("Welcome_Open"), false, true,
                    () => { new SettingsWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; }),
            };
            Steps.ItemsSource = steps;
            var done = steps.Count(s => s.Done);
            var required = steps.Count(s => !s.Optional);
            Progress.Text = done == required ? L.Get("Welcome_AllReady") : L.Format("Welcome_Progress", done, required);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
        finally { _loading = false; }
    }

    private async Task ChooseLibraryFolderAsync()
    {
        var info = await Api.Library();
        if (Ui.PickLibraryFolder(this, info.Folder) is not { } folder) return;
        await Api.SetLibraryFolder(folder);
    }

    // The window is modeless: IsCancel only closes dialogs, so Close (and Escape) must close it explicitly.
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Step_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not WelcomeStep step) return;
        try
        {
            await step.Run();
            if (IsLoaded) await LoadAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
