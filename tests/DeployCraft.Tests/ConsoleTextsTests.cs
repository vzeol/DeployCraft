extern alias ConsoleApp;

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConsoleApp::DeployCraft.Console;

namespace DeployCraft.Tests;

/// <summary>Console texts (Resources\Strings.resx in French, Strings.en.resx in English).</summary>
public partial class ConsoleTextsTests
{
    /// <summary>
    /// Tests read the console texts in French, whatever the language of the computer running them (the CI
    /// runs in English).
    /// </summary>
    [ModuleInitializer]
    internal static void UseFrench()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
    }

    private static readonly string ConsoleDir = Path.Combine(RepoRoot(), "src", "DeployCraft.Console");

    [Fact]
    public void BothLanguages_HaveTheSameKeys()
    {
        var fr = Texts("Strings.resx");
        var en = Texts("Strings.en.resx");

        Assert.Empty(fr.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(fr.Keys));
    }

    [Fact]
    public void BothLanguages_HaveTheSamePlaceholders()
    {
        var en = Texts("Strings.en.resx");
        var different = Texts("Strings.resx")
            .Where(t => en.TryGetValue(t.Key, out var english) && !Placeholders(t.Value).SetEquals(Placeholders(english)))
            .Select(t => t.Key);

        Assert.Empty(different);
    }

    [Fact]
    public void EveryKeyUsed_Exists()
    {
        var missing = UsedKeys().Except(Texts("Strings.resx").Keys);

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryKey_IsUsed()
    {
        // A key can also be chosen in code (cond ? "A_One" : "A_Many"): any string literal naming it counts.
        var sources = string.Join("\n", ConsoleSources().Select(File.ReadAllText));
        var unused = Texts("Strings.resx").Keys.Where(k => !sources.Contains($"T {k}}}") && !sources.Contains($"\"{k}\""));

        Assert.Empty(unused);
    }

    [Fact]
    public void Get_ReadsTheCurrentLanguage()
    {
        Assert.Equal("Thème", L.Get("Settings_Theme"));
        Assert.Equal("[Nope]", L.Get("Nope"));

        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("Theme", L.Get("Settings_Theme"));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    private static Dictionary<string, string> Texts(string file) =>
        XDocument.Load(Path.Combine(ConsoleDir, "Resources", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];

    /// <summary>Keys read by the console: {local:T Key} in XAML, L.Get("Key") / L.Format("Key", …) in C#.</summary>
    private static HashSet<string> UsedKeys() =>
        [.. ConsoleSources().SelectMany(f => KeyPattern().Matches(File.ReadAllText(f))).Select(m => m.Groups["key"].Value)];

    private static IEnumerable<string> ConsoleSources() =>
        Directory.EnumerateFiles(ConsoleDir, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DeployCraft.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("DeployCraft.slnx not found");
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{local:T (?<key>\w+)\}|L\.(Get|Format)\(""(?<key>\w+)""")]
    private static partial Regex KeyPattern();
}
