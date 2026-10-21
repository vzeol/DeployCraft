using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DeployCraft.Runner;
using DeployCraft.Server;

namespace DeployCraft.Tests;

/// <summary>Messages of the service (Texts\Msg.resx, Msg.en.resx) and of the runner (RunnerTexts).</summary>
public partial class ServiceTextsTests
{
    private static readonly string Src = Path.Combine(RepoRoot(), "src");

    [Fact]
    public void Service_BothLanguagesHaveTheSameKeysAndPlaceholders() =>
        AssertSameKeysAndPlaceholders(Resx("Msg.resx"), Resx("Msg.en.resx"));

    [Fact]
    public void Service_EveryKeyUsedExists_AndEveryKeyIsUsed() =>
        AssertKeysMatchSources(Resx("Msg.resx").Keys, ["DeployCraft.Server"], MsgPattern());

    [Fact]
    public void Runner_BothLanguagesHaveTheSameKeysAndPlaceholders() =>
        AssertSameKeysAndPlaceholders(RunnerTexts.French, RunnerTexts.English);

    [Fact]
    public void Runner_EveryKeyUsedExists_AndEveryKeyIsUsed() =>
        AssertKeysMatchSources(RunnerTexts.French.Keys, ["DeployCraft.Runner"], RunnerPattern());

    [Fact]
    public void Runner_ReadsTheLanguageOfTheJob()
    {
        var key = RunnerTexts.French.Keys.First();
        try
        {
            RunnerTexts.Language = "en";
            Assert.Equal(RunnerTexts.English[key], RunnerTexts.Get(key));
            RunnerTexts.Language = "fr";
            Assert.Equal(RunnerTexts.French[key], RunnerTexts.Get(key));
            Assert.Equal("[Nope]", RunnerTexts.Get("Nope"));
        }
        finally { RunnerTexts.Language = "fr"; }
    }

    [Theory]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("fr-FR", "fr")]
    [InlineData("de-DE, fr;q=0.5", "fr")]
    [InlineData("de-DE", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Supported_PicksFrenchOrEnglish(string? header, string? expected) => Assert.Equal(expected, Msg.Supported(header));

    [Fact]
    public void Get_ReadsTheCurrentLanguage()
    {
        var key = Resx("Msg.resx").Keys.First();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal(Resx("Msg.en.resx")[key], Msg.Get(key));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            Assert.Equal(Resx("Msg.resx")[key], Msg.Get(key));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    private static void AssertSameKeysAndPlaceholders(IReadOnlyDictionary<string, string> fr, IReadOnlyDictionary<string, string> en)
    {
        Assert.Empty(fr.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(fr.Keys));
        Assert.Empty(fr.Where(t => !Placeholders(t.Value).SetEquals(Placeholders(en[t.Key]))).Select(t => t.Key));
    }

    /// <summary>Keys read in code exist; every key appears in code (as a direct call or a string literal).</summary>
    private static void AssertKeysMatchSources(IEnumerable<string> keys, string[] projects, Regex calls)
    {
        var code = string.Join("\n", projects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(Src, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));
        var known = keys.ToHashSet();

        var missing = calls.Matches(code).Select(m => m.Groups["key"].Value).Except(known);
        var unused = known.Where(k => !code.Contains($"\"{k}\"")).ToList();

        Assert.Empty(missing);
        Assert.Empty(unused);
    }

    private static Dictionary<string, string> Resx(string file) =>
        XDocument.Load(Path.Combine(Src, "DeployCraft.Server", "Texts", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DeployCraft.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("DeployCraft.slnx not found");
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"Msg\.(Get|Format)\(""(?<key>\w+)""")]
    private static partial Regex MsgPattern();

    [GeneratedRegex(@"RunnerTexts\.(Get|Format)\(""(?<key>\w+)""")]
    private static partial Regex RunnerPattern();
}
