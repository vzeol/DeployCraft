using DeployCraft.Runner;

namespace DeployCraft.Tests;

/// <summary>Conditions d'un paquet ou d'une étape, vérifiées par le runner sur un poste simulé.</summary>
public class ConditionsTests
{
    private sealed class FakeFacts : ITargetFacts
    {
        public string WindowsFamily { get; init; } = "11";
        public bool Is64Bit { get; init; } = true;
        public int PowerShellVersion { get; init; } = 5;
        public bool UserLoggedOn { get; init; }
        public HashSet<string> Paths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>« clé » ou « clé|valeur ».</summary>
        public HashSet<string> Registry { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool PathExists(string path) => Paths.Contains(path);

        public bool RegistryExists(string key, string value) =>
            Registry.Contains(string.IsNullOrEmpty(value) ? key : key + "|" + value);
    }

    [Fact]
    public void NoConditions_AreMet()
    {
        Assert.Null(ConditionCheck.Failure(null!, new FakeFacts()));
        Assert.Null(ConditionCheck.Failure(new JobConditions(), new FakeFacts()));
    }

    [Theory]
    [InlineData("11", true)]
    [InlineData("10", true)]
    [InlineData("server", false)]
    [InlineData("older", false)]
    public void WindowsVersion_MustBeInTheList(string family, bool met)
    {
        var c = new JobConditions { Windows = ["10", "11"] };
        Assert.Equal(met, ConditionCheck.Failure(c, new FakeFacts { WindowsFamily = family }) == null);
    }

    [Theory]
    [InlineData(22631, false, 10, "11")]
    [InlineData(19045, false, 10, "10")]
    [InlineData(20348, true, 10, "server")]
    [InlineData(9600, false, 6, "older")]
    public void Family_FromBuildNumber(int build, bool server, int major, string expected) =>
        Assert.Equal(expected, ConditionCheck.Family(build, server, major));

    [Fact]
    public void Architecture_IsChecked()
    {
        Assert.Null(ConditionCheck.Failure(new JobConditions { Architecture = "x64" }, new FakeFacts { Is64Bit = true }));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { Architecture = "x64" }, new FakeFacts { Is64Bit = false }));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { Architecture = "x86" }, new FakeFacts { Is64Bit = true }));
    }

    [Fact]
    public void PowerShell_MinimumVersion()
    {
        var c = new JobConditions { MinPowerShell = 5 };
        Assert.Null(ConditionCheck.Failure(c, new FakeFacts { PowerShellVersion = 5 }));
        Assert.Null(ConditionCheck.Failure(c, new FakeFacts { PowerShellVersion = 7 }));
        Assert.Contains("version 4", ConditionCheck.Failure(c, new FakeFacts { PowerShellVersion = 4 }));
        Assert.Contains("absent", ConditionCheck.Failure(c, new FakeFacts { PowerShellVersion = 0 }));
    }

    [Fact]
    public void LoggedOnUser_BothWays()
    {
        Assert.Null(ConditionCheck.Failure(new JobConditions { LoggedOn = true }, new FakeFacts { UserLoggedOn = true }));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { LoggedOn = true }, new FakeFacts { UserLoggedOn = false }));
        Assert.Null(ConditionCheck.Failure(new JobConditions { LoggedOn = false }, new FakeFacts { UserLoggedOn = false }));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { LoggedOn = false }, new FakeFacts { UserLoggedOn = true }));
    }

    [Fact]
    public void File_ExistsOrNot()
    {
        var facts = new FakeFacts { Paths = { @"C:\Program Files\Outil\outil.exe" } };
        Assert.Null(ConditionCheck.Failure(new JobConditions { FilePath = @"C:\Program Files\Outil\outil.exe", FileExists = true }, facts));
        Assert.Contains("existe déjà", ConditionCheck.Failure(new JobConditions { FilePath = @"C:\Program Files\Outil\outil.exe", FileExists = false }, facts));
        Assert.Null(ConditionCheck.Failure(new JobConditions { FilePath = @"C:\Autre\absent.exe", FileExists = false }, facts));
    }

    [Fact]
    public void File_ExpandsEnvironmentVariables()
    {
        var windir = Environment.GetEnvironmentVariable("windir")!;
        var facts = new FakeFacts { Paths = { Path.Combine(windir, "notepad.exe") } };
        Assert.Null(ConditionCheck.Failure(new JobConditions { FilePath = @"%windir%\notepad.exe", FileExists = true }, facts));
    }

    [Fact]
    public void Registry_KeyOrValue()
    {
        var facts = new FakeFacts { Registry = { @"HKLM\SOFTWARE\Editeur\Outil", @"HKLM\SOFTWARE\Editeur\Outil|Version" } };
        Assert.Null(ConditionCheck.Failure(new JobConditions { RegistryKey = @"HKLM\SOFTWARE\Editeur\Outil", RegistryExists = true }, facts));
        Assert.Null(ConditionCheck.Failure(new JobConditions { RegistryKey = @"HKLM\SOFTWARE\Editeur\Outil", RegistryValue = "Version", RegistryExists = true }, facts));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { RegistryKey = @"HKLM\SOFTWARE\Editeur\Outil", RegistryValue = "Autre", RegistryExists = true }, facts));
        Assert.NotNull(ConditionCheck.Failure(new JobConditions { RegistryKey = @"HKLM\SOFTWARE\Editeur\Outil", RegistryExists = false }, facts));
    }

    [Fact]
    public void AllConditions_MustBeMet()
    {
        var c = new JobConditions { Windows = ["11"], Architecture = "x64", LoggedOn = false };
        Assert.Null(ConditionCheck.Failure(c, new FakeFacts()));
        Assert.NotNull(ConditionCheck.Failure(c, new FakeFacts { UserLoggedOn = true }));
    }
}
