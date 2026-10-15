using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Variables $(Nom) : remplacement au lancement, $(Poste) par poste, sous-expressions PowerShell intactes, saisie.</summary>
public sealed class VariableTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Depot"] = @"\\SERVEUR-01\Logiciels",
        ["Boucle"] = "$(Depot)",
    };

    [Theory]
    [InlineData(@"$(Depot)\outil.msi", @"\\SERVEUR-01\Logiciels\outil.msi")]
    [InlineData(@"$(depot)\outil.msi", @"\\SERVEUR-01\Logiciels\outil.msi")] // insensible à la casse
    [InlineData("Write-Output $(Get-Date)", "Write-Output $(Get-Date)")]    // sous-expression PowerShell
    [InlineData("$($x.Nom) $(Inconnue)", "$($x.Nom) $(Inconnue)")]
    [InlineData("$(Boucle)", "$(Depot)")]                                   // pas de remplacement récursif
    public void OnlyKnownNamesAreReplaced(string text, string expected) =>
        Assert.Equal(expected, VariableText.Replace(text, Values));

    [Fact]
    public void Resolve_ReplacesFilesArgumentsCommandsAndConditions_ButKeepsTheComputer()
    {
        var package = new Package
        {
            Conditions = new Conditions { File = PresenceCondition.Exists, FilePath = @"$(Depot)\licence.txt" },
            Steps =
            [
                new PackageStep { Type = StepType.Install, File = @"$(Depot)\outil.msi", Arguments = "LOG=C:\\Logs\\$(Date)-$(Poste).log" },
                new PackageStep
                {
                    Type = StepType.Command, Command = "echo $(Serveur)",
                    Conditions = new Conditions { Registry = PresenceCondition.Exists, RegistryKey = @"HKLM\SOFTWARE\$(Serveur)" },
                },
            ],
        };
        var values = VariableText.AtLaunch([new Variable { Name = "Depot", Value = @"\\SERVEUR-01\Logiciels" }], new DateTime(2026, 3, 9, 7, 5, 0), "SERVEUR-01");

        var resolved = package.Resolve(new DeploymentDefaults(), variables: values);

        Assert.Equal(@"\\SERVEUR-01\Logiciels\licence.txt", resolved.Conditions.FilePath);
        Assert.Equal(@"\\SERVEUR-01\Logiciels\outil.msi", resolved.Steps[0].File);
        Assert.Equal(@"LOG=C:\Logs\2026-03-09-$(Poste).log", resolved.Steps[0].Arguments);
        Assert.Equal("echo SERVEUR-01", resolved.Steps[1].Command);
        Assert.Equal(@"HKLM\SOFTWARE\SERVEUR-01", resolved.Steps[1].Conditions.RegistryKey);
        Assert.Equal(@"$(Depot)\outil.msi", package.Steps[0].File); // le paquet enregistré ne change pas
        Assert.Equal("07-05", values[VariableText.Time]);
    }

    [Fact]
    public void Computer_IsReplacedPerComputer_InStepsAndInheritedConditions()
    {
        var package = new Package
        {
            Steps =
            [
                new PackageStep
                {
                    Command = "echo $(Poste)",
                    ExtraConditions = [new ConditionGroup { Label = "x", Conditions = new Conditions { FilePath = @"C:\$(Poste).txt" } }],
                },
            ],
        };

        var forPc = TargetSession.WithComputer(package, "PC-TEST-01");

        Assert.Equal("echo PC-TEST-01", forPc.Steps[0].Command);
        Assert.Equal(@"C:\PC-TEST-01.txt", forPc.Steps[0].ExtraConditions![0].Conditions.FilePath);
        Assert.Equal("echo $(Poste)", package.Steps[0].Command);
    }

    [Theory]
    [InlineData("Depot", null)]
    [InlineData("Mon_Depot2", null)]
    [InlineData("Get-Date", "pas d'espace ni de « - »")]
    [InlineData("Mon depot", "pas d'espace ni de « - »")]
    [InlineData("poste", "variable système")]
    [InlineData("", "doit avoir un nom")]
    public void Names_AreLettersDigitsAndUnderscores_AndNotSystemNames(string name, string? error)
    {
        var result = VariableText.Validate([new Variable { Name = name, Value = "x" }]);

        if (error == null) Assert.Null(result);
        else Assert.Contains(error, result);
    }

    [Fact]
    public void Names_MustBeUnique_WhateverTheCase() =>
        Assert.Contains("deux fois", VariableText.Validate([new Variable { Name = "Depot" }, new Variable { Name = "DEPOT" }]));

    [Fact]
    public void Store_ReplacesAllVariables_AndKeepsThemSorted()
    {
        var store = new Store(Path.Combine(_dir, "test.db"));
        store.SaveVariables([new Variable { Name = "Zeta", Value = "1" }, new Variable { Name = "Alpha", Value = "2", Description = "test" }]);
        store.SaveVariables([.. store.ListVariables().Where(v => v.Name != "Zeta"), new Variable { Name = "Beta", Value = "3" }]);

        var list = store.ListVariables();

        Assert.Equal(["Alpha", "Beta"], list.Select(v => v.Name));
        Assert.Equal("test", list[0].Description);
    }
}
