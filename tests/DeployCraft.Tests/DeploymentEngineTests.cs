using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployCraft.Tests;

/// <summary>The deployment engine run end to end against an in-memory remote: no network, no real computer.</summary>
public sealed class DeploymentEngineTests : IAsyncLifetime, IDisposable
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

    /// <summary>
    /// Starts the engine and waits until its startup cleanup has run: it fails any deployment left "running" by a
    /// previous stop, so a deployment started before that point would be failed too.
    /// </summary>
    public async Task InitializeAsync()
    {
        var leftOver = _store.Deployments.Create(SavedPackage(), [new TargetRequest("PC-OLD")], null, null, null,
            RunAsMode.LocalSystem, CopyMode.Push, prioritized: false);
        await _engine.StartAsync(_stop.Token);
        for (var i = 0; i < 200 && _store.Deployments.Get(leftOver.Id)!.Status != DeploymentStatus.Failed; i++) await Task.Delay(10);
    }

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        await _engine.StopAsync(CancellationToken.None);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _stop.Dispose();
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
    public async Task ServiceStop_WaitsForTheCleanupOnTheComputer()
    {
        _remote.WaitForCancel = true;
        _remote.CleanupGate = Task.Delay(300);
        var started = Deploy("PC-TEST-01");
        await _remote.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await StopServiceAsync();

        Assert.True(_remote.CleanedUp, "the service stopped before the cleanup on the computer");
        var deployment = _store.Deployments.Get(started.Id)!;
        Assert.Equal(DeploymentStatus.Failed, deployment.Status);
        var target = Assert.Single(deployment.Targets);
        Assert.Equal(TargetStatus.Failed, target.Status);
        Assert.Equal("Interrompu : le serveur DeployCraft a été arrêté.", target.Error);
        Assert.Equal("failed", Assert.Single(target.Steps).State);
    }

    [Fact]
    public async Task ServiceStop_FailsAComputerStillWaitingForASlot()
    {
        var defaults = _store.Settings.GetDefaults();
        defaults.Performance = new PerformanceSettings { ParallelTargets = 1 };
        _store.Settings.SaveDefaults(defaults);
        _remote.WaitForCancel = true;
        var started = Deploy("PC-TEST-01", "PC-TEST-02");
        await _remote.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await StopServiceAsync();

        var targets = _store.Deployments.Get(started.Id)!.Targets;
        Assert.All(targets, t => Assert.Equal(TargetStatus.Failed, t.Status));
        Assert.All(targets, t => Assert.Equal("Interrompu : le serveur DeployCraft a été arrêté.", t.Error));
        Assert.Single(_remote.Computers);
    }

    [Fact]
    public async Task ServiceStop_DoesNotWaitLongerThanTheHostAllows()
    {
        _remote.WaitForCancel = true;
        var cleanupStuck = new TaskCompletionSource();
        _remote.CleanupGate = cleanupStuck.Task;
        Deploy("PC-TEST-01");
        await _remote.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var hostLimit = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        _stop.Cancel();
        await _engine.StopAsync(hostLimit.Token).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_remote.CleanedUp);
        cleanupStuck.SetResult(); // lets the test cleanup stop the engine
    }

    /// <summary>Stops the engine like the host does: stopping token cancelled, then StopAsync.</summary>
    private async Task StopServiceAsync()
    {
        _stop.Cancel();
        await _engine.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SeveralComputers_AreAllRun()
    {
        var deployment = await WaitEndAsync(Deploy("PC-TEST-01", "PC-TEST-02", "PC-TEST-03").Id);

        Assert.All(deployment.Targets, t => Assert.True(t.Status == TargetStatus.Successful, $"{t.Computer}: {t.Status} {t.Error}"));
        Assert.Equal(["PC-TEST-01", "PC-TEST-02", "PC-TEST-03"], _remote.Computers.Order());
    }

    /// <summary>Answers like a computer would, without touching the network.</summary>
    private sealed class FakeRemote : IRemoteTargets
    {
        private readonly List<string> _computers = [];
        public string StepState { get; set; } = "succeeded";
        public DeployException? Error { get; set; }
        public bool WaitForCancel { get; set; }
        /// <summary>End of the cleanup on the computer once cancelled (deleting the runner service, files…).</summary>
        public Task CleanupGate { get; set; } = Task.CompletedTask;
        public bool CleanedUp { get; private set; }
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
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                finally
                {
                    await CleanupGate;
                    CleanedUp = true;
                }
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
