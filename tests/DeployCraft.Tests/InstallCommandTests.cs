using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Étape « Installer » : ligne de commande générée ou personnalisée, journal, fichiers additionnels.</summary>
public class InstallCommandTests
{
    private static PackageStep Install(string file, string? args = null) =>
        new() { Type = StepType.Install, File = @"\\SERVEUR-01\Logiciels\" + file, Arguments = args };

    [Theory]
    [InlineData(MsiOperation.Install, "/i")]
    [InlineData(MsiOperation.Repair, "/f")]
    [InlineData(MsiOperation.Uninstall, "/x")]
    public void Msi_Operation(MsiOperation operation, string option)
    {
        var step = Install("app.msi", "TRANSFORMS=ecole.mst");
        step.MsiOperation = operation;

        Assert.Equal($"msiexec.exe {option} \"app.msi\" ALLUSERS=1 /qn /norestart /log output.log TRANSFORMS=ecole.mst", InstallCommand.Generate(step));
    }

    [Theory]
    [InlineData(MsiRestart.Always, "msiexec.exe /i \"app.msi\" ALLUSERS=1 /forcerestart /log output.log")]
    [InlineData(MsiRestart.Prompt, "msiexec.exe /i \"app.msi\" ALLUSERS=1 /promptrestart /log output.log")]
    [InlineData(MsiRestart.NotSet, "msiexec.exe /i \"app.msi\" ALLUSERS=1 /log output.log")]
    public void Msi_RestartAndNotQuiet(MsiRestart restart, string expected)
    {
        var step = Install("app.msi");
        step.MsiRestart = restart;
        step.MsiQuiet = false;

        Assert.Equal(expected, InstallCommand.Generate(step));
    }

    [Theory]
    [InlineData("correctif.msp", "msiexec.exe /p \"correctif.msp\" /qn /norestart /log output.log")]
    [InlineData("maj.msu", "wusa.exe \"maj.msu\" /quiet /norestart")]
    [InlineData("script.ps1", "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"script.ps1\"")]
    [InlineData("script.vbs", "cscript.exe //NoLogo //B \"script.vbs\"")]
    [InlineData("reglages.reg", "reg.exe import \"reglages.reg\"")]
    [InlineData("installer.cmd", "\"installer.cmd\"")]
    [InlineData("setup.exe", "\"setup.exe\"")]
    public void OtherTypes(string file, string expected)
    {
        Assert.Equal(expected, InstallCommand.Generate(Install(file)));
    }

    [Fact]
    public void Msu_Uninstall_AndParameters()
    {
        var step = Install("maj.msu", "/warnrestart");
        step.MsiOperation = MsiOperation.Uninstall;

        Assert.Equal("wusa.exe /uninstall \"maj.msu\" /quiet /norestart /warnrestart", InstallCommand.Generate(step));
    }

    [Fact]
    public void Exe_KeepsItsParameters()
    {
        Assert.Equal("\"Mon Setup.exe\" /S", InstallCommand.Generate(Install("Mon Setup.exe", " /S ")));
    }

    [Fact]
    public void CustomCommandLine_ReplacesTheGeneratedOne()
    {
        var step = Install("app.msi");
        Assert.Equal(InstallCommand.Generate(step), InstallCommand.For(step));

        step.CustomCommandLine = "  msiexec.exe /i \"app.msi\" /qn  ";
        Assert.Equal("msiexec.exe /i \"app.msi\" /qn", InstallCommand.For(step));
    }

    [Fact]
    public void Log_IsAppendedToTheOutput_ThenDeleted()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, InstallLog.FileName), "MSI (s) Produit installé.", System.Text.Encoding.Unicode);

            var output = InstallLog.Append("", dir, 1000);

            Assert.Contains("Produit installé.", output);
            Assert.False(File.Exists(Path.Combine(dir, InstallLog.FileName)));
            Assert.Equal("sortie", InstallLog.Append("sortie", dir, 1000)); // plus de journal
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Log_TooLong_KeepsTheEnd()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, InstallLog.FileName), new string('a', 500) + "FIN", System.Text.Encoding.Unicode);

            var output = InstallLog.Append("", dir, 10);

            Assert.EndsWith("aaaaaaaFIN", output);
            Assert.Contains("[…]", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MsiErrorCodes_AreExplained_OnlyForWindowsInstallerFiles()
    {
        Assert.Contains("autre installation", InstallLog.ErrorMessage("app.msi", 1618));
        Assert.Contains("ne concerne pas ce poste", InstallLog.ErrorMessage("maj.msu", 1));
        Assert.Null(InstallLog.ErrorMessage("setup.exe", 1618));
        Assert.Null(InstallLog.ErrorMessage("app.msi", 4242));
    }

    [Fact]
    public void AdditionalFiles_PushedByTheServer_OrReadByThePostInPullMode()
    {
        var source = Directory.CreateTempSubdirectory().FullName;
        var exec = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var local = Path.Combine(source, "config.xml");
            File.WriteAllText(local, "<config />");
            var step = new PackageStep { Type = StepType.Command, AdditionalFiles = local + "\r\n\r\n" + @"\\SERVEUR-01\Partage\licence.txt" };
            var job = new RunnerJobStep { Name = "Commande" };

            TargetSession.StageAdditionalFiles(step, job, CopyMode.Pull, exec);

            Assert.True(File.Exists(Path.Combine(exec, "config.xml")));
            Assert.Equal([@"\\SERVEUR-01\Partage\licence.txt"], job.PullFiles!);
            Assert.Equal(["config.xml", "licence.txt"], job.AdditionalFiles!);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(exec, recursive: true);
        }
    }

    [Fact]
    public void AdditionalFiles_MissingFile_StopsThePreparation()
    {
        var step = new PackageStep { Type = StepType.Command, AdditionalFiles = @"C:\introuvable\fichier.cfg" };

        Assert.Throws<DeployException>(() => TargetSession.StageAdditionalFiles(step, new RunnerJobStep(), CopyMode.Push, Path.GetTempPath()));
    }

    [Fact]
    public void KeepFolder_UsesTheSanitizedPackageName()
    {
        Assert.Equal(@"%ProgramData%\DeployCraft\Installeurs\Bureautique 2026", TargetSession.KeepFolder("Bureautique: 2026"));
        Assert.Equal(@"%ProgramData%\DeployCraft\Installeurs\Paquet", TargetSession.KeepFolder("???"));
    }
}
