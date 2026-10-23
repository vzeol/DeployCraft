using System.Text.Json;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// A deployment on one PC: copy of the runner and files into a "slot",
/// temporary service, wait for the result, cleanup. The slot is reserved by <see cref="SlotLock"/> and the
/// instructions come from <see cref="RunnerJobBuilder"/>.
///
/// On the target:
///   C:\Windows\DeployCraft\slot-N.lock
///   C:\Windows\DeployCraft\slot-N\DeployCraftRunner-N.exe, job.json, status.json, runner.log, exec\
/// </summary>
internal sealed class TargetSession(string computer, DeployCredentials credentials, Credential? account, string runnerExe)
{
    private const string RemoteRoot = @"C:\Windows\DeployCraft";

    private readonly bool _isLocal = IsLocalComputer(computer);
    private int _slot;

    /// <summary>Root folder as seen from the server (local, or through the ADMIN$ share).</summary>
    private string Root => _isLocal ? RemoteRoot : $@"\\{computer}\ADMIN$\DeployCraft";
    private string SlotDir => Path.Combine(Root, $"slot-{_slot}");
    private string LockFile => Path.Combine(Root, $"slot-{_slot}.lock");
    private string ServiceName => $"DeployCraftRunner-{_slot}";
    /// <summary>Service of the current slice (suffixed -1, -2... when there are several identities).</summary>
    private string? _service;
    private string RunnerName => $"{ServiceName}.exe";

    public async Task RunAsync(Package package, int startStep, RunAsMode runAs, CopyMode copyMode,
        Action<string> stage, Action<RunnerStatus> progress, CancellationToken ct)
    {
        stage(Msg.Get("TargetSession_ResolvingName"));
        await CheckReachableAsync(ct);

        stage(Msg.Get("TargetSession_CopyingFiles"));
        var done = new List<RunnerStepResult>();
        var restarted = false;

        try
        {
            // Inside the try: a failed preparation (file not found...) also releases the slot it reserved.
            // During the copy, progress is shown once per second (nothing is written for every chunk).
            var copying = new CopyProgress();
            // A cancellation stops the copy between two chunks; we wait until it has stopped before cleaning up the slot.
            var preparing = Task.Run(() => credentials.RunAs(account, computer, () => Prepare(package, startStep, runAs, copyMode, copying, ct)), CancellationToken.None);
            while (await Task.WhenAny(preparing, Task.Delay(1000, CancellationToken.None)) != preparing)
                if (copying.Text() is { } text) stage(text);
            var (job, modes) = await preparing;
            ct.ThrowIfCancellationRequested();
            // A reboot ends its slice: the next one is started once the computer is back.
            var tranches = RunAsPlan.Split(modes, [.. job.Steps.Select(s => s.Type == RebootType)]);

            // One slice per identity; the next one only starts once the previous one's service is deleted.
            for (var t = 0; t < tranches.Count; t++)
            {
                var tranche = tranches[t];
                var last = t == tranches.Count - 1;
                var trancheJob = new RunnerJob
                {
                    Conditions = job.Conditions,
                    FirstNumber = startStep + tranche.Start,
                    Language = job.Language,
                    Steps = job.Steps.GetRange(tranche.Start, tranche.Count),
                };
                var later = job.Steps.Skip(tranche.Start + tranche.Count).Select(s => s.Name).ToList();
                _service = tranches.Count == 1 ? ServiceName : $"{ServiceName}-{t + 1}";

                var identity = tranche.AsDeployUser && account != null ? Msg.Get("TargetSession_IdentityDeployAccount") : Msg.Get("TargetSession_IdentitySystem");
                // Cancelled between two slices: no new one is started.
                ct.ThrowIfCancellationRequested();
                stage(tranches.Count == 1 ? Msg.Get("TargetSession_StartingRunner") : Msg.Format("TargetSession_StartingRunnerSlice", t + 1, tranches.Count, identity));
                if (restarted) await StartAfterRestartAsync(trancheJob, tranche.AsDeployUser, ct);
                else credentials.RunAs(account, computer, () => StartTranche(trancheJob, tranche.AsDeployUser));
                restarted = false;
                var running = Msg.Format("TargetSession_Running", identity);
                stage(running);

                var final = await WaitAsync(trancheJob, stage, running, status => progress(RunAsPlan.Combine(done, status, later, last)), ct);
                credentials.RunAs(account, computer, () =>
                {
                    using var scm = new ServiceControl(_isLocal ? null : computer);
                    scm.Remove(_service);
                });
                _service = null;
                done.AddRange(final.Steps);
                if (RunAsPlan.Stops(final)) return;

                // Reboot scheduled by the last step of the slice (not skipped, not failed).
                var reboot = trancheJob.Steps[^1];
                if (reboot.Type != RebootType || final.Steps.Count != trancheJob.Steps.Count || final.Steps[^1].State != "success") continue;
                if (reboot.ShutdownOnly)
                {
                    if (!last) progress(RunAsPlan.Finish(done, later, Msg.Format("TargetSession_ShutDownByStep", startStep + tranche.Start + tranche.Count - 1, reboot.Name), final.Network));
                    return;
                }
                await WaitForRestartAsync(reboot, stage, ct);
                restarted = true;
            }
        }
        finally
        {
            stage("Nettoyage");
            try { credentials.RunAs(account, computer, Cleanup); }
            catch { /* un nettoyage raté ne doit pas masquer le vrai résultat */ }
        }
    }

    private const string RebootType = "reboot";

    /// <summary>
    /// Waits for the computer to shut down (after the delay announced to the user), then to answer again, at most the
    /// step's maximum delay. The slot (files and lock) stays on the computer's disk during the reboot.
    /// </summary>
    private async Task WaitForRestartAsync(RunnerJobStep reboot, Action<string> stage, CancellationToken ct)
    {
        stage(Msg.Get("TargetSession_Restarting"));
        var downBy = DateTime.UtcNow.AddSeconds(reboot.Seconds + 300);
        while (await ProbeAsync(computer, ct) == null)
        {
            if (DateTime.UtcNow > downBy)
                throw new DeployException(Msg.Get("TargetSession_NotStopped"));
            await Task.Delay(3000, ct);
        }

        stage(Msg.Get("TargetSession_WaitingBack"));
        var upBy = DateTime.UtcNow.AddSeconds(reboot.TimeoutSeconds);
        while (await ProbeAsync(computer, ct) != null)
        {
            if (DateTime.UtcNow > upBy)
                throw new DeployException(Msg.Format("TargetSession_NotBack", reboot.TimeoutSeconds / 60, reboot.Name));
            await Task.Delay(5000, ct);
        }
    }

    /// <summary>Right after startup, the share may answer before the service manager does: a few attempts.</summary>
    private async Task StartAfterRestartAsync(RunnerJob job, bool asDeployUser, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                credentials.RunAs(account, computer, () => StartTranche(job, asDeployUser));
                return;
            }
            catch (Exception) when (attempt < 12)
            {
                await Task.Delay(10000, ct);
            }
        }
    }

    /// <summary>Writes the slice's instructions and creates its service, under the deployment account or as System.</summary>
    private void StartTranche(RunnerJob job, bool asDeployUser)
    {
        File.WriteAllText(Path.Combine(SlotDir, "job.json"), JsonSerializer.Serialize(job));
        File.Delete(Path.Combine(SlotDir, "status.json"));

        string? serviceAccount = null, password = null;
        if (asDeployUser && account != null)
        {
            // The local account ".\user" is looked up under the computer's name.
            serviceAccount = account.Account;
            LogonRight.GrantServiceLogon(_isLocal ? null : computer, DeployCredentials.LogonDomain(account, computer) + "\\" + account.User);
            password = credentials.Password(account);
        }
        using var scm = new ServiceControl(_isLocal ? null : computer);
        scm.CreateAndStart(_service!, Path.Combine(RemoteRoot, $"slot-{_slot}", RunnerName), serviceAccount, password);
    }

    /// <summary>How long the monitoring tolerates an unreachable computer (the runner keeps going on the computer).</summary>
    internal static readonly TimeSpan ReconnectGrace = TimeSpan.FromMinutes(10);

    /// <summary>Follows status.json until the slice ends; returns its last state.</summary>
    private async Task<RunnerStatus> WaitAsync(RunnerJob job, Action<string> stage, string runningStage, Action<RunnerStatus> progress,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(job.Steps.Sum(s => s.TimeoutSeconds) + 120);
        var statusPath = Path.Combine(SlotDir, "status.json");
        DateTime? lostSince = null;
        while (true)
        {
            await Task.Delay(1000, ct);
            if (DateTime.UtcNow > deadline)
                throw new DeployException(Msg.Get("TargetSession_GlobalTimeout"));

            RunnerStatus? status;
            bool running;
            try
            {
                (status, running) = credentials.RunAs(account, computer, () =>
                {
                    using var scm = new ServiceControl(_isLocal ? null : computer);
                    RefreshShareCache(SlotDir);
                    return (ReadStatus(statusPath), scm.IsRunning(_service!));
                });
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                // Network outage: retry for a while instead of giving up on a computer that is still working.
                lostSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - lostSince > ReconnectGrace)
                    throw new DeployException(
                        Msg.Format("TargetSession_LostDuringRun", ReconnectGrace.TotalMinutes, ex.Message), ex);
                stage(Msg.Get("TargetSession_Retrying"));
                await Task.Delay(4000, ct);
                continue;
            }
            if (lostSince != null)
            {
                lostSince = null;
                stage(runningStage);
            }

            if (status != null)
            {
                progress(status);
                if (status.State == "done") return status;
            }
            if (!running)
            {
                // The service may have finished between the two calls: last reads, while the share catches up with the
                // final status.json (it may look absent or old for a few seconds right after being replaced).
                status = await FinalStatusAsync(() =>
                {
                    try
                    {
                        return credentials.RunAs(account, computer, () =>
                        {
                            RefreshShareCache(SlotDir);
                            return ReadStatus(statusPath);
                        });
                    }
                    catch (Exception ex) when (IsNetworkError(ex))
                    {
                        return null;
                    }
                }, delay => Task.Delay(delay, ct));
                if (status != null) progress(status);
                if (status?.State == "done") return status;
                throw new DeployException(Msg.Get("TargetSession_RunnerStopped"));
            }
        }
    }

    /// <summary>Computer access error that may go away by itself (network, share, remote service manager).</summary>
    internal static bool IsNetworkError(Exception ex) =>
        ex is IOException or System.ComponentModel.Win32Exception;

    /// <summary>
    /// Re-reads the state until it finds "done", at most <paramref name="attempts"/> times one second apart;
    /// returns the last state read (or null).
    /// </summary>
    internal static async Task<RunnerStatus?> FinalStatusAsync(Func<RunnerStatus?> read, Func<TimeSpan, Task> delay, int attempts = 12)
    {
        RunnerStatus? last = null;
        for (var attempt = 1; ; attempt++)
        {
            last = read() ?? last;
            if (last?.State == "done" || attempt >= attempts) return last;
            await delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Windows keeps the content of a folder read through a share (and the absence of a
    /// file) in memory for a few seconds. Creating then deleting an empty file in the folder forces a real re-read.
    /// </summary>
    private void RefreshShareCache(string directory)
    {
        if (_isLocal) return;
        try
        {
            var probe = Path.Combine(directory, $"refresh-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No effect on reading: we read anyway.
        }
    }

    private async Task CheckReachableAsync(CancellationToken ct)
    {
        if (_isLocal) return;
        if (await ProbeAsync(computer, ct) is { } offline) throw offline;
    }

    /// <summary>
    /// Quick test: does the name resolve and does file sharing (port 445) answer?
    /// Returns the matching "offline" error, or null if the computer is reachable.
    /// </summary>
    public static async Task<DeployException?> ProbeAsync(string computer, CancellationToken ct)
    {
        if (IsLocalComputer(computer)) return null;
        System.Net.IPAddress[] addresses;
        try
        {
            addresses = await System.Net.Dns.GetHostAddressesAsync(computer, ct);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return new DeployException(Msg.Format("TargetSession_NotInDns", computer)) { Offline = true };
        }

        foreach (var address in addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).DefaultIfEmpty(addresses.FirstOrDefault()))
        {
            if (address == null) break;
            using var client = new System.Net.Sockets.TcpClient(address.AddressFamily);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await client.ConnectAsync(address, 445, timeout.Token);
                return null;
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // next address
            }
        }
        return new DeployException(Msg.Format("TargetSession_Offline", computer)) { Offline = true };
    }

    /// <summary>Copies the runner and the files, prepares the instructions of all steps and the identity of each.</summary>
    private (RunnerJob Job, List<RunAsMode> Modes) Prepare(Package package, int startStep, RunAsMode runAs, CopyMode copyMode, CopyProgress progress, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Root);
        }
        catch (UnauthorizedAccessException ex) when (_isLocal)
        {
            throw new DeployException(Msg.Get("TargetSession_WindowsDenied"), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw DeployException.FromShareAccess(computer, ex);
        }

        _slot = SlotLock.Acquire(Root);
        if (Directory.Exists(SlotDir)) Directory.Delete(SlotDir, recursive: true); // left over from a crash
        var exec = Directory.CreateDirectory(Path.Combine(SlotDir, "exec")).FullName;
        // Files are first listed (and checked), then copied in one go: the total is known for progress.
        var copies = new CopyBatch();
        copies.Add(runnerExe, Path.Combine(SlotDir, RunnerName));
        var config = runnerExe + ".config";
        if (File.Exists(config)) copies.Add(config, Path.Combine(SlotDir, RunnerName + ".config"));

        var (job, modes) = RunnerJobBuilder.Build(package, startStep, runAs, account != null, copyMode, _isLocal, exec, copies);

        progress.Total = copies.TotalBytes;
        copies.Run(BandwidthLimiter.Copies, progress, ct);
        return (job, modes);
    }

    /// <summary>
    /// Reads status.json without blocking the runner (which replaces it on every change); retries a little if the file
    /// is locked or half written, otherwise null: it will be re-read next round.
    /// </summary>
    internal static RunnerStatus? ReadStatus(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // No File.Exists: through a share, it may answer "absent" from a stale cache.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize<RunnerStatus>(reader.ReadToEnd());
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception ex) when (ex is IOException or JsonException && attempt < 3)
            {
                Thread.Sleep(200);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }
    }

    private void Cleanup()
    {
        if (_slot == 0) return;
        using (var scm = new ServiceControl(_isLocal ? null : computer)) scm.Remove(_service ?? ServiceName);

        // The service exe may stay locked for a few moments after stopping.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(SlotDir))
                {
                    var log = Path.Combine(SlotDir, "runner.log");
                    if (File.Exists(log)) File.Copy(log, Path.Combine(Root, "last-runner.log"), overwrite: true);
                    Directory.Delete(SlotDir, recursive: true);
                }
                File.Delete(LockFile);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    public static bool IsLocalComputer(string name)
    {
        var n = name.Trim().TrimStart('\\').Split('.')[0];
        return n.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || n.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || n == "127" || n == ".";
    }
}
