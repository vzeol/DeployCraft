using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Avertissements d'une étape dans la fenêtre du paquet.</summary>
public class StepWarningsTests
{
    private const string Share = @"\\SERVEUR-01\Logiciels\";

    private static PackageStep Install(string file, string? args = null) =>
        new() { Type = StepType.Install, File = file, Arguments = args };

    [Fact]
    public void Exe_WithoutParameters_IsFlagged_UnlessTheLineIsCustom()
    {
        Assert.Single(StepWarnings.For(Install(Share + "setup.exe"), CopyMode.Push, []));
        Assert.Empty(StepWarnings.For(Install(Share + "setup.exe", "/S"), CopyMode.Push, []));
        var custom = Install(Share + "setup.exe");
        custom.CustomCommandLine = "setup.exe /S";
        Assert.Empty(StepWarnings.For(custom, CopyMode.Push, []));
        Assert.Empty(StepWarnings.For(Install(Share + "app.msi"), CopyMode.Push, []));
    }

    [Fact]
    public void MissingFiles_AreFlagged_IncludingAdditionalFiles()
    {
        var step = Install(Share + "app.msi");
        step.AdditionalFiles = Share + "reglages.cfg\n" + Share + "licence.txt";

        var warnings = StepWarnings.For(step, CopyMode.Push, [Share + "LICENCE.TXT"]);

        Assert.Equal([$"Introuvable depuis le serveur : {Share}licence.txt"], warnings);
    }

    [Fact]
    public void PullMode_FlagsLocalPaths()
    {
        var step = new PackageStep { Type = StepType.CopyFiles, CopySource = @"C:\Sources\fond.jpg" };
        Assert.Single(StepWarnings.For(step, CopyMode.Pull, []));
        Assert.Empty(StepWarnings.For(step, CopyMode.Push, []));
        Assert.Empty(StepWarnings.For(Install(Share + "app.msi"), CopyMode.Pull, []));
    }

    [Fact]
    public void DisabledStep_HasNoWarning()
    {
        var step = Install(@"C:\absent\setup.exe");
        step.Enabled = false;
        Assert.Empty(StepWarnings.Paths(step));
        Assert.Empty(StepWarnings.For(step, CopyMode.Pull, [@"C:\absent\setup.exe"]));
    }
}
