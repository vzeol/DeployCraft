using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Built-in help: text reading and search.</summary>
public class HelpContentTests
{
    private const string Sample = """
        ignoré avant le premier sujet
        ## Premiers pas {#accueil}
        Bienvenue.
        ### Avant de commencer
        - Ajouter un identifiant.

        ## Dépannage {#depannage}
        - Code 1618 : une autre installation est en cours.
        """;

    [Fact]
    public void Parse_ReadsTopicsHeadingsBulletsAndParagraphs()
    {
        var topics = HelpContent.Parse(Sample);

        Assert.Equal(["accueil", "depannage"], topics.Select(t => t.Id));
        Assert.Equal("Premiers pas", topics[0].Title);
        Assert.Equal(
        [
            new HelpContent.Block(HelpContent.BlockKind.Paragraph, "Bienvenue."),
            new HelpContent.Block(HelpContent.BlockKind.Heading, "Avant de commencer"),
            new HelpContent.Block(HelpContent.BlockKind.Bullet, "Ajouter un identifiant."),
        ], topics[0].Blocks);
    }

    [Fact]
    public void Matches_NeedsEveryWord_InTitleOrText()
    {
        var topics = HelpContent.Parse(Sample);

        Assert.True(topics[1].Matches("1618 installation"));
        Assert.True(topics[0].Matches("premiers"));
        Assert.False(topics[0].Matches("1618"));
    }

    [Fact]
    public void ShippedHelp_HasTheTopicsUsedByF1()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DeployCraft.Console", "Help", "aide.txt");
        var ids = HelpContent.Parse(File.ReadAllText(file)).Select(t => t.Id).ToList();

        Assert.Subset(ids.ToHashSet(), new HashSet<string> { "accueil", "suivi", "paquets", "planifications", "bibliotheque" });
    }
}
