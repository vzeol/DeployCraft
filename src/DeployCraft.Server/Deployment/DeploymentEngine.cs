using System.Collections.Concurrent;
using System.Threading.Channels;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Deployment;

/// <summary>
/// Deployment queue. Each computer is processed independently,
/// with at most <see cref="PerformanceSettings.ParallelTargets"/> computers in parallel (priority ones first).
/// </summary>
public class DeploymentEngine(IPackageRepository packageRepository, IDeploymentRepository deploymentRepository,
    IScheduleRepository scheduleRepository, ISettingsRepository settingsRepository, IComputerRepository computerRepository,
    DeployCredentials credentials, IRemoteTargets remote, ILogger<DeploymentEngine> log) : BackgroundService
{
    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>();
    private readonly SlotQueue _slots = new(new PerformanceSettings().ParallelTargets);

    /// <summary>Computers waiting or running, by target id, so they can be cancelled.</summary>
    private readonly ConcurrentDictionary<long, (long DeploymentId, CancellationTokenSource Cts)> _active = new();

    /// <summary>End of each computer, by target id: the next package on the same computer waits for it.</summary>
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _done = new();

    /// <summary>Target of the previous package on the same computer (several packages started together).</summary>
    private readonly ConcurrentDictionary<long, long> _after = new();

    /// <summary>
    /// Applies the performance settings from Preferences: at each start and at each save. Computers
    /// already running continue; a decrease only takes effect as they finish.
    /// </summary>
    public void ApplyPerformance(PerformanceSettings? settings)
    {
        settings ??= new();
        _slots.Resize(Math.Clamp(settings.ParallelTargets, 1, PerformanceSettings.MaxParallel));
        BandwidthLimiter.Copies.BytesPerSecond = BandwidthLimiter.FromMbps(Math.Clamp(settings.CopyLimitMbps, 0, PerformanceSettings.MaxCopyLimit));
    }

    /// <summary>
    /// Starts one deployment per package, in the given order. On a given computer, a package only starts
    /// once the previous one has finished (even if it failed). The start step applies to the first package.
    /// </summary>
    public List<Data.Deployment> Start(IReadOnlyList<Package> packages, IEnumerable<TargetRequest> targets, string? launchedBy, DeployOptions options)
    {
        var requests = targets
            .Select(t => t with { Computer = t.Computer.Trim().TrimStart('\\') })
            .Where(t => t.Computer.Length > 0)
            .DistinctBy(t => t.Computer, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requests.Count == 0) throw new ArgumentException(Msg.Get("DeploymentEngine_NoTarget"));
        if (packages.Count == 0) throw new ArgumentException(Msg.Get("DeploymentEngine_NoPackage"));
        foreach (var p in packages.Where(p => p.Steps.Count == 0))
            throw new ArgumentException(Msg.Format("DeploymentEngine_PackageNoSteps", p.Name));
        // The start step is counted on the deployed list, with nested packages replaced by their steps.
        var stepCount = packages[0].Flatten(packageRepository.Get).Steps.Count;
        foreach (var t in requests.Where(t => t.StartStep < 1 || t.StartStep > stepCount))
            throw new ArgumentException(Msg.Format("DeploymentEngine_BadStartStep", t.Computer, t.StartStep, stepCount));

        return StartPlan(
            packages.Select((p, i) => (p, i == 0 ? requests : requests.Select(t => t with { StartStep = 1 }).ToList())).ToList(),
            launchedBy, options, schedule: null);
    }

    /// <summary>
    /// Starts one deployment per package, each with its own computers (a schedule does not resend
    /// a package that already succeeded). On a given computer, packages stay chained in order.
    /// </summary>
    public List<Data.Deployment> StartPlan(IReadOnlyList<(Package Package, List<TargetRequest> Targets)> plan, string? launchedBy,
        DeployOptions options, Schedule? schedule)
    {
        // Chosen credential, otherwise the default one; none saved = the service identity.
        var credential = credentials.Resolve(options.CredentialId);
        if (options.CredentialId != null && credential == null) throw new ArgumentException(Msg.Get("DeploymentEngine_CredentialNotFound"));
        var account = credential?.Account ?? Msg.Get("DeploymentEngine_ServiceAccount");

        var defaults = settingsRepository.GetDefaults();
        ApplyPerformance(defaults.Performance);
        var deployments = new List<Data.Deployment>();
        var previous = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (original, packageTargets) in plan.Where(p => p.Targets.Count > 0))
        {
            // "Default" settings replaced by those from Preferences, and nested packages replaced by their
            // steps: frozen in the deployment history.
            var package = original.Resolve(defaults, packageRepository.Get);
            var runAs = options.RunAs ?? package.RunAs!.Value;
            // Without a credential, the service can only run under a built-in account.
            if (runAs == RunAsMode.DeployUser && credential == null) runAs = RunAsMode.LocalSystem;
            var copyMode = options.CopyMode ?? package.CopyMode!.Value;

            var deployment = deploymentRepository.Create(package, packageTargets, launchedBy, account, credential?.Id, runAs, copyMode, options.Prioritize, schedule);
            foreach (var t in deployment.Targets)
            {
                _done[t.Id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (previous.TryGetValue(t.Computer, out var before)) _after[t.Id] = before;
                previous[t.Computer] = t.Id;
            }
            deployments.Add(deployment);
        }

        foreach (var d in deployments) _queue.Writer.TryWrite(d.Id);
        return deployments;
    }

    /// <summary>
    /// Cancels computers of a deployment (all of them if <paramref name="targetIds"/> is empty).
    /// A waiting computer is not started; on a running computer the installation is stopped.
    /// </summary>
    public int Abort(long deploymentId, IReadOnlyCollection<long>? targetIds, string? by)
    {
        var count = 0;
        foreach (var (targetId, entry) in _active)
        {
            if (entry.DeploymentId != deploymentId) continue;
            if (targetIds is { Count: > 0 } && !targetIds.Contains(targetId)) continue;
            _abortedBy[targetId] = by;
            entry.Cts.Cancel();
            count++;
        }
        return count;
    }

    private readonly ConcurrentDictionary<long, string?> _abortedBy = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        deploymentRepository.FailInterrupted();
        ApplyPerformance(settingsRepository.GetDefaults().Performance);
        if (remote.SetupProblem() is { } problem) log.LogError("{Problem}", problem);

        await foreach (var id in _queue.Reader.ReadAllAsync(ct))
        {
            // Each deployment advances in parallel with the others; the slot queue limits the total number of computers.
            _ = Task.Run(() => RunDeploymentAsync(id, ct), ct);
        }
    }

    private async Task RunDeploymentAsync(long id, CancellationToken ct)
    {
        var deployment = deploymentRepository.Get(id);
        if (deployment == null) return;

        // Registered right away: a computer still waiting must be cancellable.
        var runs = deployment.Targets.Select(t =>
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _active[t.Id] = (id, cts);
            return (Target: t, Cts: cts);
        }).ToList();

        deploymentRepository.UpdateStatus(id, DeploymentStatus.Running, ended: false);
        await Task.WhenAll(runs.Select(r => RunTargetAsync(deployment, r.Target, r.Cts, ct)));

        var ok = deployment.Targets.All(t => t.Status == TargetStatus.Successful);
        deploymentRepository.UpdateStatus(id, ok ? DeploymentStatus.Successful : DeploymentStatus.Failed, ended: true);
    }

    private async Task RunTargetAsync(Data.Deployment deployment, DeploymentTarget target, CancellationTokenSource cts, CancellationToken appStopping)
    {
        var slotTaken = false;
        // Counted in the schedule history: not cancellations nor computers that never started.
        var countForSchedule = false;
        // Slot wait is limited (schedule option): the computer is abandoned if it has not started in time.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        if (deployment.QueueTimeoutMinutes is > 0 and var minutes)
        {
            var left = TimeSpan.FromMinutes(minutes) - (DateTime.UtcNow - deployment.CreatedUtc);
            waitCts.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        }
        try
        {
            try
            {
                if (_after.TryRemove(target.Id, out var before) && _done.TryGetValue(before, out var previous))
                {
                    target.Stage = Msg.Get("DeploymentEngine_WaitingPrevious");
                    deploymentRepository.UpdateTarget(target);
                    await previous.Task.WaitAsync(waitCts.Token);
                }

                await _slots.WaitAsync(target.Prioritized, waitCts.Token);
                slotTaken = true;
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested && !cts.IsCancellationRequested)
            {
                target.Status = TargetStatus.Failed;
                target.Error = Msg.Format("DeploymentEngine_QueueTimeout", deployment.QueueTimeoutMinutes);
                return;
            }
            countForSchedule = true;

            target.Status = TargetStatus.Running;
            target.StartedUtc = DateTime.UtcNow;
            target.Stage = Msg.Get("DeploymentEngine_Starting");
            deploymentRepository.UpdateTarget(target);

            Credential? credential = null;
            if (deployment.CredentialId is { } credentialId)
                credential = credentials.Get(credentialId)
                    ?? throw new DeployException(Msg.Format("DeploymentEngine_CredentialDeleted", deployment.RunAs));

            // Offline: retry later if the package allows it (the slot is released during the wait).
            var offline = deployment.Snapshot.Offline ?? new OfflineSettings();
            var attempts = offline.Retry ? Math.Max(1, offline.RetryAttempts) : 1;
            IReadOnlyList<int>? earlierRuns = null;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (offline.WakeOnLan) await WakeAsync(target, offline.WakeWaitMinutes, cts.Token);
                    var networkSaved = false;
                    await remote.RunAsync(
                        target.Computer,
                        credential,
                        deployment.Snapshot,
                        target.StartStep,
                        deployment.RunAsMode,
                        deployment.CopyMode,
                        stage: s => { target.Stage = s; deploymentRepository.UpdateTarget(target); },
                        progress: status =>
                        {
                            if (!networkSaved && status.Network is { Count: > 0 } cards)
                            {
                                computerRepository.SaveNetwork(target.Computer, cards.Select(c => new NetworkCard(c.Mac, c.Broadcast)).ToList());
                                networkSaved = true;
                            }
                            target.Steps = ToOutcomes(status, earlierRuns);
                            deploymentRepository.UpdateTarget(target);
                        },
                        cts.Token);
                    break;
                }
                catch (DeployException ex) when (ex.Offline && attempt < attempts)
                {
                    // Steps already passed during this attempt count in the runs of the next attempt.
                    earlierRuns = [.. target.Steps.Select(s => s.Runs)];
                    _slots.Release();
                    slotTaken = false;
                    var next = DateTime.Now.AddMinutes(Math.Max(1, offline.RetryIntervalMinutes));
                    target.Status = TargetStatus.Queued;
                    target.Error = ex.Message;
                    target.Stage = Msg.Format("DeploymentEngine_OfflineRetry", next, attempt + 1, attempts);
                    deploymentRepository.UpdateTarget(target);

                    await Task.Delay(next - DateTime.Now, cts.Token);
                    target.Stage = Msg.Get("DeploymentEngine_WaitingSlot");
                    deploymentRepository.UpdateTarget(target);
                    await _slots.WaitAsync(target.Prioritized, cts.Token);
                    slotTaken = true;
                    target.Status = TargetStatus.Running;
                    target.Error = null;
                    target.Stage = Msg.Get("DeploymentEngine_Starting");
                    deploymentRepository.UpdateTarget(target);
                }
            }

            (target.Status, target.Error) = Outcome(target.Steps);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !appStopping.IsCancellationRequested)
        {
            countForSchedule = false;
            target.Status = TargetStatus.Failed;
            _abortedBy.TryRemove(target.Id, out var by);
            target.Error = string.IsNullOrEmpty(by) ? Msg.Format("DeploymentEngine_Aborted", DeploymentSummary.AbortedPrefix) : Msg.Format("DeploymentEngine_AbortedBy", DeploymentSummary.AbortedPrefix, by);
            MarkAborted(target.Steps, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            target.Status = TargetStatus.Failed;
            target.Error = ErrorMessages.ForException(ex);
            log.LogWarning(ex, "Échec sur {Computer}", target.Computer);
        }
        finally
        {
            target.Stage = null;
            target.StartedUtc ??= DateTime.UtcNow;
            target.EndedUtc = DateTime.UtcNow;
            deploymentRepository.UpdateTarget(target);
            if (countForSchedule && deployment.ScheduleId is { } scheduleId && !appStopping.IsCancellationRequested)
            {
                try
                {
                    scheduleRepository.RecordResult(scheduleId, target.Computer, deployment.PackageId, target.Status == TargetStatus.Successful);
                }
                catch (Exception ex) { log.LogWarning(ex, "Historique de planification non mis à jour ({Computer})", target.Computer); }
            }
            if (slotTaken) _slots.Release();
            _active.TryRemove(target.Id, out _);
            cts.Dispose();
            if (_done.TryRemove(target.Id, out var done)) done.TrySetResult();
        }
    }

    /// <summary>
    /// Computer that does not respond: sends the wake-up packet then waits for it to respond.
    /// Returns without doing anything if the computer already responds; throws an "offline" error if it does not wake up.
    /// </summary>
    private async Task WakeAsync(DeploymentTarget target, int waitMinutes, CancellationToken ct)
    {
        if (await remote.ProbeAsync(target.Computer, ct) is not { } down) return;
        var cards = computerRepository.GetNetwork(target.Computer);
        if (cards.Count == 0)
            throw new DeployException(down.Message + " " + Msg.Get("DeploymentEngine_WakeNoMac")) { Offline = true };

        target.Stage = Msg.Get("DeploymentEngine_Waking");
        deploymentRepository.UpdateTarget(target);
        remote.Wake(cards);
        var deadline = DateTime.UtcNow.AddMinutes(Math.Max(1, waitMinutes));
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            if (await remote.ProbeAsync(target.Computer, ct) == null) return;
        }
        throw new DeployException(Msg.Format("DeploymentEngine_WakeTimeout", target.Computer, Math.Max(1, waitMinutes))) { Offline = true };
    }

    /// <summary>
    /// Result of a computer from its steps: a failed step makes it fail. Stop on success or skipped
    /// steps (conditions not met, disabled step...): computer succeeded, with the reason as a note.
    /// </summary>
    internal static (TargetStatus Status, string? Error) Outcome(List<StepOutcome> steps)
    {
        if (steps.FirstOrDefault(s => s.State == "failed") is { } failed) return (TargetStatus.Failed, ErrorMessages.ForStep(failed));
        if (steps.Count == 0) return (TargetStatus.Failed, null);
        if (steps.FirstOrDefault(s => s.State == "stopped") is { } stopped)
            return (TargetStatus.Successful, Msg.Get("DeploymentEngine_StoppedAsSuccess") + " " + ErrorMessages.ForStep(stopped));
        var skipped = steps.Where(s => s.State == "skipped").ToList();
        if (skipped.Count == 0) return (TargetStatus.Successful, null);
        // Everything skipped (package conditions not met...): the reason is enough.
        if (skipped.Count == steps.Count) return (TargetStatus.Successful, steps[0].Error);
        var first = skipped[0];
        var reason = string.IsNullOrEmpty(first.Error) ? "" : $" ({first.Error})";
        return (TargetStatus.Successful, skipped.Count == 1
            ? Msg.Format("DeploymentEngine_OneSkipped", first.Name, reason)
            : Msg.Format("DeploymentEngine_ManySkipped", skipped.Count, first.Name, reason));
    }

    /// <summary>Cancellation: the interrupted step (still "running") becomes failed "Annulé.", with an end date.</summary>
    internal static void MarkAborted(List<StepOutcome> steps, DateTime nowUtc)
    {
        foreach (var step in steps.Where(s => s.State == "running"))
        {
            step.State = "failed";
            step.Error ??= Msg.Format("DeploymentEngine_Aborted", DeploymentSummary.AbortedPrefix);
            step.EndedUtc ??= nowUtc;
        }
    }

    /// <param name="earlierRuns">Runs from earlier attempts on this computer, by step position.</param>
    internal static List<StepOutcome> ToOutcomes(RunnerStatus status, IReadOnlyList<int>? earlierRuns = null) =>
        status.Steps.Select(s => new StepOutcome
        {
            Name = s.Name,
            State = s.State,
            ExitCode = s.ExitCode,
            CommandLine = s.CommandLine,
            Output = s.Output,
            Error = s.Error,
            StartedUtc = s.StartedUtc?.ToUniversalTime(),
            EndedUtc = s.EndedUtc?.ToUniversalTime(),
        }).Select((o, i) =>
        {
            o.Runs = (earlierRuns != null && i < earlierRuns.Count ? earlierRuns[i] : 0) + (o.Ran ? 1 : 0);
            return o;
        }).ToList();
}
