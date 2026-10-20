using DeployCraft.Server.Data;

namespace DeployCraft.Server.Validation;

/// <summary>Checks a package (and its options) before it is saved.</summary>
internal static class PackageValidation
{
    internal static string? Validate(Package p, Func<long, Package?> lookup)
    {
        // Un client qui envoie null obtient « aucune condition ».
        p.Conditions ??= new();
        foreach (var step in p.Steps)
        {
            step.Conditions ??= new();
            step.ExtraConditions = null; // posées seulement au lancement, à partir des paquets imbriqués
        }
        if (string.IsNullOrWhiteSpace(p.Name)) return "Le paquet doit avoir un nom.";
        if (p.TimeoutMinutes is < 1) return "Options : le délai max doit être d'au moins 1 minute.";
        if (ValidateConditions(p.Conditions) is { } conditionError) return $"Conditions du paquet : {conditionError}";
        // Paquets imbriqués : choisis, existants, sans boucle ni trop de niveaux (le paquet enregistré compte pour lui-même).
        try
        {
            p.Flatten(id => id == p.Id ? p : lookup(id));
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
        if (p.Offline != null && ValidateOffline(p.Offline) is { } offlineError) return $"Hors ligne : {offlineError}";
        for (var i = 0; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            var label = $"Étape {i + 1}";
            switch (s.Type)
            {
                case StepType.Install when string.IsNullOrWhiteSpace(s.File):
                    return $"{label} : choisissez le fichier d'installation.";
                case StepType.Command or StepType.PowerShell when string.IsNullOrWhiteSpace(s.Command):
                    return $"{label} : la commande est vide.";
                case StepType.Pause when s.PauseSeconds is < 1 or > 86400:
                    return $"{label} : la pause doit durer de 1 seconde à 24 heures (86 400 secondes).";
                case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopySource):
                    return $"{label} : choisissez le fichier ou le dossier à copier.";
                case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopyDestination):
                    return $"{label} : indiquez le dossier de destination sur le poste.";
                case StepType.Reboot when s.RebootDelaySeconds is < 30 or > 3600:
                    return $"{label} : le délai avant le redémarrage doit être de 30 secondes à 1 heure (3 600 secondes).";
                case StepType.Reboot when s.RebootMessage?.Length > 500:
                    return $"{label} : le message ne doit pas dépasser 500 caractères.";
                case StepType.Message when string.IsNullOrWhiteSpace(s.MessageText):
                    return $"{label} : le message est vide.";
                case StepType.Message when s.MessageText!.Length > 2000 || s.MessageTitle?.Length > 100:
                    return $"{label} : 2 000 caractères au plus pour le message, 100 pour le titre.";
                case StepType.Message when s.MessageSeconds is < 0 or > 86400:
                    return $"{label} : la durée d'affichage doit aller de 0 (jusqu'au clic) à 24 heures (86 400 secondes).";
            }
            if (s.TimeoutMinutes is < 1) return $"{label} : le délai max doit être d'au moins 1 minute.";
            if (ValidateConditions(s.Conditions) is { } error) return $"{label}, conditions : {error}";
        }
        return null;
    }

    internal static string? ValidateConditions(Conditions c)
    {
        if (c.File != PresenceCondition.None && string.IsNullOrWhiteSpace(c.FilePath)) return "indiquez le chemin du fichier.";
        if (c.Registry != PresenceCondition.None)
        {
            if (string.IsNullOrWhiteSpace(c.RegistryKey)) return "indiquez la clé de registre.";
            var hive = c.RegistryKey.Trim().Split('\\')[0].ToUpperInvariant();
            if (hive is not ("HKLM" or "HKEY_LOCAL_MACHINE" or "HKCU" or "HKEY_CURRENT_USER" or "HKCR" or "HKEY_CLASSES_ROOT" or "HKU" or "HKEY_USERS"))
                return "la clé de registre doit commencer par HKLM, HKCU, HKCR ou HKU.";
        }
        return null;
    }

    internal static string? ValidateOffline(OfflineSettings o)
    {
        if (o.WakeOnLan && o.WakeWaitMinutes < 1) return "l'attente après le réveil doit être d'au moins 1 minute.";
        if (o.Retry && o.RetryAttempts < 2) return "il faut au moins 2 essais pour réessayer.";
        if (o.Retry && o.RetryIntervalMinutes < 1) return "l'intervalle entre deux essais doit être d'au moins 1 minute.";
        return null;
    }
}
