namespace DeployCraft.Server.Data;

/// <summary>Un logiciel proposé par la bibliothèque de paquets, et ce que DeployCraft en a fait.</summary>
public class LibraryItem
{
    /// <summary>Identifiant du logiciel dans le catalogue public winget (ex. VideoLAN.VLC).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Dernière version connue du catalogue (null tant qu'il n'a pas été lu).</summary>
    public string? LatestVersion { get; set; }
    /// <summary>Type d'installeur retenu (msi, nullsoft, inno…) et architecture.</summary>
    public string? Installer { get; set; }
    /// <summary>Raison pour laquelle ce logiciel ne peut pas être téléchargé (aucun installeur silencieux pour tout le poste…).</summary>
    public string? Problem { get; set; }
    /// <summary>Paquet DeployCraft créé à partir de ce logiciel (null s'il n'a jamais été téléchargé ou a été supprimé).</summary>
    public long? PackageId { get; set; }
    /// <summary>Version téléchargée dans ce paquet.</summary>
    public string? PackageVersion { get; set; }
    /// <summary>Étape en cours (téléchargement, vérification, copie, création du paquet), null s'il n'y en a pas.</summary>
    public string? Stage { get; set; }
    /// <summary>Avancement de l'étape en cours, de 0 à 100.</summary>
    public int? Progress { get; set; }
    /// <summary>Erreur du dernier téléchargement de ce logiciel.</summary>
    public string? Error { get; set; }
    public bool UpdateAvailable { get; set; }
}

/// <summary>Mise à jour des paquets créés depuis la bibliothèque quand le catalogue publie une nouvelle version.</summary>
public enum LibraryAutoUpdate
{
    /// <summary>Signalée seulement : l'utilisateur clique sur « Mettre à jour ».</summary>
    Manual,
    /// <summary>Faite par le service dès que la nouvelle version est vue.</summary>
    Immediate,
    /// <summary>Faite par le service quelques jours après que la version est apparue dans le catalogue.</summary>
    Delayed,
}

/// <summary>État de la bibliothèque renvoyé à la console.</summary>
public class LibraryInfo
{
    /// <summary>Dossier où les installeurs sont rangés.</summary>
    public string Folder { get; set; } = "";
    /// <summary>Le dossier est celui proposé par défaut (sur le serveur, lisible en mode push seulement).</summary>
    public bool DefaultFolder { get; set; }
    public DateTime? LastRefreshUtc { get; set; }
    public bool Refreshing { get; set; }
    /// <summary>Erreur de la dernière lecture du catalogue.</summary>
    public string? RefreshError { get; set; }
    public LibraryAutoUpdate AutoUpdate { get; set; }
    /// <summary>Délai avant une mise à jour automatique (mode <see cref="LibraryAutoUpdate.Delayed"/>).</summary>
    public int AutoUpdateDays { get; set; }
    /// <summary>Nombre de logiciels dans le catalogue.</summary>
    public int CatalogCount { get; set; }
    public List<LibraryItem> Items { get; set; } = [];
}
