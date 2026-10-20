using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployCraft.Tests;

/// <summary>The deployment engine run end to end against an in-memory remote: no network, no real computer.</summary>
public sealed class DeploymentEngineTests : IAsyncLifetime
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly Store _store;
    private readonly FakeRemote _remote = new();
    private readonly DeploymentEngine _engine;
    private readonly CancellationTokenSource _stop = new();

    public DeploymentEngineTests()
    {
        _store = new Store(Path.Combine(_dir, "test.db"));
        var credentials = new DeployCredentials(_store.Credentials, _store.Settings);
        _engine = new DeploymentEngine(_store.Packages, _store.Deployments, _store.Schedules, _store.Settings, _store.Computers,
            credentials, _remote, NullLogger<DeploymentEngine>.Instance);
    }

    public Task InitializeAsync() => _engine.StartAsync(_stop.Token);

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        await _engine.StopAsync(CancellationToken.None);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private Package SavedPackage() => _store.Packages.Save(new Package
    {
        Name = "Tool",
        Steps = [new() { Name = "Install", Type = StepType.Command, Command = "hostname" }],
    });

    private DeployCraft.Server.Data.Deployment Deploy(params string[] computers) =>
        _engine.Start([SavedPackage()], computers.Select(c => new TargetRequest(c, 1)), "admin", new DeployOptions()).Single();

    private async Task<DeployCraft.Server.Data.Deployment> WaitEndAsync(long id)
    {
        for (var i = 0; i < 200; i++)
        {
            var d = _store.Deployments.Get(id)!;
            if (d.Status is DeploymentStatus.Successful or DeploymentStatus.Failed) return d;
            await Task.Delay(25);
        }
        throw new TimeoutException("The deployment did not end.");
    }

    [Fact]
    public async Task SuccessfulRun_MarksComputerAndDeploymentSuccessful()
    {
        var deployment = await WaitEndAsync(Deploy("PC-TEST-01").Id);

        Assert.Equal(DeploymentStatus.Successful, deployment.Status);
        var target = Assert.Single(deployment.Targets);
        Assert.Equal(TargetStatus.Successful, target.Status);
        Assert.Equal("succeeded", Assert.Single(target.Steps).State);
        Assert.Equal(["PC-TEST-01"], _remote.Computers);
    }

    [Fact]
    public async Task FailedStep_FailsTheComputer()
    {
        _remote.StepState = "failed";

        var deployment = await WaitEndAsync(Deploy("PC-TEST-01").Id);

        Assert.Equal(DeploymentStatus.Failed, deployment.Status);
        Assert.Equal(TargetStatus.Failed, Assert.Single(deployment.Targets).Status);
    }

    [Fact]
    public async Task OfflineComputer_FailsWithTheRemoteMessage()
    {
        _remote.Error = new DeployException("PC-TEST-01 does not answer.") { Offline = true };

        var target = Assert.Single((await WaitEndAsync(Deploy("PC-TEST-01").Id)).Targets);

        Assert.Equal(TargetStatus.Failed, target.Status);
        Assert.Contains("PC-TEST-01 does not answer.", target.Error);
    }

    [Fact]
    public async Task Abort_StopsARunningComputer()
    {
        _remote.WaitForCancel = true;
        var started = Deploy("PC-TEST-01");
        await _remote.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, _engine.Abort(started.Id, null, "admin"));
        var target = Assert.Single((await WaitEndAsync(started.Id)).Targets);

        Assert.Equal(TargetStatus.Failed, target.Status);
        Assert.StartsWith(DeploymentSummary.AbortedPrefix, target.Error);
    }

    [Fact]
    public async Task SeveralComputers_AreAllRun()
    {
        var deployment = await WaitEndAsync(Deploy("PC-TEST-01", "PC-TEST-02", "PC-TEST-03").Id);

        Assert.All(deployment.Targets, t => Assert.Equal(TargetStatus.Successful, t.Status));
        Assert.Equal(["PC-TEST-01", "PC-TEST-02", "PC-TEST-03"], _remote.Computers.Order());
    }

    /// <summary>Answers like a computer would, without touching the network.</summary>
    private sealed class FakeRemote : IRemoteTargets
    {
        private readonly List<string> _computers = [];
        public string StepState { get; set; } = "succeeded";
        public DeployException? Error { get; set; }
        public bool WaitForCancel { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Computers
        {
            get { lock (_computers) return [.. _computers]; }
        }

        public string? SetupProblem() => null;

        public async Task RunAsync(string computer, Credential? account, Package package, int startStep, RunAsMode runAs,
            CopyMode copyMode, Action<string> stage, Action<RunnerStatus> progress, CancellationToken ct)
        {
            lock (_computers) _computers.Add(computer);
            stage("Running");
            Started.TrySetResult();
            if (WaitForCancel)
            {
                progress(new RunnerStatus { Steps = [new() { Name = package.Steps[0].Name, State = "running" }] });
                await Task.Delay(Timeout.Infinite, ct);
            }
            if (Error != null) throw Error;
            progress(new RunnerStatus
            {
                State = "done",
                Steps = package.Steps.Select(s => new RunnerStepResult { Name = s.Name, State = StepState, ExitCode = StepState == "failed" ? 1 : 0 }).ToList(),
            });
        }

        public Task<DeployException?> ProbeAsync(string computer, CancellationToken ct) => Task.FromResult<DeployException?>(null);

        public void Wake(IReadOnlyCollection<NetworkCard> cards) { }
    }
}
