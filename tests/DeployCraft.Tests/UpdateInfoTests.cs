using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Nouvelle version publiée : comparaison de l'étiquette de la release et de la version installée.</summary>
public class UpdateInfoTests
{
    [Theory]
    [InlineData("v0.16.0", "0.15.0", true)]
    [InlineData("v0.15.1", "0.15.0", true)]
    [InlineData("v1.0.0", "0.15.0", true)]
    [InlineData("v0.15.0", "0.15.0", false)]
    [InlineData("v0.14.0", "0.15.0", false)]
    [InlineData("v0.15.0", "0.15.0-beta.1", true)]
    [InlineData("v1.0.0", "1.0.0-rc.1", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v1.0.0", "1.0.0+abc-def", false)]
    [InlineData("v1.0.0-rc.2", "1.0.0-rc.1", false)]
    [InlineData("pas-une-version", "0.15.0", false)]
    [InlineData("v0.16.0", null, false)]
    public void IsNewer(string? tag, string? current, bool expected) => Assert.Equal(expected, UpdateInfo.IsNewer(tag, current));

    [Fact]
    public void Parse_KeepsThreeNumbers()
    {
        Assert.Equal(new Version(0, 15, 0), UpdateInfo.Parse("v0.15"));
        Assert.Equal(new Version(0, 15, 2), UpdateInfo.Parse("V0.15.2-beta.1"));
    }

    private const string Sha = "ac48811775e258e253b83e72b8b1c2a2abdb97a32951768674068f2bd5d4105b";

    [Fact]
    public void FindSetup_TakesTheInstallerOfThisVersion_WithItsDigest()
    {
        List<UpdateInfo.Asset> assets =
        [
            new("DeployCraft-Setup-0.15.0.exe", "https://exemple.test/old.exe", $"sha256:{Sha}"),
            new("DeployCraft-Setup-0.16.0.exe", "https://exemple.test/new.exe", $"sha256:{Sha}"),
        ];
        var setup = UpdateInfo.FindSetup(assets, "0.16.0");
        Assert.NotNull(setup);
        Assert.Equal("https://exemple.test/new.exe", setup.Value.Url);
        Assert.Equal(Sha.ToUpperInvariant(), setup.Value.Sha256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:0123")]
    [InlineData("sha256:pas-une-empreinte")]
    public void FindSetup_WithoutAValidDigest_GivesNothing(string? digest) =>
        Assert.Null(UpdateInfo.FindSetup([new("DeployCraft-Setup-0.16.0.exe", "https://exemple.test/new.exe", digest)], "0.16.0"));

    [Fact]
    public void FindSetup_WithoutTheInstaller_GivesNothing() =>
        Assert.Null(UpdateInfo.FindSetup([new("Notes.txt", "https://exemple.test/notes.txt", $"sha256:{Sha}")], "0.16.0"));
}
