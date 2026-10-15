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
    private string RunnerName => $"{ServiceName}.exe";

    public async Task RunAsync(Package package, int startStep, RunAsMode runAs, CopyMode copyMode,
        Action<string> stage, Action<RunnerStatus> progress, CancellationToken ct)
    {
        stage("Résolution du nom");
        await CheckReachableAsync(ct);

        stage("Copie des fichiers");
        var job = credentials.RunAs(account, computer, () => Prepare(package, startStep, runAs, copyMode));

        try
        {
            stage("Démarrage du runner");
            credentials.RunAs(account, computer, () =>
            {
                string? serviceAccount = null, password = null;
                if (runAs == RunAsMode.DeployUser && account != null)
                {
                    // Le compte local « .\utilisateur » se cherche sous le nom du poste.
                    serviceAccount = account.Account;
                    LogonRight.GrantServiceLogon(_isLocal ? null : computer, DeployCredentials.LogonDomain(account, computer) + "\\" + account.User);
                    password = credentials.Password(account);
                }
                using var scm = new ServiceControl(_isLocal ? null : computer);
                scm.CreateAndStart(ServiceName, Path.Combine(RemoteRoot, $"slot-{_slot}", RunnerName), serviceAccount, password);
            });

            var deadline = DateTime.UtcNow.AddSeconds(job.Steps.Sum(s => s.TimeoutSeconds) + 120);
            var statusPath = Path.Combine(SlotDir, "status.json");
            while (true)
            {
                await Task.Delay(1000, ct);
                var (status, running) = credentials.RunAs(account, computer, () =>
                {
                    using var scm = new ServiceControl(_isLocal ? null : computer);
                    return (ReadStatus(statusPath), scm.IsRunning(ServiceName));
                });

                if (status != null)
                {
                    progress(status);
                    if (status.State == "done") return;
                }
                if (!running)
                {
                    // Dernière lecture : le service a pu finir entre les deux appels.
                    status = credentials.RunAs(account, computer, () => ReadStatus(statusPath));
                    if (status != null) progress(status);
                    if (status?.State == "done") return;
                    throw new DeployException("Le runner s'est arrêté sans rendre de résultat (voir runner.log sur la cible).");
                }
                if (DateTime.UtcNow > deadline)
                    throw new DeployException("Délai global dépassé : le runner ne répond plus.");
            }
        }
        finally
        {
            stage("Nettoyage");
            try { credentials.RunAs(account, computer, Cleanup); }
            catch { /* un nettoyage raté ne doit pas masquer le vrai résultat */ }
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

    private RunnerJob Prepare(Package package, int startStep, RunAsMode runAs, CopyMode copyMode)
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

        var forComputer = ForComputer(package);
        var job = new RunnerJob { Conditions = RunnerConditions.From(forComputer.Conditions) };
        foreach (var step in StepsFrom(forComputer, startStep))
        {
            var jobStep = ToJobStep(step, package, runAs);

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
            job.Steps.Add(jobStep);
        }

        File.WriteAllText(Path.Combine(SlotDir, "job.json"), JsonSerializer.Serialize(job));
        return job;
    }

    /// <summary>Copie du paquet où $(Poste) est remplacé par le nom de ce poste (avant la copie des fichiers).</summary>
    private Package ForComputer(Package package) => WithComputer(package, computer);

    internal static Package WithComputer(Package package, string computer)
    {
        var copy = JsonSerializer.Deserialize<Package>(JsonSerializer.Serialize(package))!;
        copy.ReplaceVariables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [VariableText.Computer] = computer });
        return copy;
    }

    /// <summary>
    /// Étapes envoyées au poste quand on (re)commence à l'étape N : les étapes rangées par section (Avant,
    /// Principales, Après), à partir de N. La section Après suit donc toujours une reprise dans les sections précédentes.
    /// </summary>
    internal static IEnumerable<PackageStep> StepsFrom(Package package, int startStep) =>
        package.Steps.OrderBy(s => Package.SectionRank(s.Section)).Skip(Math.Max(0, startStep - 1));

    /// <summary>Consignes d'une étape pour le runner (hors fichiers à copier).</summary>
    internal static RunnerJobStep ToJobStep(PackageStep step, Package package, RunAsMode runAs)
    {
        if (step.Type == StepType.Nested)
            throw new DeployException($"Étape « {step.Name} » : paquet imbriqué non remplacé par ses étapes.");
        return new()
        {
            Name = string.IsNullOrWhiteSpace(step.Name) ? step.Type.ToString() : step.Name,
            Section = step.Section.ToString().ToLowerInvariant(),
            Type = step.Type.ToString().ToLowerInvariant(),
            Arguments = step.Arguments,
            Command = step.Command,
            Seconds = step.Type == StepType.Pause ? step.PauseSeconds : 0,
            SuccessCodes = ParseCodes(step.SuccessCodes),
            // Une pause dure ce qu'elle dure : son délai (qui compte dans l'attente du serveur) suit sa durée.
            TimeoutSeconds = step.Type == StepType.Pause
                ? Math.Max(1, step.PauseSeconds) + 60
                : Math.Max(1, step.TimeoutMinutes ?? package.TimeoutMinutes ?? 60) * 60,
            ContinueOnError = step.ErrorMode == ErrorMode.Continue,
            StopSuccessOnError = step.ErrorMode == ErrorMode.StopSuccess,
            Disabled = !step.Enabled,
            RunAsLoggedOnUser = runAs == RunAsMode.LoggedOnUser,
            Conditions = RunnerConditions.From(step.Conditions),
            ExtraConditions = step.ExtraConditions?
                .Select(g => new RunnerConditionGroup { Label = g.Label, Conditions = RunnerConditions.From(g.Conditions) })
                .Where(g => g.Conditions != null).ToList() is { Count: > 0 } extras ? extras : null,
        };
    }

    /// <summary>Réserve un slot libre grâce à un fichier verrou créé de façon exclusive.</summary>
    private void AcquireSlot()
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            try
            {
                using (new FileStream(Path.Combine(Root, $"slot-{i}.lock"), FileMode.CreateNew)) { }
                _slot = i;
                if (Directory.Exists(SlotDir)) Directory.Delete(SlotDir, recursive: true); // reste d'un crash
                return;
            }
            catch (IOException) { /* slot occupé */ }
        }
        throw new DeployException($"Trop de déploiements simultanés sur ce poste ({MaxSlots}).");
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
                if (!File.Exists(path)) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize<RunnerStatus>(reader.ReadToEnd());
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
        using (var scm = new ServiceControl(_isLocal ? null : computer)) scm.Remove(ServiceName);

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
