namespace DeployCraft.Server.Data;

/// <summary>Un logiciel proposé par la bibliothèque de paquets, et ce que DeployCraft en a fait.</summary>
public class LibraryItem
{
    /// <summary>Identifiant du logiciel dans le catalogue public winget (ex. VideoLAN.VLC).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
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
    /// <summary>Téléchargement en cours : pourcentage (0 à 100), null sinon.</summary>
    public int? Progress { get; set; }
    /// <summary>Erreur du dernier téléchargement de ce logiciel.</summary>
    public string? Error { get; set; }
    public bool UpdateAvailable { get; set; }
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
    public List<LibraryItem> Items { get; set; } = [];
}
