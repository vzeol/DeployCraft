using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Rules of the "Deploy once" window that do not need the window.</summary>
public class DeployEditingTests
{
    [Fact]
    public void StepChoices_ReplaceNestedPackagesByTheirSteps()
    {
        var nested = new Package { Id = 2, Name = "Base", Steps = [new() { Name = "a" }, new() { Name = "b" }] };
        var first = new Package { Id = 1, Steps = [new() { Name = "" }, new() { Type = StepType.Nested, NestedPackageId = 2 }] };

        var choices = DeployEditing.StepChoices(first, id => id == 2 ? nested : null);

        Assert.Equal(["1", "2. Base › a", "3. Base › b"], choices.Select(c => c.Text));
        Assert.Equal([1, 2, 3], choices.Select(c => c.Number));
    }

    [Fact]
    public void StepChoices_MissingNestedPackage_KeepsThePackageAsIs()
    {
        var first = new Package { Id = 1, Steps = [new() { Name = "x" }, new() { Type = StepType.Nested, NestedPackageId = 9 }] };

        Assert.Equal(2, DeployEditing.StepChoices(first, _ => null).Count);
        Assert.Empty(DeployEditing.StepChoices(null, _ => null));
    }

    [Fact]
    public void Hints()
    {
        Assert.Contains("Système local", DeployEditing.RunAsHint([RunAsMode.DeployUser], hasCredentials: false));
        Assert.Contains("identifiant choisi", DeployEditing.RunAsHint([RunAsMode.DeployUser], hasCredentials: true));
        Assert.Equal("Chaque paquet garde son propre réglage.", DeployEditing.RunAsHint([RunAsMode.DeployUser, RunAsMode.LocalSystem], true));
        Assert.Contains("serveur copie", DeployEditing.CopyModeHint([CopyMode.Push]));
        Assert.Contains("partage", DeployEditing.CopyModeHint([CopyMode.Pull]));
    }

    [Theory]
    [InlineData(1, 0, "Aucune cible")]
    [InlineData(1, 3, "3 poste(s)")]
    [InlineData(2, 3, "2 paquets · 3 poste(s)")]
    public void CountText(int packages, int targets, string expected) => Assert.Equal(expected, DeployEditing.CountText(packages, targets));
}
