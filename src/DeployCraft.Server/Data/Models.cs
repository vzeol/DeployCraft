namespace DeployCraft.Server.Data;

/// <summary>Nested: nested package, replaced by its steps at launch (never sent as is to the computer).</summary>
public enum StepType { Install, Command, PowerShell, Pause, Nested, CopyFiles, Reboot, Message }

/// <summary>When a step fails: stop as failed, continue, or stop the package while counting the computer as succeeded.</summary>
public enum ErrorMode { Stop, Continue, StopSuccess }

/// <summary>Identity under which the steps run on the computer.</summary>
public enum RunAsMode
{
    /// <summary>The temporary service runs under the deployment account.</summary>
    DeployUser,
    /// <summary>The temporary service runs as Local System.</summary>
    LocalSystem,
    /// <summary>The steps are launched in the logged-on user's session.</summary>
    LoggedOnUser,
}

/// <summary>Install: operation done on an .msi file (.msu: install or uninstall only).</summary>
public enum MsiOperation { Install, Repair, Uninstall }

/// <summary>Install: restart requested from Windows Installer after an .msi, .msp or .msu ("non défini": nothing imposed).</summary>
public enum MsiRestart { Never, Always, Prompt, NotSet }

/// <summary>Push: the server copies the files to the computer. Pull: the computer reads them itself from the share.</summary>
public enum CopyMode { Push, Pull }

public class PackageStep
{
    public string Name { get; set; } = "";
    /// <summary>Disabled: kept in the package but never run ("Ignoré" in the tracking view).</summary>
    public bool Enabled { get; set; } = true;
    public StepType Type { get; set; } = StepType.Install;
    /// <summary>Install: file path (local or UNC) on the server side.</summary>
    public string? File { get; set; }
    /// <summary>Install: copy the whole folder of the file (multi-file installers).</summary>
    public bool IncludeDirectory { get; set; }
    /// <summary>Install: parameters (silent). PowerShell: script arguments.</summary>
    public string? Arguments { get; set; }
    /// <summary>Install (.msi, .msu): install, repair or uninstall.</summary>
    public MsiOperation MsiOperation { get; set; }
    /// <summary>Install (.msi, .msp, .msu): restart after the installation.</summary>
    public MsiRestart MsiRestart { get; set; }
    /// <summary>Install (.msi, .msp, .msu): no window at all (strongly advised: nobody can click).</summary>
    public bool MsiQuiet { get; set; } = true;
    /// <summary>Install: hand-written command line, run as is in the working folder (empty = generated).</summary>
    public string? CustomCommandLine { get; set; }
    /// <summary>Install: keep the installation file on the computer after the step.</summary>
    public bool KeepInstallFile { get; set; }
    /// <summary>Install, Command, PowerShell: files copied next to the command before the step (one path per line).</summary>
    public string? AdditionalFiles { get; set; }
    /// <summary>Command: cmd line. PowerShell: script content.</summary>
    public string? Command { get; set; }
    public string SuccessCodes { get; set; } = Defaults.SuccessCodes;
    /// <summary>Pause: wait time on the computer, in seconds.</summary>
    public int PauseSeconds { get; set; } = 30;
    /// <summary>Nested package: package whose steps take the place of this one at launch.</summary>
    public long? NestedPackageId { get; set; }
    /// <summary>File copy: file or folder to copy (local or UNC path) on the server side.</summary>
    public string? CopySource { get; set; }
    /// <summary>File copy: folder on the computer (environment variables accepted), created if missing.</summary>
    public string? CopyDestination { get; set; }
    /// <summary>File copy: patterns separated by ";" (folder source only).</summary>
    public string CopyPatterns { get; set; } = "*";
    /// <summary>File copy: also copy the subfolders of the source.</summary>
    public bool CopySubfolders { get; set; } = true;
    /// <summary>File copy: replace files already present (otherwise they are kept).</summary>
    public bool CopyOverwrite { get; set; } = true;
    /// <summary>File copy: a file that cannot be replaced (in use...) is not an error.</summary>
    public bool CopyIgnoreErrors { get; set; }
    /// <summary>Restart: message shown to the logged-on user (optional).</summary>
    public string? RebootMessage { get; set; }
    /// <summary>Restart: delay before the restart, in seconds (the logged-on user is warned).</summary>
    public int RebootDelaySeconds { get; set; } = 60;
    /// <summary>Restart: shut the computer down instead of restarting it (the following steps are skipped).</summary>
    public bool RebootShutdownOnly { get; set; }
    /// <summary>Message: title of the window shown to the logged-on user.</summary>
    public string MessageTitle { get; set; } = "Information";
    /// <summary>Message: text shown to the logged-on user.</summary>
    public string? MessageText { get; set; }
    /// <summary>Message: display duration in seconds (0 = until OK is clicked).</summary>
    public int MessageSeconds { get; set; }
    /// <summary>Message: wait for the user to click OK (or for the display to end) before the next step.</summary>
    public bool MessageWait { get; set; }
    /// <summary>
    /// Conditions to meet in addition to the step's own: set at launch on the steps of a nested package
    /// (conditions of the "nested package" step and of the nested package itself).
    /// </summary>
    public List<ConditionGroup>? ExtraConditions { get; set; }
    /// <summary>Max time for the step (null = the package's, otherwise the Preferences').</summary>
    public int? TimeoutMinutes { get; set; }
    /// <summary>
    /// Identity of the step (null = the deployment's: package, or choice from Déployer une fois / schedule).
    /// Consecutive steps with the same identity are launched together on the computer.
    /// </summary>
    public RunAsMode? RunAs { get; set; }
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Stop;
    /// <summary>Conditions of the step: if not met on a computer, the step is skipped there.</summary>
    public Conditions Conditions { get; set; } = new();

    /// <summary>Additional files, one per line (empty lines ignored).</summary>
    public static List<string> SplitLines(string? text) =>
        [.. (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>File copy patterns, separated by ";" or "," (none = all files).</summary>
    public static List<string> SplitPatterns(string? text)
    {
        var patterns = (text ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return patterns.Count > 0 ? patterns : ["*"];
    }
}

/// <summary>Inherited conditions, with their origin to explain a skipped step.</summary>
public class ConditionGroup
{
    /// <summary>For example "Conditions du paquet imbriqué "Outils""; followed by "non remplies" in the tracking view.</summary>
    public string Label { get; set; } = "";
    public Conditions Conditions { get; set; } = new();
}

/// <summary>Windows versions accepted by a condition (none checked = all).</summary>
[Flags]
public enum WindowsVersions
{
    None = 0,
    Windows10 = 1,
    Windows11 = 2,
    Server = 4,
    /// <summary>Windows 8.1 and earlier versions.</summary>
    Older = 8,
}

public enum ArchitectureCondition { Any, X64, X86 }

public enum LoggedOnCondition { Any, LoggedOn, NotLoggedOn }

/// <summary>Condition on a file or a registry key.</summary>
public enum PresenceCondition { None, Exists, NotExists }

/// <summary>
/// Conditions of a package or a step, checked on the computer just before running.
/// All must be met; otherwise the step (or the whole package) is skipped, without failure.
/// </summary>
public class Conditions
{
    public WindowsVersions Windows { get; set; }
    public ArchitectureCondition Architecture { get; set; }
    /// <summary>Minimum PowerShell version (3, 4, 5 or 7; null = none).</summary>
    public int? MinPowerShell { get; set; }
    public LoggedOnCondition LoggedOn { get; set; }
    public PresenceCondition File { get; set; }
    /// <summary>File or folder on the computer (environment variables accepted).</summary>
    public string? FilePath { get; set; }
    public PresenceCondition Registry { get; set; }
    /// <summary>Registry key, for example HKLM\SOFTWARE\Publisher\Software.</summary>
    public string? RegistryKey { get; set; }
    /// <summary>Name of a value of the key (empty = the key itself).</summary>
    public string? RegistryValue { get; set; }

    public bool IsEmpty => Windows == WindowsVersions.None && Architecture == ArchitectureCondition.Any && MinPowerShell == null
                           && LoggedOn == LoggedOnCondition.Any && File == PresenceCondition.None && Registry == PresenceCondition.None;
}

/// <summary>
/// Offline computers: wake-up (Wake-on-LAN) and retries. A computer is always tested
/// (DNS name and file share) before the copy; these settings say what to do if it does not answer.
/// </summary>
public class OfflineSettings
{
    /// <summary>Send a Wake-on-LAN wake-up packet, then wait for the computer to answer.</summary>
    public bool WakeOnLan { get; set; }
    public int WakeWaitMinutes { get; set; } = 3;
    /// <summary>Retry an offline computer later instead of marking it as failed.</summary>
    public bool Retry { get; set; }
    /// <summary>Total number of attempts, the first included.</summary>
    public int RetryAttempts { get; set; } = 5;
    public int RetryIntervalMinutes { get; set; } = 30;
}

/// <summary>Server load: computers processed at the same time and throughput of file copies to the computers.</summary>
public class PerformanceSettings
{
    public const int MaxParallel = 100;
    public const int MaxCopyLimit = 10000;

    /// <summary>Computers deployed at the same time, across all deployments.</summary>
    public int ParallelTargets { get; set; } = 8;
    /// <summary>Total throughput of copies from the server to the computers, in Mbit/s (0 = unlimited).</summary>
    public int CopyLimitMbps { get; set; }

    /// <summary>Error message if a setting is out of range, otherwise null.</summary>
    public static string? Validate(PerformanceSettings p) =>
        p.ParallelTargets is < 1 or > MaxParallel ? Msg.Format("PerformanceSettings_ParallelRange", MaxParallel)
        : p.CopyLimitMbps is < 0 or > MaxCopyLimit ? Msg.Format("PerformanceSettings_CopyLimitRange", MaxCopyLimit)
        : null;
}

/// <summary>Automatic cleanup of the deployment history (Préférences › Déploiement).</summary>
public class HistorySettings
{
    public const int MinDays = 1;
    public const int MaxDays = 3650;

    /// <summary>Delete deployments finished more than <see cref="KeepDays"/> days ago (off by default).</summary>
    public bool Cleanup { get; set; }
    public int KeepDays { get; set; } = 30;

    /// <summary>Error message if the number of days is out of range, otherwise null.</summary>
    public static string? Validate(int keepDays) =>
        keepDays is < MinDays or > MaxDays ? Msg.Format("HistorySettings_DaysRange", MinDays, MaxDays) : null;
}

/// <summary>Network card of a computer: MAC address and broadcast address of its subnet (to wake it up).</summary>
public record NetworkCard(string Mac, string? Broadcast);

/// <summary>Default package settings (Préférences › Déploiement and Hors ligne).</summary>
public class DeploymentDefaults
{
    public RunAsMode RunAs { get; set; } = RunAsMode.DeployUser;
    public CopyMode CopyMode { get; set; } = CopyMode.Push;
    public int TimeoutMinutes { get; set; } = 60;
    public OfflineSettings Offline { get; set; } = new();
    public HistorySettings History { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}

public class Package
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<PackageStep> Steps { get; set; } = [];
    /// <summary>null = the Preferences value.</summary>
    public RunAsMode? RunAs { get; set; }
    /// <summary>null = the Preferences value.</summary>
    public CopyMode? CopyMode { get; set; }
    /// <summary>Max time of steps that do not set one (null = the Preferences value).</summary>
    public int? TimeoutMinutes { get; set; }
    /// <summary>Conditions of the package: if not met on a computer, none of its steps runs there.</summary>
    public Conditions Conditions { get; set; } = new();
    /// <summary>null = the Preferences' offline settings.</summary>
    public OfflineSettings? Offline { get; set; }
    /// <summary>Folder in the package tree (null = at the root). Changed only by a move.</summary>
    public long? FolderId { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// Ready-to-deploy copy: each setting left on "default" takes its value (step → package → Preferences).
    /// The deployment history thus keeps the values actually used.
    /// </summary>
    public Package Resolve(DeploymentDefaults defaults, Func<long, Package?>? lookup = null)
    {
        var copy = Flatten(lookup ?? (_ => null), defaults.TimeoutMinutes);
        copy.RunAs ??= defaults.RunAs;
        copy.CopyMode ??= defaults.CopyMode;
        copy.TimeoutMinutes ??= defaults.TimeoutMinutes;
        copy.Offline ??= Clone(defaults.Offline);
        foreach (var step in copy.Steps) step.TimeoutMinutes ??= copy.TimeoutMinutes;
        return copy;
    }

    /// <summary>Accepted nested package levels (a package containing one that contains one...).</summary>
    public const int MaxNestingDepth = 5;

    /// <summary>
    /// Copy where each "nested package" step is replaced by the steps of that package: named "Step › Sub-step",
    /// in place of the replaced step (in their order), disabled if it is, with its conditions and those of the nested package
    /// as additional conditions. Timeout: the sub-step's, otherwise the nested package's, otherwise
    /// <paramref name="defaultTimeout"/>. Package not found, loop or too many levels: <see cref="ArgumentException"/>.
    /// </summary>
    public Package Flatten(Func<long, Package?> lookup, int? defaultTimeout = null)
    {
        var copy = Clone(this);
        copy.Steps = Expand(copy.Steps, lookup, defaultTimeout, [Id], 0);
        return copy;
    }

    private static List<PackageStep> Expand(List<PackageStep> steps, Func<long, Package?> lookup, int? defaultTimeout, List<long> path, int depth)
    {
        var result = new List<PackageStep>();
        foreach (var step in steps)
        {
            if (step.Type != StepType.Nested)
            {
                result.Add(step);
                continue;
            }

            var nested = NestedPackage(step, lookup, path, depth);
            var label = string.IsNullOrWhiteSpace(step.Name) ? nested.Name : step.Name;
            path.Add(nested.Id);
            var inner = Expand(Clone(nested.Steps), lookup, defaultTimeout, path, depth + 1);
            path.RemoveAt(path.Count - 1);

            foreach (var s in inner)
            {
                s.Name = $"{label} › {(string.IsNullOrWhiteSpace(s.Name) ? s.Type.ToString() : s.Name)}";
                s.Enabled = step.Enabled && s.Enabled;
                s.RunAs ??= step.RunAs;
                s.TimeoutMinutes ??= nested.TimeoutMinutes ?? defaultTimeout;
                var extras = new List<ConditionGroup>();
                if (!step.Conditions.IsEmpty) extras.Add(new() { Label = Msg.Format("Package_StepConditions", label), Conditions = step.Conditions });
                if (!nested.Conditions.IsEmpty) extras.Add(new() { Label = Msg.Format("Package_NestedConditions", nested.Name), Conditions = nested.Conditions });
                extras.AddRange(s.ExtraConditions ?? []);
                s.ExtraConditions = extras.Count > 0 ? extras : null;
                result.Add(s);
            }
        }
        return result;
    }

    private static Package NestedPackage(PackageStep step, Func<long, Package?> lookup, List<long> path, int depth)
    {
        var title = string.IsNullOrWhiteSpace(step.Name) ? Msg.Get("Package_NestedUntitled") : Msg.Format("Package_QuotedName", step.Name);
        if (step.NestedPackageId is not { } id) throw new ArgumentException(Msg.Format("Package_NestedChoose", title));
        var nested = lookup(id) ?? throw new ArgumentException(Msg.Format("Package_NestedNotFound", title, id));
        if (path.Contains(id))
        {
            var loop = path.Skip(path.IndexOf(id)).Append(id).Select(i => lookup(i)?.Name ?? Msg.Format("Package_NumberedId", i));
            throw new ArgumentException(Msg.Format("Package_NestedLoop", title, string.Join(" › ", loop)));
        }
        if (depth >= MaxNestingDepth) throw new ArgumentException(Msg.Format("Package_NestedTooDeep", title, MaxNestingDepth));
        return nested;
    }

    /// <summary>Packages that use <paramref name="id"/> as a nested package (it cannot be deleted).</summary>
    public static List<Package> UsersOf(IEnumerable<Package> packages, long id) =>
        [.. packages.Where(p => p.Id != id && p.Steps.Any(s => s.Type == StepType.Nested && s.NestedPackageId == id))];

    private static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
}

public enum DeploymentStatus { Queued, Running, Successful, Failed }

public enum TargetStatus { Queued, Running, Successful, Failed }

public class Deployment
{
    public long Id { get; set; }
    public long PackageId { get; set; }
    public string PackageName { get; set; } = "";
    /// <summary>Copy of the package at launch time: changing the package afterwards does not affect the history.</summary>
    public Package Snapshot { get; set; } = new();
    public DeploymentStatus Status { get; set; }
    /// <summary>Windows user of the console who launched the deployment.</summary>
    public string? LaunchedBy { get; set; }
    /// <summary>Deployment account used (at launch time).</summary>
    public string? RunAs { get; set; }
    /// <summary>Credential used (null = none: the service's identity).</summary>
    public long? CredentialId { get; set; }
    /// <summary>Lowest start step among the computers (display); each computer has its own.</summary>
    public int StartStep { get; set; } = 1;
    public RunAsMode RunAsMode { get; set; } = RunAsMode.DeployUser;
    public CopyMode CopyMode { get; set; } = CopyMode.Push;
    public DateTime CreatedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Schedule that launched this deployment (null = launched by hand).</summary>
    public long? ScheduleId { get; set; }
    public string? ScheduleName { get; set; }
    /// <summary>Computers still waiting for a slot after this delay are abandoned (null = never).</summary>
    public int? QueueTimeoutMinutes { get; set; }
    public List<DeploymentTarget> Targets { get; set; } = [];
}

public class DeploymentTarget
{
    public long Id { get; set; }
    public long DeploymentId { get; set; }
    public string Computer { get; set; } = "";
    /// <summary>First step run on this computer (1 = the whole package; higher to resume at the failed step).</summary>
    public int StartStep { get; set; } = 1;
    /// <summary>Goes ahead of non-priority computers waiting for a slot.</summary>
    public bool Prioritized { get; set; }
    public TargetStatus Status { get; set; }
    /// <summary>Current step, for display ("Copie des fichiers", "Étape 2/3"...).</summary>
    public string? Stage { get; set; }
    public string? Error { get; set; }
    public List<StepOutcome> Steps { get; set; } = [];
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
}

public class StepOutcome
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public int? ExitCode { get; set; }
    public string? CommandLine { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Number of times the step ran on this computer in this deployment (retries included).</summary>
    public int Runs { get; set; }

    /// <summary>The step ran (or is running): neither skipped nor not yet started.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Ran => StartedUtc != null && State is "success" or "failed" or "stopped" or "running";

    /// <summary>
    /// Short message about the result: reason for a skipped step, error, success that requires a restart. Other
    /// success codes (for example those of a file copy) have no note, so as not to worry.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Note => State switch
    {
        "skipped" => Short(Error) ?? Msg.Get("StepOutcome_Skipped"),
        "failed" => Short(Error) ?? (ExitCode is { } code ? Msg.Format("StepOutcome_NotSuccessCode", code) : Msg.Get("StepOutcome_Failed")),
        "stopped" => Short(Error) is { } error ? Msg.Format("StepOutcome_StoppedWithError", error) : Msg.Get("StepOutcome_Stopped"),
        "success" => ExitCode switch
        {
            3010 => Msg.Get("StepOutcome_RestartNeeded"),
            1641 => Msg.Get("StepOutcome_RestartStarted"),
            2359302 => Msg.Get("StepOutcome_AlreadyInstalled"),
            _ => "",
        },
        _ => "",
    };

    /// <summary>First line of a message, shortened for a column.</summary>
    private static string? Short(string? text)
    {
        var line = text?.Split('\n')[0].Trim();
        if (string.IsNullOrEmpty(line)) return null;
        return line.Length > 160 ? line[..159] + "…" : line;
    }
}

/// <summary>A target computer and the step to start at.</summary>
public record TargetRequest(string Computer, int StartStep = 1);

/// <summary>Options of a "Déployer une fois"; null = package setting (credential: the default one).</summary>
public record DeployOptions(RunAsMode? RunAs = null, CopyMode? CopyMode = null, bool Prioritize = false, long? CredentialId = null);

/// <summary>
/// Saved credential: an administrator account on the computers. The encrypted password
/// never leaves the service. Without a domain (or "."): the local account of each computer.
/// </summary>
public class Credential
{
    public long Id { get; set; }
    public string? Domain { get; set; }
    public string User { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Used when a deployment or a schedule does not designate one.</summary>
    public bool IsDefault { get; set; }
    /// <summary>Password that cannot be decrypted (database copied from another server...): to be re-entered.</summary>
    public bool PasswordUnreadable { get; set; }

    public bool IsLocal => NormalizeDomain(Domain) == null;

    /// <summary>"", ".", ".\" and "./" designate the local account of each computer (null); otherwise the domain, without trailing slash.</summary>
    public static string? NormalizeDomain(string? domain)
    {
        var d = domain?.Trim().TrimEnd('\\', '/').Trim();
        return string.IsNullOrEmpty(d) || d == "." ? null : d;
    }

    /// <summary>Cleans up the input, including "DOMAINE\utilisateur" or ".\utilisateur" typed in full in Utilisateur.</summary>
    public Credential Normalize()
    {
        User = User.Trim();
        if (NormalizeDomain(Domain) == null && User.IndexOf('\\') is var sep and > 0)
        {
            Domain = User[..sep];
            User = User[(sep + 1)..].Trim();
        }
        Domain = NormalizeDomain(Domain);
        return this;
    }
    /// <summary>"DOMAINE\utilisateur", or ".\utilisateur" for a local account.</summary>
    public string Account => (IsLocal ? "." : Domain) + "\\" + User;
    public string Display => Account + (string.IsNullOrWhiteSpace(Description) ? "" : $" — {Description}");
}

/// <summary>Folder in the package tree; can contain packages and other folders.</summary>
public class PackageFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>null = at the root.</summary>
    public long? ParentId { get; set; }

    /// <summary>
    /// True if <paramref name="folderId"/> is <paramref name="ancestorId"/> itself or one of its subfolders:
    /// moving <paramref name="ancestorId"/> into it would create a loop.
    /// </summary>
    public static bool IsWithin(IReadOnlyCollection<PackageFolder> folders, long? folderId, long ancestorId)
    {
        var parents = folders.ToDictionary(f => f.Id, f => f.ParentId);
        for (var guard = 0; folderId is { } id && guard <= folders.Count; guard++)
        {
            if (id == ancestorId) return true;
            folderId = parents.GetValueOrDefault(id);
        }
        return false;
    }

    /// <summary>Error to show if the folder cannot be saved as is (name, parent, loop).</summary>
    public static string? Check(PackageFolder folder, IReadOnlyCollection<PackageFolder> all)
    {
        if (string.IsNullOrWhiteSpace(folder.Name)) return Msg.Get("PackageFolder_NameRequired");
        if (folder.ParentId is { } parent && all.All(f => f.Id != parent)) return Msg.Get("PackageFolder_ParentNotFound");
        if (folder.Id != 0 && IsWithin(all, folder.ParentId, folder.Id))
            return Msg.Get("PackageFolder_MoveIntoItself");
        return null;
    }

    /// <summary>Name of a copy pasted into a folder: the same if free, otherwise "Nom (copie)", "Nom (copie 2)"...</summary>
    public static string CopyName(string name, IEnumerable<string> namesInFolder)
    {
        var taken = namesInFolder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? Msg.Format("PackageFolder_CopyName", name) : Msg.Format("PackageFolder_CopyNameN", name, n);
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Error to show if the folder is not empty (only an empty folder can be deleted).</summary>
    public static string? CheckDelete(long id, IEnumerable<PackageFolder> all, IEnumerable<Package> packages) =>
        all.Any(f => f.ParentId == id) || packages.Any(p => p.FolderId == id)
            ? Msg.Get("PackageFolder_NotEmpty")
            : null;
}

/// <summary>
/// Schedule: packages (in order) deployed to computers at each trigger.
/// With <see cref="StopOnSuccess"/>, a computer that succeeded on a package no longer receives it:
/// an interval trigger thus retries the computers until all have succeeded.
/// </summary>
public class Schedule
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<ScheduleTrigger> Triggers { get; set; } = [];
    public List<long> PackageIds { get; set; } = [];
    public List<string> Targets { get; set; } = [];
    /// <summary>null = each package's setting.</summary>
    public RunAsMode? RunAs { get; set; }
    public CopyMode? CopyMode { get; set; }
    /// <summary>Credential to use (null = the default one at trigger time).</summary>
    public long? CredentialId { get; set; }
    /// <summary>No longer deploy a package to a computer that already succeeded with this schedule.</summary>
    public bool StopOnSuccess { get; set; } = true;
    /// <summary>No longer deploy a package to a computer after this number of failures (null = no limit).</summary>
    public int? StopAfterFailures { get; set; }
    /// <summary>Abandon computers still waiting for a slot after this delay (null = never).</summary>
    public int? QueueTimeoutMinutes { get; set; }

    // Filled in by the service.
    public DateTime? LastRunUtc { get; set; }
    public string? LastResult { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>Adds the package at the end of the list; false if it was already there.</summary>
    public bool Attach(long packageId)
    {
        if (PackageIds.Contains(packageId)) return false;
        PackageIds.Add(packageId);
        return true;
    }

    /// <summary>Removes the package; false if it was not there or was the only one (a schedule keeps at least one package).</summary>
    public bool Detach(long packageId) => PackageIds.Count > 1 && PackageIds.Remove(packageId);

    /// <summary>Schedule that redoes this deployment (same package, same computers, same options), without a trigger.</summary>
    public static Schedule From(Deployment deployment) => new()
    {
        Name = deployment.PackageName,
        PackageIds = [deployment.PackageId],
        Targets = [.. deployment.Targets.Select(t => t.Computer).Distinct(StringComparer.OrdinalIgnoreCase)],
        RunAs = deployment.RunAsMode,
        CopyMode = deployment.CopyMode,
        CredentialId = deployment.CredentialId,
    };
}

public enum TriggerType { Once, Interval, Daily, Weekly, Monthly }

/// <summary>
/// Trigger of a schedule. Dates and times are those of the server clock
/// (local time, no time zone): "every day at 8 am" stays 8 am after a clock change.
/// </summary>
public class ScheduleTrigger
{
    public TriggerType Type { get; set; } = TriggerType.Interval;
    public bool Enabled { get; set; } = true;
    /// <summary>First possible trigger (date and time). For "once": the trigger moment.</summary>
    public DateTime Start { get; set; }
    /// <summary>No trigger after this moment (null = no end). Ignored for "once".</summary>
    public DateTime? End { get; set; }
    /// <summary>Interval: time between two triggers.</summary>
    public int IntervalMinutes { get; set; } = 60;
    /// <summary>Interval: trigger only between <see cref="WindowStart"/> and <see cref="WindowEnd"/>.</summary>
    public bool UseTimeWindow { get; set; }
    public TimeSpan WindowStart { get; set; } = new(8, 0, 0);
    public TimeSpan WindowEnd { get; set; } = new(18, 0, 0);
    /// <summary>Weekly: days of the week.</summary>
    public List<DayOfWeek> Days { get; set; } = [];
    /// <summary>Monthly: day of the month (1 to 31, 0 = last day). Used if <see cref="MonthWeek"/> is null.</summary>
    public int MonthDay { get; set; } = 1;
    /// <summary>Monthly: "the Nth [day] of the month" (1 to 4, 5 = last).</summary>
    public int? MonthWeek { get; set; }
    public DayOfWeek MonthWeekDay { get; set; } = DayOfWeek.Monday;
    public string? Description { get; set; }
}

/// <summary>Accumulated result of a package on a computer, for a schedule.</summary>
public class ScheduleTargetHistory
{
    public string Computer { get; set; } = "";
    public long PackageId { get; set; }
    public string PackageName { get; set; } = "";
    public bool Succeeded { get; set; }
    public int Failures { get; set; }
    public DateTime? LastUtc { get; set; }
}

/// <summary>History row to erase (the computer will receive the package again).</summary>
public record ScheduleHistoryKey(string Computer, long PackageId);

public static class Defaults
{
    /// <summary>OK, OK + restart launched, OK + restart required, MSU update already installed.</summary>
    public const string SuccessCodes = "0,1641,3010,2359302";
}

/// <summary>Result of checking a computer name in DNS (Déployer une fois window).</summary>
public record NameCheckResult(string Name, bool Found, string? DnsName, string? Error);
