using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Identity of each step and the "slices" started on the computer. A runner service has only one identity: the
/// deployment account, or Local System (which is also used to launch into the logged-on user's session).
/// Consecutive steps with the same service form a slice; the server starts the slices one after the other. The
/// account password never leaves the server (it is only used to create the service).
/// </summary>
internal static class RunAsPlan
{
    /// <param name="Start">Position of the first step in the list sent (0 = the first).</param>
    /// <param name="AsDeployUser">Service under the deployment account; otherwise Local System.</param>
    public sealed record Tranche(int Start, int Count, bool AsDeployUser);

    /// <summary>Actual identity of a step: its own, otherwise the deployment's; with no credential, Local System.</summary>
    /// <remarks>A message is shown by a process started in each session: it needs a service running as System.</remarks>
    public static RunAsMode Effective(PackageStep step, RunAsMode deployment, bool hasAccount) =>
        step.Type == StepType.Message ? RunAsMode.LocalSystem
        : (step.RunAs ?? deployment) is var mode && mode == RunAsMode.DeployUser && !hasAccount ? RunAsMode.LocalSystem : mode;

    /// <param name="cutAfter">Steps that end their slice (restart): the next one starts after the computer is back.</param>
    public static List<Tranche> Split(IReadOnlyList<RunAsMode> modes, IReadOnlyList<bool>? cutAfter = null)
    {
        var tranches = new List<Tranche>();
        for (var i = 0; i < modes.Count;)
        {
            var asDeployUser = modes[i] == RunAsMode.DeployUser;
            var j = i;
            while (j < modes.Count && (modes[j] == RunAsMode.DeployUser) == asDeployUser)
            {
                var cut = cutAfter != null && cutAfter[j];
                j++;
                if (cut) break;
            }
            tranches.Add(new Tranche(i, j - i, asDeployUser));
            i = j;
        }
        return tranches;
    }

    /// <summary>A finished slice stops the package: stop requested by the runner, or runner interrupted by an error.</summary>
    public static bool Stops(RunnerStatus finished) => finished.Halt || finished.Error != null;

    /// <summary>Reason given to the steps of the slices not started.</summary>
    public static string? StopReason(RunnerStatus finished) =>
        finished.Halt ? finished.HaltReason : finished.Error != null ? Msg.Format("RunAsPlan_PreviousInterrupted", finished.Error) : null;

    /// <summary>Early end decided by the server (computer off): the following steps are skipped with <paramref name="reason"/>.</summary>
    public static RunnerStatus Finish(IReadOnlyList<RunnerStepResult> done, IReadOnlyList<string> later, string reason, List<RunnerNetworkCard>? network) => new()
    {
        State = "done",
        Steps = [.. done, .. later.Select(name => new RunnerStepResult { Name = name, State = "skipped", Error = reason })],
        Network = network,
        Halt = true,
        HaltReason = reason,
    };

    /// <summary>
    /// Complete progress: results of the finished slices, then those of the current slice. If the current slice
    /// stops the package, the steps of the following slices are added to it as "skipped" with its reason.
    /// </summary>
    public static RunnerStatus Combine(IReadOnlyList<RunnerStepResult> done, RunnerStatus current, IReadOnlyList<string> later, bool last)
    {
        var steps = new List<RunnerStepResult>(done);
        steps.AddRange(current.Steps);
        var stops = current.State == "done" && Stops(current);
        if (stops)
            steps.AddRange(later.Select(name => new RunnerStepResult { Name = name, State = "skipped", Error = StopReason(current) }));
        return new RunnerStatus
        {
            State = current.State == "done" && (last || stops) ? "done" : "running",
            Error = current.Error,
            Steps = steps,
            Network = current.Network,
            Halt = current.Halt,
            HaltReason = current.HaltReason,
        };
    }
}
