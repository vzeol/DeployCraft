using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>status.json read by the server while the runner replaces it, and a step interrupted by a cancellation.</summary>
public sealed class StatusFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class NoFacts : ITargetFacts
    {
        public string WindowsFamily => "11";
        public bool Is64Bit => true;
        public int PowerShellVersion => 5;
        public bool UserLoggedOn => false;
        public bool PathExists(string path) => false;
        public bool RegistryExists(string key, string value) => false;
    }

    [Fact]
    public void Retry_GoesOnAfterALockedFile_AndGivesUpAfterTheLastAttempt()
    {
        var calls = 0;
        Json.Retry(() => { if (++calls < 3) throw new IOException("verrouillé"); }, 10, _ => { });
        Assert.Equal(3, calls);

        calls = 0;
        Assert.Throws<IOException>(() => Json.Retry(() => { calls++; throw new IOException("verrouillé"); }, 4, _ => { }));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void FailedSave_NeverStopsTheRemainingSteps()
    {
        var ran = new List<string>();
        var logs = new List<string>();
        var status = new RunStatus();

        StepSequence.Run(new Job { Steps = [new JobStep { Name = "a" }, new JobStep { Name = "b" }, new JobStep { Name = "c" }] },
            new NoFacts(), status,
            (_, step, result) => { ran.Add(step.Name); result.State = StepSequence.Success; },
            save: () => throw new IOException("status.json utilisé par un autre processus"),
            aborted: () => false, log: logs.Add);

        Assert.Equal(["a", "b", "c"], ran);
        Assert.All(status.Steps, s => Assert.Equal("success", s.State));
        Assert.Contains(logs, l => l.StartsWith("Enregistrement de l'avancement impossible"));
    }

    [Fact]
    public void ReadStatus_WorksWhileTheRunnerHoldsTheFile_AndSkipsAHalfWrittenOne()
    {
        var path = Path.Combine(_dir, "status.json");
        File.WriteAllText(path, """{"state":"done","steps":[{"name":"a","state":"success"}]}""");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            Assert.Equal("done", TargetSession.ReadStatus(path)!.State);

        File.WriteAllText(path, """{"state":"run""");
        Assert.Null(TargetSession.ReadStatus(path));
        Assert.Null(TargetSession.ReadStatus(Path.Combine(_dir, "absent.json")));
    }

    [Fact]
    public void Abort_EndsTheInterruptedStep()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var steps = new List<StepOutcome>
        {
            new() { Name = "a", State = "success", EndedUtc = now.AddMinutes(-1) },
            new() { Name = "b", State = "running" },
        };

        DeploymentEngine.MarkAborted(steps, now, "Annulé.");

        Assert.Equal("success", steps[0].State);
        Assert.Equal("failed", steps[1].State);
        Assert.Equal("Annulé.", steps[1].Error);
        Assert.Equal(now, steps[1].EndedUtc);
    }

    [Fact]
    public async Task FinalStatus_WaitsForTheFinalFile_WhenTheShareShowsItLate()
    {
        // Right after the runner stops: file "absent" (share cache), then old state, then the right one.
        var reads = new Queue<RunnerStatus?>([null, new RunnerStatus { State = "running" }, new RunnerStatus { State = "done" }]);
        var waits = 0;

        var status = await TargetSession.FinalStatusAsync(() => reads.Dequeue(), _ => { waits++; return Task.CompletedTask; });

        Assert.Equal("done", status!.State);
        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task FinalStatus_GivesUpAfterTheLastAttempt_KeepingTheLastStateRead()
    {
        var calls = 0;

        var status = await TargetSession.FinalStatusAsync(() => ++calls == 2 ? new RunnerStatus { State = "running" } : null,
            _ => Task.CompletedTask, attempts: 5);

        Assert.Equal(5, calls);
        Assert.Equal("running", status!.State);
    }
}
