using System.Text.Json;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Un déploiement sur un PC : copie du runner et des fichiers dans un « slot »,
/// service temporaire, attente du résultat, nettoyage.
///
/// Sur la cible :
///   C:\Windows\DeployCraft\slot-N.lock
///   C:\Windows\DeployCraft\slot-N\DeployCraftRunner-N.exe, job.json, status.json, runner.log, exec\
/// </summary>
internal sealed class TargetSession(string computer, DeployCredentials credentials, Credential? account, string runnerExe)
{
    private const string RemoteRoot = @"C:\Windows\DeployCraft";
    private const int MaxSlots = 32;

    private readonly bool _isLocal = IsLocalComputer(computer);
    private int _slot;

    /// <summary>Dossier racine vu depuis le serveur (local ou via le partage ADMIN$).</summary>
    private string Root => _isLocal ? RemoteRoot : $@"\\{computer}\ADMIN$\DeployCraft";
    private string SlotDir => Path.Combine(Root, $"slot-{_slot}");
    private string LockFile => Path.Combine(Root, $"slot-{_slot}.lock");
    private string ServiceName => $"DeployCraftRunner-{_slot}";
    /// <summary>Service de la tranche en cours (suffixé -1, -2… quand il y a plusieurs identités).</summary>
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
            // Dans le try : une préparation ratée (fichier introuvable…) libère aussi le slot qu'elle a réservé.
            var (job, modes) = credentials.RunAs(account, computer, () => Prepare(package, startStep, runAs, copyMode));
            // Un redémarrage termine sa tranche : la suivante est lancée une fois le poste revenu.
            var tranches = RunAsPlan.Split(modes, [.. job.Steps.Select(s => s.Type == RebootType)]);

            // Une tranche par identité ; la suivante ne démarre qu'une fois le service de la précédente supprimé.
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
                // Annulé entre deux tranches : aucune nouvelle n'est lancée.
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

                // Redémarrage programmé par la dernière étape de la tranche (pas ignorée, pas en échec).
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
    /// Attend que le poste s'arrête (après le délai annoncé à l'utilisateur), puis qu'il réponde de nouveau, au plus le
    /// délai max de l'étape. Le slot (fichiers et verrou) reste sur le disque du poste pendant le redémarrage.
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

    /// <summary>Juste après le démarrage, le partage peut répondre avant le gestionnaire de services : quelques essais.</summary>
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

    /// <summary>Écrit les consignes de la tranche et crée son service, sous le compte de déploiement ou en Système.</summary>
    private void StartTranche(RunnerJob job, bool asDeployUser)
    {
        File.WriteAllText(Path.Combine(SlotDir, "job.json"), JsonSerializer.Serialize(job));
        File.Delete(Path.Combine(SlotDir, "status.json"));

        string? serviceAccount = null, password = null;
        if (asDeployUser && account != null)
        {
            // Le compte local « .\utilisateur » se cherche sous le nom du poste.
            serviceAccount = account.Account;
            LogonRight.GrantServiceLogon(_isLocal ? null : computer, DeployCredentials.LogonDomain(account, computer) + "\\" + account.User);
            password = credentials.Password(account);
        }
        using var scm = new ServiceControl(_isLocal ? null : computer);
        scm.CreateAndStart(_service!, Path.Combine(RemoteRoot, $"slot-{_slot}", RunnerName), serviceAccount, password);
    }

    /// <summary>Durée pendant laquelle le suivi supporte un poste injoignable (le runner, lui, continue sur le poste).</summary>
    internal static readonly TimeSpan ReconnectGrace = TimeSpan.FromMinutes(10);

    /// <summary>Suit status.json jusqu'à la fin de la tranche ; renvoie son dernier état.</summary>
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
                // Coupure réseau : on réessaie un moment au lieu d'abandonner un poste qui continue son travail.
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
                // Le service a pu finir entre les deux appels : dernières lectures, le temps que le partage montre le
                // status.json final (il peut sembler absent ou ancien quelques secondes juste après son remplacement).
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

    /// <summary>Erreur d'accès au poste qui peut passer d'elle-même (réseau, partage, gestionnaire de services distant).</summary>
    internal static bool IsNetworkError(Exception ex) =>
        ex is IOException or System.ComponentModel.Win32Exception;

    /// <summary>
    /// Relit l'état jusqu'à trouver « done », au plus <paramref name="attempts"/> fois à une seconde d'intervalle ;
    /// renvoie le dernier état lu (ou null).
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
    /// Windows garde quelques secondes en mémoire le contenu d'un dossier lu à travers un partage (et l'absence d'un
    /// fichier). Créer puis supprimer un fichier vide dans le dossier oblige à le relire pour de bon.
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
            // Sans effet sur la lecture : on lit quand même.
        }
    }

    private async Task CheckReachableAsync(CancellationToken ct)
    {
        if (_isLocal) return;
        if (await ProbeAsync(computer, ct) is { } offline) throw offline;
    }

    /// <summary>
    /// Test rapide : le nom se résout-il et le partage de fichiers (port 445) répond-il ?
    /// Renvoie l'erreur « hors ligne » correspondante, ou null si le poste est joignable.
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
                // adresse suivante
            }
        }
        return new DeployException($"{computer} est hors ligne (éteint, ou partage de fichiers bloqué par le pare-feu).") { Offline = true };
    }

    /// <summary>Copie le runner et les fichiers, prépare les consignes de toutes les étapes et l'identité de chacune.</summary>
    private (RunnerJob Job, List<RunAsMode> Modes) Prepare(Package package, int startStep, RunAsMode runAs, CopyMode copyMode)
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
        File.Copy(runnerExe, Path.Combine(SlotDir, RunnerName), overwrite: true);
        var config = runnerExe + ".config";
        if (File.Exists(config)) File.Copy(config, Path.Combine(SlotDir, RunnerName + ".config"), overwrite: true);

        var job = new RunnerJob { Conditions = RunnerConditions.From(package.Conditions), FirstNumber = startStep };
        var modes = new List<RunAsMode>();
        foreach (var step in StepsFrom(package, startStep))
        {
            var mode = RunAsPlan.Effective(step, runAs, account != null);
            modes.Add(mode);
            var jobStep = ToJobStep(step, package, mode);

            // Étape désactivée : transmise pour apparaître « Ignoré », mais aucun fichier n'est copié ni vérifié.
            if (step.Type == StepType.Install && step.Enabled)
            {
                if (string.IsNullOrWhiteSpace(step.File)) throw new DeployException($"Étape « {jobStep.Name} » : aucun fichier d'installation.");
                jobStep.Command = Path.GetFileName(step.File);
                // Pull : le poste lit lui-même le partage, avec l'identité du runner. Un chemin local reste en push.
                if (copyMode == CopyMode.Pull && step.File.StartsWith(@"\\"))
                {
                    jobStep.SourcePath = step.File;
                    jobStep.IncludeDirectory = step.IncludeDirectory;
                    job.Steps.Add(jobStep);
                    continue;
                }
                // Push : le fichier est lu par le serveur (avec le compte de déploiement s'il est sur un partage).
                if (!File.Exists(step.File)) throw new DeployException($"Fichier d'installation introuvable : {step.File}");
                if (step.IncludeDirectory) CopyDirectory(Path.GetDirectoryName(step.File)!, exec);
                else File.Copy(step.File, Path.Combine(exec, Path.GetFileName(step.File)), overwrite: true);
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
                    // Push : le serveur dépose les fichiers retenus dans exec\copie-N, que le poste copie ensuite tels quels.
                    jobStep.Command = $"copie-{startStep + job.Steps.Count}";
                    StageCopy(step.CopySource, step.CopyPatterns, step.CopySubfolders, Path.Combine(exec, jobStep.Command));
                    jobStep.CopyPatterns = ["*"];
                    jobStep.CopySubfolders = true;
                }
            }
            job.Steps.Add(jobStep);
        }

        return (job, modes);
    }

    /// <summary>
    /// Copie de fichiers en push : dépose dans <paramref name="target"/> le fichier <paramref name="source"/>, ou les
    /// fichiers du dossier qui suivent les motifs (avec les sous-dossiers si demandé), en gardant leur arborescence.
    /// </summary>
    internal static void StageCopy(string source, string? patterns, bool subfolders, string target)
    {
        Directory.CreateDirectory(target);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(target, Path.GetFileName(source)), overwrite: true);
            return;
        }
        if (!Directory.Exists(source)) throw new DeployException($"Fichier ou dossier à copier introuvable : {source}");

        var option = subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        // Comme sur le poste : les sous-dossiers sont recréés même vides.
        if (subfolders)
            foreach (var dir in Directory.EnumerateDirectories(source, "*", option))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var pattern in PackageStep.SplitPatterns(patterns))
            foreach (var file in Directory.EnumerateFiles(source, pattern, option))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
    }

    /// <summary>Étapes envoyées au poste quand on (re)commence à l'étape N.</summary>
    internal static IEnumerable<PackageStep> StepsFrom(Package package, int startStep) =>
        package.Steps.Skip(Math.Max(0, startStep - 1));

    /// <summary>Consignes d'une étape pour le runner (hors fichiers à copier), lancée sous l'identité <paramref name="runAs"/>.</summary>
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
            // Copie : robocopy réussit de 0 à 7 ; 8 à 15 = des fichiers n'ont pas pu être copiés ; 16 = erreur grave.
            SuccessCodes = step.Type == StepType.CopyFiles ? [.. Enumerable.Range(0, step.CopyIgnoreErrors ? 16 : 8)] : ParseCodes(step.SuccessCodes),
            // Une pause dure ce qu'elle dure : son délai (qui compte dans l'attente du serveur) suit sa durée, comme celui
            // d'un message attendu plus longtemps que le délai de l'étape.
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
            // Redémarrage et message passent par le service, jamais par l'utilisateur connecté (qui n'en a souvent pas le droit).
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
    /// Ce lancement du serveur, écrit dans les verrous de slot : un verrou laissé par un lancement précédent (serveur
    /// arrêté en plein déploiement) peut ainsi être repris au lieu de bloquer son slot pour toujours.
    /// </summary>
    private static readonly string Instance = $"{Environment.MachineName}|{Guid.NewGuid():N}";

    /// <summary>Réserve un slot libre grâce à un fichier verrou créé de façon exclusive.</summary>
    private void AcquireSlot()
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            var lockPath = Path.Combine(Root, $"slot-{i}.lock");
            if (!TryCreateLock(lockPath) && !TryReclaim(lockPath, Path.Combine(Root, $"slot-{i}"))) continue;
            _slot = i;
            if (Directory.Exists(SlotDir)) Directory.Delete(SlotDir, recursive: true); // reste d'un crash
            return;
        }
        throw new DeployException($"Trop de déploiements simultanés sur ce poste ({MaxSlots}).");
    }

    /// <summary>Deux déploiements de ce serveur ne reprennent jamais le même verrou en même temps.</summary>
    private static readonly Lock ReclaimGate = new();

    private static bool TryReclaim(string lockPath, string slotDir)
    {
        lock (ReclaimGate)
        {
            if (!CanReclaim(lockPath, slotDir, Instance, DateTime.UtcNow)) return false;
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
            return false; // slot occupé
        }
    }

    /// <summary>
    /// Un verrou peut être repris s'il a été posé par un lancement précédent de ce serveur (ou, sans auteur, par une
    /// ancienne version il y a plus d'un jour) et qu'aucun runner ne tourne encore dans son slot.
    /// </summary>
    internal static bool CanReclaim(string lockPath, string slotDir, string instance, DateTime nowUtc)
    {
        try
        {
            var owner = File.ReadAllText(lockPath).Trim();
            var stale = owner.Length == 0
                ? nowUtc - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromDays(1)
                : owner != instance && owner.Split('|')[0].Equals(instance.Split('|')[0], StringComparison.OrdinalIgnoreCase);
            return stale && !(Directory.Exists(slotDir) && Directory.EnumerateFiles(slotDir, "*.exe").Any(IsInUse));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Un programme en cours d'exécution ne peut pas être ouvert en écriture exclusive.</summary>
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
    /// Lit status.json sans bloquer le runner (qui le remplace à chaque changement) ; réessaie un peu si le fichier
    /// est verrouillé ou à moitié écrit, sinon null : on relira au prochain tour.
    /// </summary>
    internal static RunnerStatus? ReadStatus(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Pas de File.Exists : à travers un partage, il peut répondre « absent » d'après un cache périmé.
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

        // L'exe du service peut rester verrouillé quelques instants après l'arrêt.
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

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
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

/// <summary>Erreur de déploiement avec un message lisible par un humain.</summary>
public class DeployException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Le poste n'a pas pu être joint : candidat à la file de réessai.</summary>
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
