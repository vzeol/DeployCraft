namespace DeployCraft.Server.Data;

/// <summary>Textes de l'onglet Détails de la page d'un paquet : emplacement, conditions, hors ligne, paquets qui l'utilisent.</summary>
public static class PackageDetails
{
    /// <summary>Chemin du dossier dans l'arbre (« Paquets › Bureautique › PDF »), « Paquets » à la racine.</summary>
    public static string FolderPath(long? folderId, IEnumerable<PackageFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        var names = new List<string>();
        // Limite de profondeur : une base modifiée à la main ne doit pas faire tourner en rond.
        while (folderId is { } id && byId.TryGetValue(id, out var folder) && names.Count < 50)
        {
            names.Insert(0, folder.Name);
            folderId = folder.ParentId;
        }
        return string.Join(" › ", names.Prepend("Paquets"));
    }

    /// <summary>Conditions en une ligne lisible, ou « Aucune ».</summary>
    public static string ConditionsText(Conditions c)
    {
        if (c.IsEmpty) return "Aucune : le paquet s'exécute sur tous les postes.";
        var parts = new List<string>();
        if (c.Windows != WindowsVersions.None)
        {
            var versions = new List<string>();
            if (c.Windows.HasFlag(WindowsVersions.Windows11)) versions.Add("Windows 11");
            if (c.Windows.HasFlag(WindowsVersions.Windows10)) versions.Add("Windows 10");
            if (c.Windows.HasFlag(WindowsVersions.Server)) versions.Add("Windows Server");
            if (c.Windows.HasFlag(WindowsVersions.Older)) versions.Add("Windows 8.1 ou plus ancien");
            parts.Add(string.Join(" ou ", versions));
        }
        if (c.Architecture != ArchitectureCondition.Any) parts.Add(c.Architecture == ArchitectureCondition.X64 ? "64 bits" : "32 bits");
        if (c.MinPowerShell is { } ps) parts.Add($"PowerShell {ps} ou plus");
        if (c.LoggedOn != LoggedOnCondition.Any) parts.Add(c.LoggedOn == LoggedOnCondition.LoggedOn ? "un utilisateur connecté" : "personne de connecté");
        if (c.File != PresenceCondition.None) parts.Add($"fichier {c.FilePath} {Presence(c.File)}");
        if (c.Registry != PresenceCondition.None)
        {
            var key = string.IsNullOrWhiteSpace(c.RegistryValue) ? c.RegistryKey : $@"{c.RegistryKey}\{c.RegistryValue}";
            parts.Add($"registre {key} {Presence(c.Registry)}");
        }
        return string.Join(" ; ", parts);
    }

    private static string Presence(PresenceCondition p) => p == PresenceCondition.Exists ? "présent" : "absent";

    /// <summary>Réglages hors ligne en une ligne ; <paramref name="fromDefaults"/> : ceux des Préférences.</summary>
    public static string OfflineText(OfflineSettings o, bool fromDefaults)
    {
        var parts = new List<string>
        {
            o.WakeOnLan ? $"réveil du poste (attente {o.WakeWaitMinutes} min)" : "pas de réveil",
            o.Retry ? $"{o.RetryAttempts} essais, toutes les {o.RetryIntervalMinutes} min" : "pas de nouvel essai",
        };
        var text = string.Join(", ", parts);
        return fromDefaults ? $"Préférences ({text})" : char.ToUpper(text[0]) + text[1..];
    }

    /// <summary>Paquets qui contiennent celui-ci comme paquet imbriqué, par nom.</summary>
    public static string UsedByText(long id, IEnumerable<Package> packages)
    {
        var users = Package.UsersOf(packages, id).Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        return users.Count == 0 ? "Aucun paquet" : string.Join(", ", users);
    }
}
