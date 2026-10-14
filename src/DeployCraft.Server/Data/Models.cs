namespace DeployCraft.Server.Data;

public enum StepType { Install, Command, PowerShell }

public enum ErrorMode { Stop, Continue }

/// <summary>Identité sous laquelle les étapes s'exécutent sur le poste.</summary>
public enum RunAsMode
{
    /// <summary>Le service temporaire tourne sous le compte de déploiement.</summary>
    DeployUser,
    /// <summary>Le service temporaire tourne en Système local.</summary>
    LocalSystem,
    /// <summary>Les étapes sont lancées dans la session de l'utilisateur connecté.</summary>
    LoggedOnUser,
}

/// <summary>Push : le serveur copie les fichiers sur le poste. Pull : le poste les lit lui-même sur le partage.</summary>
public enum CopyMode { Push, Pull }

public class PackageStep
{
    public string Name { get; set; } = "";
    public StepType Type { get; set; } = StepType.Install;
    /// <summary>Install : chemin du fichier (local ou UNC) côté serveur.</summary>
    public string? File { get; set; }
    /// <summary>Install : copier tout le dossier du fichier (installeurs en plusieurs fichiers).</summary>
    public bool IncludeDirectory { get; set; }
    /// <summary>Install : paramètres (silencieux). PowerShell : arguments du script.</summary>
    public string? Arguments { get; set; }
    /// <summary>Command : ligne cmd. PowerShell : contenu du script.</summary>
    public string? Command { get; set; }
    public string SuccessCodes { get; set; } = Defaults.SuccessCodes;
    public int TimeoutMinutes { get; set; } = 60;
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Stop;
}

public class Package
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<PackageStep> Steps { get; set; } = [];
    public RunAsMode RunAs { get; set; } = RunAsMode.DeployUser;
    public CopyMode CopyMode { get; set; } = CopyMode.Push;
    public DateTime ModifiedUtc { get; set; }
}

public enum DeploymentStatus { Queued, Running, Successful, Failed }

public enum TargetStatus { Queued, Running, Successful, Failed }

public class Deployment
{
    public long Id { get; set; }
    public long PackageId { get; set; }
    public string PackageName { get; set; } = "";
    /// <summary>Copie du paquet au moment du lancement : modifier le paquet ensuite n'affecte pas l'historique.</summary>
    public Package Snapshot { get; set; } = new();
    public DeploymentStatus Status { get; set; }
    /// <summary>Utilisateur Windows de la console qui a lancé le déploiement.</summary>
    public string? LaunchedBy { get; set; }
    /// <summary>Compte de déploiement utilisé (au moment du lancement).</summary>
    public string? RunAs { get; set; }
    /// <summary>Identifiant utilisé (null = aucun : identité du service).</summary>
    public long? CredentialId { get; set; }
    /// <summary>Plus petite étape de départ parmi les postes (affichage) ; chaque poste a la sienne.</summary>
    public int StartStep { get; set; } = 1;
    public RunAsMode RunAsMode { get; set; } = RunAsMode.DeployUser;
    public CopyMode CopyMode { get; set; } = CopyMode.Push;
    public DateTime CreatedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Planification qui a lancé ce déploiement (null = lancé à la main).</summary>
    public long? ScheduleId { get; set; }
    public string? ScheduleName { get; set; }
    /// <summary>Les postes encore en attente d'un créneau après ce délai sont abandonnés (null = jamais).</summary>
    public int? QueueTimeoutMinutes { get; set; }
    public List<DeploymentTarget> Targets { get; set; } = [];
}

public class DeploymentTarget
{
    public long Id { get; set; }
    public long DeploymentId { get; set; }
    public string Computer { get; set; } = "";
    /// <summary>Première étape exécutée sur ce poste (1 = tout le paquet ; plus pour reprendre à l'étape en échec).</summary>
    public int StartStep { get; set; } = 1;
    /// <summary>Passe devant les postes non prioritaires en attente d'un créneau.</summary>
    public bool Prioritized { get; set; }
    public TargetStatus Status { get; set; }
    /// <summary>Étape en cours, pour l'affichage (« Copie des fichiers », « Étape 2/3 »…).</summary>
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
}

/// <summary>Un poste cible et l'étape à laquelle commencer.</summary>
public record TargetRequest(string Computer, int StartStep = 1);

/// <summary>Options d'un « Déployer une fois » ; null = paramètre du paquet (identifiant : celui par défaut).</summary>
public record DeployOptions(RunAsMode? RunAs = null, CopyMode? CopyMode = null, bool Prioritize = false, long? CredentialId = null);

/// <summary>
/// Identifiant enregistré : un compte administrateur des postes. Le mot de passe, chiffré,
/// ne quitte jamais le service. Sans domaine (ou « . ») : le compte local de chaque poste.
/// </summary>
public class Credential
{
    public long Id { get; set; }
    public string? Domain { get; set; }
    public string User { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Utilisé quand un déploiement ou une planification n'en désigne pas.</summary>
    public bool IsDefault { get; set; }
    /// <summary>Mot de passe impossible à déchiffrer (base copiée d'un autre serveur…) : à ressaisir.</summary>
    public bool PasswordUnreadable { get; set; }

    public bool IsLocal => NormalizeDomain(Domain) == null;

    /// <summary>« », « . », « .\ » et « ./ » désignent le compte local de chaque poste (null) ; sinon le domaine, sans barre finale.</summary>
    public static string? NormalizeDomain(string? domain)
    {
        var d = domain?.Trim().TrimEnd('\\', '/').Trim();
        return string.IsNullOrEmpty(d) || d == "." ? null : d;
    }

    /// <summary>Remet la saisie en forme, y compris « DOMAINE\utilisateur » ou « .\utilisateur » tapé en entier dans Utilisateur.</summary>
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
    /// <summary>« DOMAINE\utilisateur », ou « .\utilisateur » pour un compte local.</summary>
    public string Account => (IsLocal ? "." : Domain) + "\\" + User;
    public string Display => Account + (string.IsNullOrWhiteSpace(Description) ? "" : $" — {Description}");
}

/// <summary>
/// Planification : des paquets (dans l'ordre) déployés sur des postes à chaque déclenchement.
/// Avec <see cref="StopOnSuccess"/>, un poste qui a réussi un paquet ne le reçoit plus :
/// un déclencheur à intervalle réessaie ainsi les postes jusqu'à ce que tous aient réussi.
/// </summary>
public class Schedule
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<ScheduleTrigger> Triggers { get; set; } = [];
    public List<long> PackageIds { get; set; } = [];
    public List<string> Targets { get; set; } = [];
    /// <summary>null = paramètre de chaque paquet.</summary>
    public RunAsMode? RunAs { get; set; }
    public CopyMode? CopyMode { get; set; }
    /// <summary>Identifiant à utiliser (null = celui par défaut au moment du déclenchement).</summary>
    public long? CredentialId { get; set; }
    /// <summary>Ne plus déployer un paquet sur un poste qui l'a déjà réussi avec cette planification.</summary>
    public bool StopOnSuccess { get; set; } = true;
    /// <summary>Ne plus déployer un paquet sur un poste après ce nombre d'échecs (null = sans limite).</summary>
    public int? StopAfterFailures { get; set; }
    /// <summary>Abandonner les postes qui attendent encore un créneau après ce délai (null = jamais).</summary>
    public int? QueueTimeoutMinutes { get; set; }

    // Renseignés par le service.
    public DateTime? LastRunUtc { get; set; }
    public string? LastResult { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

public enum TriggerType { Once, Interval, Daily, Weekly, Monthly }

/// <summary>
/// Déclencheur d'une planification. Les dates et heures sont celles de l'horloge du serveur
/// (heure locale, sans fuseau) : « tous les jours à 8 h » reste 8 h après un changement d'heure.
/// </summary>
public class ScheduleTrigger
{
    public TriggerType Type { get; set; } = TriggerType.Interval;
    public bool Enabled { get; set; } = true;
    /// <summary>Premier déclenchement possible (date et heure). Pour « une fois » : le moment du déclenchement.</summary>
    public DateTime Start { get; set; }
    /// <summary>Plus aucun déclenchement après ce moment (null = sans fin). Ignoré pour « une fois ».</summary>
    public DateTime? End { get; set; }
    /// <summary>Intervalle : durée entre deux déclenchements.</summary>
    public int IntervalMinutes { get; set; } = 60;
    /// <summary>Intervalle : ne se déclencher qu'entre <see cref="WindowStart"/> et <see cref="WindowEnd"/>.</summary>
    public bool UseTimeWindow { get; set; }
    public TimeSpan WindowStart { get; set; } = new(8, 0, 0);
    public TimeSpan WindowEnd { get; set; } = new(18, 0, 0);
    /// <summary>Hebdomadaire : jours de la semaine.</summary>
    public List<DayOfWeek> Days { get; set; } = [];
    /// <summary>Mensuel : jour du mois (1 à 31, 0 = dernier jour). Utilisé si <see cref="MonthWeek"/> est null.</summary>
    public int MonthDay { get; set; } = 1;
    /// <summary>Mensuel : « le Ne [jour] du mois » (1 à 4, 5 = dernier).</summary>
    public int? MonthWeek { get; set; }
    public DayOfWeek MonthWeekDay { get; set; } = DayOfWeek.Monday;
    public string? Description { get; set; }
}

/// <summary>Résultat cumulé d'un paquet sur un poste, pour une planification.</summary>
public class ScheduleTargetHistory
{
    public string Computer { get; set; } = "";
    public long PackageId { get; set; }
    public string PackageName { get; set; } = "";
    public bool Succeeded { get; set; }
    public int Failures { get; set; }
    public DateTime? LastUtc { get; set; }
}

/// <summary>Ligne d'historique à effacer (le poste recevra de nouveau le paquet).</summary>
public record ScheduleHistoryKey(string Computer, long PackageId);

public static class Defaults
{
    /// <summary>OK, OK + redémarrage lancé, OK + redémarrage requis, mise à jour MSU déjà installée.</summary>
    public const string SuccessCodes = "0,1641,3010,2359302";

    public const int MaxParallelTargets = 8;
}
