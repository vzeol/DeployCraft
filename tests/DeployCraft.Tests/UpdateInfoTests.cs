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
    [InlineData("v0.15.0", "0.15.0-beta.1", false)]
    [InlineData("pas-une-version", "0.15.0", false)]
    [InlineData("v0.16.0", null, false)]
    public void IsNewer(string? tag, string? current, bool expected) => Assert.Equal(expected, UpdateInfo.IsNewer(tag, current));

    [Fact]
    public void Parse_KeepsThreeNumbers()
    {
        Assert.Equal(new Version(0, 15, 0), UpdateInfo.Parse("v0.15"));
        Assert.Equal(new Version(0, 15, 2), UpdateInfo.Parse("V0.15.2-beta.1"));
    }
}
