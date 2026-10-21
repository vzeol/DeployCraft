using System.Windows;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>
/// Light / dark theme of the console (Préférences › Interface).
/// Standard controls follow WPF's Fluent theme; our colors come from Themes\Clair.xaml
/// or Themes\Sombre.xaml, swapped at run time (always use them as DynamicResource).
/// The choice is per Windows user: %APPDATA%\DeployCraft\console.json.
/// </summary>
#pragma warning disable WPF0001 // ThemeMode is still marked "experimental" in .NET 10
public static class Theme
{
    public const string Light = "Clair", Dark = "Sombre", System = "Windows";

    private static ResourceDictionary? palette;

    /// <summary>Saved choice: Clair, Sombre or Windows (follows the Windows setting).</summary>
    public static string Current { get; private set; } = Light;

    /// <summary>At startup, before the first window.</summary>
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

    /// <summary>Changes the theme right away and saves it.</summary>
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

    /// <summary>Saved theme; light if none (or unreadable).</summary>
    private static string Load() => ConsolePrefs.Get<string>("Theme") is { } theme && theme is Dark or System ? theme : Light;
}
