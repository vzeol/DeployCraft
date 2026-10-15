namespace DeployCraft.Server.Data;

/// <summary>Nested : paquet imbriqué, remplacé par ses étapes au lancement (jamais envoyé tel quel au poste).</summary>
public enum StepType { Install, Command, PowerShell, Pause, Nested }

/// <summary>En cas d'erreur d'une étape : arrêter en échec, continuer, ou arrêter le paquet en comptant le poste réussi.</summary>
public enum ErrorMode { Stop, Continue, StopSuccess }

/// <summary>
/// Section d'une étape. Ordre d'exécution : Avant, Principales, Après. « Après » s'exécute même si une étape
/// précédente a échoué ou arrêté le paquet (comme un nettoyage), sauf annulation. Principales = valeur 0 :
/// les étapes enregistrées avant l'arrivée des sections en font partie.
/// </summary>
public enum StepSection { Main, Before, After }

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
    /// <summary>Désactivée : gardée dans le paquet mais jamais exécutée (« Ignoré » dans le suivi).</summary>
    public bool Enabled { get; set; } = true;
    public StepSection Section { get; set; } = StepSection.Main;
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
    /// <summary>Pause : durée d'attente sur le poste, en secondes.</summary>
    public int PauseSeconds { get; set; } = 30;
    /// <summary>Paquet imbriqué : paquet dont les étapes prennent la place de celle-ci au lancement.</summary>
    public long? NestedPackageId { get; set; }
    /// <summary>
    /// Conditions à remplir en plus de celles de l'étape : posées au lancement sur les étapes d'un paquet imbriqué
    /// (conditions de l'étape « paquet imbriqué » et du paquet imbriqué lui-même).
    /// </summary>
    public List<ConditionGroup>? ExtraConditions { get; set; }
    /// <summary>Délai max de l'étape (null = celui du paquet, sinon celui des Préférences).</summary>
    public int? TimeoutMinutes { get; set; }
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Stop;
    /// <summary>Conditions de l'étape : non remplies sur un poste, l'étape y est sautée.</summary>
    public Conditions Conditions { get; set; } = new();
}

/// <summary>
/// Variable personnalisée (Préférences › Variables), écrite $(Nom) dans les chemins, paramètres, commandes et
/// conditions des étapes. Stockée en clair : jamais de mot de passe.
/// </summary>
public class Variable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>Conditions héritées, avec leur origine pour expliquer une étape ignorée.</summary>
public class ConditionGroup
{
    /// <summary>Par exemple « Conditions du paquet imbriqué « Outils » » ; suivi de « non remplies » dans le suivi.</summary>
    public string Label { get; set; } = "";
    public Conditions Conditions { get; set; } = new();
}

/// <summary>Versions de Windows acceptées par une condition (aucune cochée = toutes).</summary>
[Flags]
public enum WindowsVersions
{
    None = 0,
    Windows10 = 1,
    Windows11 = 2,
    Server = 4,
    /// <summary>Windows 8.1 et versions antérieures.</summary>
    Older = 8,
}

public enum ArchitectureCondition { Any, X64, X86 }

public enum LoggedOnCondition { Any, LoggedOn, NotLoggedOn }

/// <summary>Condition sur un fichier ou une clé de registre.</summary>
public enum PresenceCondition { None, Exists, NotExists }

/// <summary>
/// Conditions d'un paquet ou d'une étape, vérifiées sur le poste juste avant l'exécution.
/// Toutes doivent être remplies ; sinon l'étape (ou tout le paquet) est sautée, sans échec.
/// </summary>
public class Conditions
{
    public WindowsVersions Windows { get; set; }
    public ArchitectureCondition Architecture { get; set; }
    /// <summary>Version minimale de PowerShell (3, 4, 5 ou 7 ; null = aucune).</summary>
    public int? MinPowerShell { get; set; }
    public LoggedOnCondition LoggedOn { get; set; }
    public PresenceCondition File { get; set; }
    /// <summary>Fichier ou dossier sur le poste (variables d'environnement acceptées).</summary>
    public string? FilePath { get; set; }
    public PresenceCondition Registry { get; set; }
    /// <summary>Clé de registre, par exemple HKLM\SOFTWARE\Éditeur\Logiciel.</summary>
    public string? RegistryKey { get; set; }
    /// <summary>Nom d'une valeur de la clé (vide = la clé elle-même).</summary>
    public string? RegistryValue { get; set; }

    public bool IsEmpty => Windows == WindowsVersions.None && Architecture == ArchitectureCondition.Any && MinPowerShell == null
                           && LoggedOn == LoggedOnCondition.Any && File == PresenceCondition.None && Registry == PresenceCondition.None;
}

/// <summary>
/// Postes hors ligne : réveil (Wake-on-LAN) et nouveaux essais. Un poste est toujours testé
/// (nom DNS et partage de fichiers) avant la copie ; ces réglages disent quoi faire s'il ne répond pas.
/// </summary>
public class OfflineSettings
{
    /// <summary>Envoyer un paquet de réveil Wake-on-LAN, puis attendre que le poste réponde.</summary>
    public bool WakeOnLan { get; set; }
    public int WakeWaitMinutes { get; set; } = 3;
    /// <summary>Réessayer plus tard un poste hors ligne au lieu de le marquer en échec.</summary>
    public bool Retry { get; set; }
    /// <summary>Nombre total d'essais, le premier compris.</summary>
    public int RetryAttempts { get; set; } = 5;
    public int RetryIntervalMinutes { get; set; } = 30;
}

/// <summary>Carte réseau d'un poste : adresse MAC et adresse de diffusion de son sous-réseau (pour le réveiller).</summary>
public record NetworkCard(string Mac, string? Broadcast);

/// <summary>Réglages par défaut des paquets (Préférences › Déploiement et Hors ligne).</summary>
public class DeploymentDefaults
{
    public RunAsMode RunAs { get; set; } = RunAsMode.DeployUser;
    public CopyMode CopyMode { get; set; } = CopyMode.Push;
    public int TimeoutMinutes { get; set; } = 60;
    public OfflineSettings Offline { get; set; } = new();
}

public class Package
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<PackageStep> Steps { get; set; } = [];
    /// <summary>null = valeur des Préférences.</summary>
    public RunAsMode? RunAs { get; set; }
    /// <summary>null = valeur des Préférences.</summary>
    public CopyMode? CopyMode { get; set; }
    /// <summary>Délai max des étapes qui n'en fixent pas (null = valeur des Préférences).</summary>
    public int? TimeoutMinutes { get; set; }
    /// <summary>Conditions du paquet : non remplies sur un poste, aucune étape n'y est exécutée.</summary>
    public Conditions Conditions { get; set; } = new();
    /// <summary>null = réglages hors ligne des Préférences.</summary>
    public OfflineSettings? Offline { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// Copie prête à déployer : chaque réglage laissé « par défaut » prend sa valeur (étape → paquet → Préférences).
    /// L'historique du déploiement garde ainsi les valeurs réellement utilisées.
    /// </summary>
    public Package Resolve(DeploymentDefaults defaults, Func<long, Package?>? lookup = null,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var copy = Flatten(lookup ?? (_ => null), defaults.TimeoutMinutes);
        if (variables != null) copy.ReplaceVariables(variables);
        copy.RunAs ??= defaults.RunAs;
        copy.CopyMode ??= defaults.CopyMode;
        copy.TimeoutMinutes ??= defaults.TimeoutMinutes;
        copy.Offline ??= Clone(defaults.Offline);
        foreach (var step in copy.Steps) step.TimeoutMinutes ??= copy.TimeoutMinutes;
        return copy;
    }

    /// <summary>
    /// Remplace les variables $(Nom) connues de <paramref name="values"/> dans les chemins, paramètres et commandes des
    /// étapes et dans les chemins et clés des conditions (du paquet, des étapes et héritées).
    /// </summary>
    public void ReplaceVariables(IReadOnlyDictionary<string, string> values)
    {
        VariableText.Replace(Conditions, values);
        foreach (var step in Steps) VariableText.Replace(step, values);
    }

    /// <summary>Niveaux de paquets imbriqués acceptés (un paquet qui en contient un qui en contient un…).</summary>
    public const int MaxNestingDepth = 5;

    /// <summary>
    /// Copie où chaque étape « paquet imbriqué » est remplacée par les étapes de ce paquet : nommées « Étape › Sous-étape »,
    /// dans la section de l'étape remplacée, désactivées si elle l'est, avec ses conditions et celles du paquet imbriqué
    /// en conditions supplémentaires. Délai : celui de la sous-étape, sinon celui du paquet imbriqué, sinon
    /// <paramref name="defaultTimeout"/>. Paquet introuvable, boucle ou trop de niveaux : <see cref="ArgumentException"/>.
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

            foreach (var s in inner.OrderBy(s => SectionRank(s.Section)))
            {
                s.Name = $"{label} › {(string.IsNullOrWhiteSpace(s.Name) ? s.Type.ToString() : s.Name)}";
                s.Section = step.Section;
                s.Enabled = step.Enabled && s.Enabled;
                s.TimeoutMinutes ??= nested.TimeoutMinutes ?? defaultTimeout;
                var extras = new List<ConditionGroup>();
                if (!step.Conditions.IsEmpty) extras.Add(new() { Label = $"Conditions de l'étape « {label} »", Conditions = step.Conditions });
                if (!nested.Conditions.IsEmpty) extras.Add(new() { Label = $"Conditions du paquet imbriqué « {nested.Name} »", Conditions = nested.Conditions });
                extras.AddRange(s.ExtraConditions ?? []);
                s.ExtraConditions = extras.Count > 0 ? extras : null;
                result.Add(s);
            }
        }
        return result;
    }

    private static Package NestedPackage(PackageStep step, Func<long, Package?> lookup, List<long> path, int depth)
    {
        var title = string.IsNullOrWhiteSpace(step.Name) ? "paquet imbriqué" : $"« {step.Name} »";
        if (step.NestedPackageId is not { } id) throw new ArgumentException($"Étape {title} : choisissez le paquet à imbriquer.");
        var nested = lookup(id) ?? throw new ArgumentException($"Étape {title} : paquet imbriqué introuvable (n° {id}, supprimé ?).");
        if (path.Contains(id))
        {
            var loop = path.Skip(path.IndexOf(id)).Append(id).Select(i => lookup(i)?.Name ?? $"n° {i}");
            throw new ArgumentException($"Étape {title} : boucle de paquets imbriqués ({string.Join(" › ", loop)}).");
        }
        if (depth >= MaxNestingDepth) throw new ArgumentException($"Étape {title} : trop de niveaux de paquets imbriqués ({MaxNestingDepth} au plus).");
        return nested;
    }

    /// <summary>Paquets qui utilisent <paramref name="id"/> comme paquet imbriqué (il ne peut pas être supprimé).</summary>
    public static List<Package> UsersOf(IEnumerable<Package> packages, long id) =>
        [.. packages.Where(p => p.Id != id && p.Steps.Any(s => s.Type == StepType.Nested && s.NestedPackageId == id))];

    /// <summary>Range les étapes par section (Avant, Principales, Après) en gardant leur ordre dans chaque section.</summary>
    public void SortSteps() => Steps = [.. Steps.OrderBy(s => SectionRank(s.Section))];

    public static int SectionRank(StepSection section) => section switch
    {
        StepSection.Before => 0,
        StepSection.Main => 1,
        _ => 2,
    };

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
