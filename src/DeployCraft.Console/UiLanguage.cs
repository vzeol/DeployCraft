using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace DeployCraft.Console;

/// <summary>
/// Language of the console texts (Préférences › Interface): French by default, or English.
/// The texts live in Resources\Strings.resx (French) and Strings.en.resx (English). Only the texts change:
/// dates and numbers keep their usual format. The choice is per Windows user and applies at the next start.
/// </summary>
public static class UiLanguage
{
    public const string French = "fr", English = "en";

    /// <summary>Language of this run of the console.</summary>
    public static string Current { get; private set; } = French;

    /// <summary>At startup, before the first window.</summary>
    public static void Init()
    {
        Current = Saved();
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Saved choice; French if none (or unreadable).</summary>
    public static string Saved() => ConsolePrefs.Get<string>("Language") == English ? English : French;

    /// <summary>Saves the choice for the next start.</summary>
    public static void Set(string choice) => ConsolePrefs.Set("Language", choice == English ? English : French);
}

/// <summary>Console texts in the current language. Every key exists in both .resx files (checked by the tests).</summary>
public static class L
{
    private static readonly ResourceManager Texts = new("DeployCraft.Console.Resources.Strings", typeof(L).Assembly);

    /// <summary>Text of a key; the key itself between brackets if it is missing, so the gap shows on screen.</summary>
    public static string Get(string key) => Texts.GetString(key) ?? $"[{key}]";

    /// <summary>Text with placeholders {0}, {1}… filled in.</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>Console text in XAML: <c>Text="{l:T Settings_Theme}"</c> (xmlns:l="clr-namespace:DeployCraft.Console").</summary>
[MarkupExtensionReturnType(typeof(string))]
public class T(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.Get(Key);
}
