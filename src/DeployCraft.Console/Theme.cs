using System.Windows;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>
/// Thème clair / sombre de la console (Préférences › Interface).
/// Les contrôles standard suivent le thème Fluent de WPF ; nos couleurs viennent de Themes\Clair.xaml
/// ou Themes\Sombre.xaml, échangés à chaud (toujours les utiliser en DynamicResource).
/// Le choix est propre à chaque utilisateur Windows : %APPDATA%\DeployCraft\console.json.
/// </summary>
#pragma warning disable WPF0001 // ThemeMode est encore marqué « expérimental » dans .NET 10
public static class Theme
{
    public const string Light = "Clair", Dark = "Sombre", System = "Windows";

    private static ResourceDictionary? palette;

    /// <summary>Choix enregistré : Clair, Sombre ou Windows (suit le réglage de Windows).</summary>
    public static string Current { get; private set; } = Light;

    /// <summary>Au démarrage, avant la première fenêtre.</summary>
    public static void Init(Application app)
    {
        Current = Load();
        Apply(app, Current);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) });
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Current == System && e.Category == UserPreferenceCategory.General)
                app.Dispatcher.BeginInvoke(() => Apply(app, System));
        };
    }

    /// <summary>Change le thème tout de suite et l'enregistre.</summary>
    public static void Set(string choice)
    {
        Current = choice;
        Apply(Application.Current, choice);
        ConsolePrefs.Set("Theme", choice);
    }

    private static void Apply(Application app, string choice)
    {
        var dark = choice == Dark || (choice == System && WindowsUsesDark());
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        var next = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? "Sombre" : "Clair")}.xaml", UriKind.Relative) };
        if (palette != null) app.Resources.MergedDictionaries.Remove(palette);
        app.Resources.MergedDictionaries.Add(next);
        palette = next;
    }

    private static bool WindowsUsesDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Thème enregistré ; clair si aucun (ou illisible).</summary>
    private static string Load() => ConsolePrefs.Get<string>("Theme") is { } theme && theme is Dark or System ? theme : Light;
}
