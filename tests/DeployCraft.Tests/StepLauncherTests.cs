using DeployCraft.Runner;
using DeployCraft.Runner.Steps;

namespace DeployCraft.Tests;

/// <summary>Each step type builds its own process on the computer.</summary>
public sealed class StepLauncherTests : IDisposable, IStepHost
{
    private readonly string _exec = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly List<string> _log = [];
    private int _pulls;

    string IStepHost.ExecDir => _exec;
    void IStepHost.Pull(JobStep step) => _pulls++;
    void IStepHost.Log(string message) => _log.Add(message);
    public void Dispose() => Directory.Delete(_exec, recursive: true);

    private System.Diagnostics.ProcessStartInfo Build(JobStep step, int index = 0) => StepLaunchers.For(step.Type).Build(this, index, step);

    [Theory]
    [InlineData("install")]
    [InlineData("Command")]
    [InlineData("POWERSHELL")]
    [InlineData("copyfiles")]
    [InlineData("reboot")]
    public void EveryProcessStepType_HasALauncher(string type) => Assert.NotNull(StepLaunchers.For(type));

    [Theory]
    [InlineData("pause")]
    [InlineData("message")]
    [InlineData("unknown")]
    public void OtherTypes_HaveNoLauncher(string type) => Assert.Throws<NotSupportedException>(() => StepLaunchers.For(type));

    [Fact]
    public void Command_RunsThroughCmd()
    {
        var psi = Build(new JobStep { Type = "command", Command = "hostname" });

        Assert.EndsWith("cmd.exe", psi.FileName);
        Assert.Equal("/c hostname", psi.Arguments);
    }

    [Fact]
    public void PowerShell_WritesTheScriptWithABom()
    {
        var psi = Build(new JobStep { Type = "powershell", Command = "Write-Output é", Arguments = "-Verbose" }, index: 2);

        var script = Path.Combine(_exec, "step3.ps1");
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(script).Take(3));
        Assert.Contains($"-File \"{script}\" -Verbose", psi.Arguments);
    }

    [Fact]
    public void Install_MissingFile_IsAnError() =>
        Assert.Throws<FileNotFoundException>(() => Build(new JobStep { Type = "install", Command = "app.msi", CommandLine = "msiexec /i app.msi" }));

    [Fact]
    public void Install_RunsTheCommandLineAndPullsInPullMode()
    {
        File.WriteAllText(Path.Combine(_exec, "app.msi"), "");
        File.WriteAllText(Path.Combine(_exec, InstallLog.FileName), "old log");

        var psi = Build(new JobStep { Type = "install", Command = "app.msi", CommandLine = "msiexec /i \"app.msi\" /qn", SourcePath = @"\SERVEUR-01\Partage\app.msi" });

        Assert.Equal("/s /c \"msiexec /i \"app.msi\" /qn\"", psi.Arguments);
        Assert.Equal(1, _pulls);
        Assert.False(File.Exists(Path.Combine(_exec, InstallLog.FileName)));
    }

    [Fact]
    public void CopyFiles_WithoutDestination_IsAnError() =>
        Assert.Throws<ArgumentException>(() => Build(new JobStep { Type = "copyfiles", Command = "data" }));

    [Fact]
    public void CopyFiles_RunsRobocopy()
    {
        var psi = Build(new JobStep { Type = "copyfiles", Command = "data", CopyDestination = @"C:\Data" });

        Assert.StartsWith("/c robocopy.exe ", psi.Arguments);
    }

    [Fact]
    public void Reboot_SchedulesTheRestart()
    {
        var psi = Build(new JobStep { Type = "reboot", Seconds = 60 });

        Assert.EndsWith("shutdown.exe", psi.FileName);
        Assert.Equal(Shutdown.Arguments(false, 60, null), psi.Arguments);
    }
}
