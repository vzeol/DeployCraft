using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>"Redémarrer" step: runner instructions, shutdown.exe line, cut slices and computer shutdown.</summary>
public class RebootTests
{
    private const RunAsMode D = RunAsMode.DeployUser, S = RunAsMode.LocalSystem, U = RunAsMode.LoggedOnUser;

    [Fact]
    public void JobStep_CarriesDelayMessageAndShutdown_AndNeverRunsAsTheLoggedOnUser()
    {
        var step = new PackageStep { Type = StepType.Reboot, RebootDelaySeconds = 120, RebootMessage = "Mise à jour", RebootShutdownOnly = true };

        var job = TargetSession.ToJobStep(step, new Package(), U);

        Assert.Equal("reboot", job.Type);
        Assert.Equal(120, job.Seconds);
        Assert.Equal("Mise à jour", job.Command);
        Assert.True(job.ShutdownOnly);
        Assert.False(job.RunAsLoggedOnUser);
        Assert.False(TargetSession.ToJobStep(new PackageStep { Type = StepType.Command, Command = "echo", RebootShutdownOnly = true }, new Package(), S).ShutdownOnly);
    }

    [Fact]
    public void ShutdownArguments_RestartOrShutdown_WithAtLeast30Seconds_AndACleanMessage()
    {
        Assert.Equal("/r /t 60 /d p:4:1", Shutdown.Arguments(false, 60, null!));
        Assert.Equal("/s /t 30 /d p:4:1 /c \"Poste 'PC-TEST-01' : redémarrage\"",
            Shutdown.Arguments(true, 5, "Poste \"PC-TEST-01\" :\r\nredémarrage "));
        Assert.Equal(500, Shutdown.Arguments(false, 60, new string('x', 600)).Split('"')[1].Length);
    }

    [Fact]
    public void Split_EndsATrancheAfterEachRestart()
    {
        var tranches = RunAsPlan.Split([S, S, S, D, D], [false, true, false, false, false]);

        Assert.Equal(
            [new RunAsPlan.Tranche(0, 2, false), new RunAsPlan.Tranche(2, 1, false), new RunAsPlan.Tranche(3, 2, true)],
            tranches);
        // Restart as the last step: no empty slice behind it.
        Assert.Equal([new RunAsPlan.Tranche(0, 2, false)], RunAsPlan.Split([S, S], [false, true]));
    }

    [Fact]
    public void Finish_AfterAShutdown_SkipsTheFollowingSteps_WithTheReason()
    {
        var done = new List<RunnerStepResult> { new() { Name = "Éteindre", State = "success" } };

        var status = RunAsPlan.Finish(done, ["Après"], "Poste éteint par l'étape 1 « Éteindre ».", null);

        Assert.Equal("done", status.State);
        Assert.Equal(["success", "skipped"], status.Steps.Select(s => s.State));
        Assert.Equal("Poste éteint par l'étape 1 « Éteindre ».", status.Steps[1].Error);
        Assert.True(RunAsPlan.Stops(status));
    }
}
