using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Everything the deployment engine does over the network on a target computer. Behind an interface so the engine
/// can be tested without a real computer.
/// </summary>
public interface IRemoteTargets
{
    /// <summary>Message to log at startup when remote runs cannot work on this server (runner missing), or null.</summary>
    string? SetupProblem();

    /// <summary>
    /// Runs a package on a computer: copy, temporary service, follow-up, cleanup. Reports the current stage and each
    /// runner status; throws <see cref="DeployException"/> on failure (<see cref="DeployException.Offline"/> when the
    /// computer does not answer).
    /// </summary>
    Task RunAsync(string computer, Credential? account, Package package, int startStep, RunAsMode runAs, CopyMode copyMode,
        Action<string> stage, Action<RunnerStatus> progress, CancellationToken ct);

    /// <summary>Null when the computer answers; otherwise the "offline" error to report.</summary>
    Task<DeployException?> ProbeAsync(string computer, CancellationToken ct);

    /// <summary>Sends the wake-on-LAN packet to these network cards.</summary>
    void Wake(IReadOnlyCollection<NetworkCard> cards);
}

/// <summary>Real network implementation of <see cref="IRemoteTargets"/>.</summary>
public sealed class RemoteTargets(DeployCredentials credentials) : IRemoteTargets
{
    private readonly string _runnerExe = Path.Combine(AppContext.BaseDirectory, "runner", "DeployCraft.Runner.exe");

    public string? SetupProblem() => File.Exists(_runnerExe) ? null : $"Runner introuvable : {_runnerExe}";

    public Task RunAsync(string computer, Credential? account, Package package, int startStep, RunAsMode runAs, CopyMode copyMode,
        Action<string> stage, Action<RunnerStatus> progress, CancellationToken ct) =>
        new TargetSession(computer, credentials, account, _runnerExe).RunAsync(package, startStep, runAs, copyMode, stage, progress, ct);

    public Task<DeployException?> ProbeAsync(string computer, CancellationToken ct) => TargetSession.ProbeAsync(computer, ct);

    public void Wake(IReadOnlyCollection<NetworkCard> cards) => WakeOnLan.Send(cards);
}
