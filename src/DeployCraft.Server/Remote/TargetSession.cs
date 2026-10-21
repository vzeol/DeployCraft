using System.Text.Json;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// A deployment on one PC: copy of the runner and files into a "slot",
/// temporary service, wait for the result, cleanup.
///
/// On the target:
///   C:\Windows\DeployCraft\slot-N.lock
///   C:\Windows\DeployCraft\slot-N\DeployCraftRunner-N.exe, job.json, status.json, runner.log, exec\
/// </summary>
internal sealed class TargetSession(string computer, DeployCredentials credentials, Credential? account, string runnerExe)
{
    private const string RemoteRoot = @"C:\Windows\DeployCraft";
    private const int MaxSlots = 32;

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
        stage("Résolution du nom");
        await CheckReachableAsync(ct);

        stage("Copie des fichiers");
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
                    Steps = job.Steps.GetRange(tranche.Start, tranche.Count),
                };
                var later = job.Steps.Skip(tranche.Start + tranche.Count).Select(s => s.Name).ToList();
                _service = tranches.Count == 1 ? ServiceName : $"{ServiceName}-{t + 1}";

                var identity = tranche.AsDeployUser && account != null ? "compte de déploiement" : "Système";
                // Cancelled between two slices: no new one is started.
                ct.ThrowIfCancellationRequested();
                stage(tranches.Count == 1 ? "Démarrage du runner" : $"Démarrage du runner ({t + 1}/{tranches.Count}, {identity})");
                if (restarted) await StartAfterRestartAsync(trancheJob, tranche.AsDeployUser, ct);
                else credentials.RunAs(account, computer, () => StartTranche(trancheJob, tranche.AsDeployUser));
                restarted = false;
                var running = $"Exécution ({identity})";
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
                    if (!last) progress(RunAsPlan.Finish(done, later, $"Poste éteint par l'étape {startStep + tranche.Start + tranche.Count - 1} « {reboot.Name} ».", final.Network));
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
        stage("Redémarrage du poste");
        var downBy = DateTime.UtcNow.AddSeconds(reboot.Seconds + 300);
        while (await ProbeAsync(computer, ct) == null)
        {
            if (DateTime.UtcNow > downBy)
                throw new DeployException("Le poste ne s'est pas arrêté après la demande de redémarrage (annulée sur le poste ?).");
            await Task.Delay(3000, ct);
        }

        stage("Attente du retour du poste");
        var upBy = DateTime.UtcNow.AddSeconds(reboot.TimeoutSeconds);
        while (await ProbeAsync(computer, ct) != null)
        {
            if (DateTime.UtcNow > upBy)
                throw new DeployException($"Le poste n'est pas revenu {reboot.TimeoutSeconds / 60} min après le redémarrage (délai max de l'étape « {reboot.Name} »).");
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
                throw new DeployException("Délai global dépassé : le runner ne répond plus.");

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
                        $"Poste injoignable depuis {ReconnectGrace.TotalMinutes:0} min pendant l'exécution : {ex.Message}", ex);
                stage("Poste injoignable, nouvelle tentative…");
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
                throw new DeployException("Le runner s'est arrêté sans rendre de résultat (voir runner.log sur la cible).");
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
            return new DeployException($"Nom introuvable dans le DNS : « {computer} » (poste éteint depuis longtemps ?).") { Offline = true };
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
        return new DeployException($"{computer} est hors ligne (éteint, ou partage de fichiers bloqué par le pare-feu).") { Offline = true };
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
            throw new DeployException("Accès refusé à C:\\Windows sur ce serveur : DeployCraft doit tourner en administrateur.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw DeployException.FromShareAccess(computer, ex);
        }

        AcquireSlot();
        var exec = Directory.CreateDirectory(Path.Combine(SlotDir, "exec")).FullName;
        // Files are first listed (and checked), then copied in one go: the total is known for progress.
        var copies = new CopyBatch();
        copies.Add(runnerExe, Path.Combine(SlotDir, RunnerName));
        var config = runnerExe + ".config";
        if (File.Exists(config)) copies.Add(config, Path.Combine(SlotDir, RunnerName + ".config"));

        var job = new RunnerJob { Conditions = RunnerConditions.From(package.Conditions), FirstNumber = startStep };
        var modes = new List<RunAsMode>();
        foreach (var step in StepsFrom(package, startStep))
        {
            var mode = RunAsPlan.Effective(step, runAs, account != null);
            modes.Add(mode);
            var jobStep = ToJobStep(step, package, mode);

            // Disabled step: sent so it shows as "Ignoré" (skipped), but no file is copied or checked.
            if (step.Enabled && step.Type is StepType.Install or StepType.Command or StepType.PowerShell)
                StageAdditionalFiles(step, jobStep, copyMode, exec, copies);

            if (step.Type == StepType.Install && step.Enabled)
            {
                if (string.IsNullOrWhiteSpace(step.File)) throw new DeployException($"Étape « {jobStep.Name} » : aucun fichier d'installation.");
                jobStep.Command = Path.GetFileName(step.File);
                jobStep.CommandLine = InstallCommand.For(step);
                if (step.KeepInstallFile) jobStep.KeepFolder = KeepFolder(package.Name);
                // Pull: the computer reads the share itself, with the runner's identity. A local path stays push.
                if (copyMode == CopyMode.Pull && step.File.StartsWith(@"\\"))
                {
                    jobStep.SourcePath = step.File;
                    jobStep.IncludeDirectory = step.IncludeDirectory;
                    job.Steps.Add(jobStep);
                    continue;
                }
                // Push: the file is read by the server (with the deployment account if it is on a share).
                if (!File.Exists(step.File)) throw new DeployException($"Fichier d'installation introuvable : {step.File}");
                if (step.IncludeDirectory) CopyDirectory(Path.GetDirectoryName(step.File)!, exec, copies);
                else copies.Add(step.File, Path.Combine(exec, Path.GetFileName(step.File)));
            }
            else if (step.Type == StepType.Reboot && step.Enabled && _isLocal)
                throw new DeployException($"Étape « {jobStep.Name} » : DeployCraft ne redémarre pas le serveur sur lequel il tourne.");
            else if (step.Type == StepType.CopyFiles && step.Enabled)
            {
                if (string.IsNullOrWhiteSpace(step.CopySource)) throw new DeployException($"Étape « {jobStep.Name} » : aucun fichier ou dossier à copier.");
                if (copyMode == CopyMode.Pull && step.CopySource.StartsWith(@"\\"))
                    jobStep.SourcePath = step.CopySource;
                else
                {
                    // Push: the server drops the selected files into exec\copie-N, which the computer then copies as they are.
                    jobStep.Command = $"copie-{startStep + job.Steps.Count}";
                    StageCopy(step.CopySource, step.CopyPatterns, step.CopySubfolders, Path.Combine(exec, jobStep.Command), copies);
                    jobStep.CopyPatterns = ["*"];
                    jobStep.CopySubfolders = true;
                }
            }
            job.Steps.Add(jobStep);
        }

        progress.Total = copies.TotalBytes;
        copies.Run(BandwidthLimiter.Copies, progress, ct);
        return (job, modes);
    }

    /// <summary>
    /// File copy in push mode: drops file <paramref name="source"/> into <paramref name="target"/>, or the
    /// files of the folder that match the patterns (with subfolders if requested), keeping their tree structure.
    /// </summary>
    /// <param name="batch">List to add the copies to (null = copy right away).</param>
    internal static void StageCopy(string source, string? patterns, bool subfolders, string target, CopyBatch? batch = null)
    {
        var copies = batch ?? new CopyBatch();
        Directory.CreateDirectory(target);
        if (File.Exists(source))
        {
            copies.Add(source, Path.Combine(target, Path.GetFileName(source)));
            if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
            return;
        }
        if (!Directory.Exists(source)) throw new DeployException($"Fichier ou dossier à copier introuvable : {source}");

        var option = subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        // As on the computer: subfolders are recreated even when empty.
        if (subfolders)
            foreach (var dir in Directory.EnumerateDirectories(source, "*", option))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var pattern in PackageStep.SplitPatterns(patterns))
            foreach (var file in Directory.EnumerateFiles(source, pattern, option))
                copies.Add(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
    }

    /// <summary>
    /// Additional files: dropped by the server next to the command (exec\), or, in pull mode, read by the computer
    /// itself from the share. A missing file stops the computer's preparation.
    /// </summary>
    /// <param name="batch">List to add the copies to (null = copy right away).</param>
    internal static void StageAdditionalFiles(PackageStep step, RunnerJobStep jobStep, CopyMode copyMode, string exec, CopyBatch? batch = null)
    {
        var copies = batch ?? new CopyBatch();
        var files = PackageStep.SplitLines(step.AdditionalFiles).Select(Environment.ExpandEnvironmentVariables).ToList();
        if (files.Count == 0) return;
        jobStep.AdditionalFiles = [.. files.Select(Path.GetFileName).OfType<string>()];
        foreach (var file in files)
        {
            if (copyMode == CopyMode.Pull && file.StartsWith(@"\\"))
            {
                (jobStep.PullFiles ??= []).Add(file);
                continue;
            }
            if (!File.Exists(file)) throw new DeployException($"Étape « {jobStep.Name} » : fichier additionnel introuvable : {file}");
            copies.Add(file, Path.Combine(exec, Path.GetFileName(file)));
        }
        if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
    }

    /// <summary>Folder on the computer where a package's installation files are kept (option of the "Installer" step).</summary>
    internal static string KeepFolder(string packageName)
    {
        var safe = string.Concat(packageName.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        return $@"%ProgramData%\DeployCraft\Installeurs\{(safe.Length > 0 ? safe : "Paquet")}";
    }

    /// <summary>Steps sent to the computer when (re)starting at step N.</summary>
    internal static IEnumerable<PackageStep> StepsFrom(Package package, int startStep) =>
        package.Steps.Skip(Math.Max(0, startStep - 1));

    /// <summary>Instructions of one step for the runner (apart from files to copy), run under identity <paramref name="runAs"/>.</summary>
    internal static RunnerJobStep ToJobStep(PackageStep step, Package package, RunAsMode runAs)
    {
        if (step.Type == StepType.Nested)
            throw new DeployException($"Étape « {step.Name} » : paquet imbriqué non remplacé par ses étapes.");
        return new()
        {
            Name = string.IsNullOrWhiteSpace(step.Name) ? step.Type.ToString() : step.Name,
            Type = step.Type.ToString().ToLowerInvariant(),
            Arguments = step.Type == StepType.Message ? step.MessageTitle : step.Arguments,
            Command = step.Type switch { StepType.Reboot => step.RebootMessage, StepType.Message => step.MessageText, _ => step.Command },
            Seconds = step.Type switch
            {
                StepType.Pause => step.PauseSeconds,
                StepType.Reboot => step.RebootDelaySeconds,
                StepType.Message => step.MessageSeconds,
                _ => 0,
            },
            // Copy: robocopy succeeds with 0 to 7; 8 to 15 = some files could not be copied; 16 = serious error.
            SuccessCodes = step.Type == StepType.CopyFiles ? [.. Enumerable.Range(0, step.CopyIgnoreErrors ? 16 : 8)] : ParseCodes(step.SuccessCodes),
            // A pause lasts as long as it lasts: its timeout (which counts in the server's wait) follows its duration, like that
            // of a message waited for longer than the step timeout.
            TimeoutSeconds = step.Type == StepType.Pause
                ? Math.Max(1, step.PauseSeconds) + 60
                : Math.Max(Math.Max(1, step.TimeoutMinutes ?? package.TimeoutMinutes ?? 60) * 60,
                    step.Type == StepType.Message && step.MessageWait ? step.MessageSeconds + 60 : 0),
            ContinueOnError = step.ErrorMode == ErrorMode.Continue,
            StopSuccessOnError = step.ErrorMode == ErrorMode.StopSuccess,
            Disabled = !step.Enabled,
            CopyDestination = step.Type == StepType.CopyFiles ? step.CopyDestination : null,
            CopyPatterns = step.Type == StepType.CopyFiles ? PackageStep.SplitPatterns(step.CopyPatterns) : null,
            CopySubfolders = step.Type == StepType.CopyFiles && step.CopySubfolders,
            CopyOverwrite = step.Type == StepType.CopyFiles && step.CopyOverwrite,
            // Reboot and message go through the service, never through the logged-on user (who often lacks the right).
            RunAsLoggedOnUser = runAs == RunAsMode.LoggedOnUser && step.Type is not (StepType.Reboot or StepType.Message),
            ShutdownOnly = step.Type == StepType.Reboot && step.RebootShutdownOnly,
            WaitForUser = step.Type == StepType.Message && step.MessageWait,
            Conditions = RunnerConditions.From(step.Conditions),
            ExtraConditions = step.ExtraConditions?
                .Select(g => new RunnerConditionGroup { Label = g.Label, Conditions = RunnerConditions.From(g.Conditions) })
                .Where(g => g.Conditions != null).ToList() is { Count: > 0 } extras ? extras : null,
        };
    }

    /// <summary>
    /// This server (machine, process and start time), written in slot locks: a lock left by a
    /// server stopped mid-deployment can then be taken over instead of blocking its slot forever.
    /// </summary>
    private static readonly string Instance = OwnerOf(System.Diagnostics.Process.GetCurrentProcess());

    private static string OwnerOf(System.Diagnostics.Process process) =>
        $"{Environment.MachineName}|{process.Id}|{process.StartTime.ToUniversalTime().Ticks}";

    /// <summary>Is the server that placed this lock still running on this machine?</summary>
    private static bool OwnerAlive(string owner)
    {
        var parts = owner.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[1], out var pid)) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return OwnerOf(process) == owner; // same number but another start: number reused
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false; // no more process with this number
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true; // cannot check: when in doubt, the lock is kept
        }
    }

    /// <summary>Reserves a free slot using a lock file created exclusively.</summary>
    private void AcquireSlot()
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            var lockPath = Path.Combine(Root, $"slot-{i}.lock");
            if (!TryCreateLock(lockPath) && !TryReclaim(lockPath, Path.Combine(Root, $"slot-{i}"))) continue;
            _slot = i;
            if (Directory.Exists(SlotDir)) Directory.Delete(SlotDir, recursive: true); // left over from a crash
            return;
        }
        throw new DeployException($"Trop de déploiements simultanés sur ce poste ({MaxSlots}).");
    }

    /// <summary>Two deployments of this server never take the same lock at the same time.</summary>
    private static readonly Lock ReclaimGate = new();

    private static bool TryReclaim(string lockPath, string slotDir)
    {
        lock (ReclaimGate)
        {
            if (!CanReclaim(lockPath, slotDir, Instance, DateTime.UtcNow, OwnerAlive)) return false;
            try { File.Delete(lockPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            return TryCreateLock(lockPath);
        }
    }

    private static bool TryCreateLock(string lockPath)
    {
        try
        {
            using var stream = new FileStream(lockPath, FileMode.CreateNew);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(Instance));
            return true;
        }
        catch (IOException)
        {
            return false; // slot taken
        }
    }

    /// <summary>
    /// A lock can be taken over if it was placed, on this server's machine, by a server that is no longer running (or,
    /// with no author, by an old version more than a day ago) and no runner is still running in its slot.
    /// Another server still running on the same machine (test instance) keeps its locks.
    /// </summary>
    internal static bool CanReclaim(string lockPath, string slotDir, string instance, DateTime nowUtc, Func<string, bool> ownerAlive)
    {
        try
        {
            var owner = File.ReadAllText(lockPath).Trim();
            var stale = owner.Length == 0
                ? nowUtc - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromDays(1)
                : owner != instance && owner.Split('|')[0].Equals(instance.Split('|')[0], StringComparison.OrdinalIgnoreCase)
                    && !ownerAlive(owner);
            return stale && !(Directory.Exists(slotDir) && Directory.EnumerateFiles(slotDir, "*.exe").Any(IsInUse));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A running program cannot be opened for exclusive writing.</summary>
    private static bool IsInUse(string file)
    {
        try
        {
            using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
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

    private static void CopyDirectory(string source, string destination, CopyBatch copies)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            copies.Add(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    public static List<int> ParseCodes(string? codes)
    {
        var list = (codes ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => int.TryParse(c, out var v) ? (int?)v : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list : [0];
    }

    public static bool IsLocalComputer(string name)
    {
        var n = name.Trim().TrimStart('\\').Split('.')[0];
        return n.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || n.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || n == "127" || n == ".";
    }
}

/// <summary>Deployment error with a message readable by a human.</summary>
public class DeployException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The computer could not be reached: candidate for the retry queue.</summary>
    public bool Offline { get; init; }

    public static DeployException FromShareAccess(string computer, Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            new($"Accès refusé au partage ADMIN$ de {computer} : le compte de déploiement est-il admin de ce poste ?", ex),
        IOException io when io.HResult is unchecked((int)0x80070035) or unchecked((int)0x80070079) or unchecked((int)0x80070040) =>
            new($"{computer} est injoignable (éteint, pare-feu ou partage de fichiers désactivé).", ex) { Offline = true },
        IOException io when io.HResult == unchecked((int)0x8007052E) =>
            new("Nom d'utilisateur ou mot de passe de l'identifiant incorrect.", ex),
        _ => new($"Impossible d'accéder à {computer} : {ex.Message}", ex),
    };
}
