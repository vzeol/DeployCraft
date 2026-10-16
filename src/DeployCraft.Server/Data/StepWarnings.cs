namespace DeployCraft.Server.Data;

/// <summary>
/// Avertissements d'une étape, affichés dans la fenêtre du paquet et rappelés à l'enregistrement : rien de bloquant,
/// mais un déploiement risque d'échouer ou de ne pas se passer comme prévu.
/// </summary>
public static class StepWarnings
{
    /// <summary>Fichiers et dossiers que le serveur (ou le poste, en pull) devra lire pour cette étape.</summary>
    public static List<string> Paths(PackageStep step)
    {
        var paths = new List<string>();
        if (!step.Enabled) return paths;
        if (step.Type == StepType.Install && !string.IsNullOrWhiteSpace(step.File)) paths.Add(step.File.Trim());
        if (step.Type == StepType.CopyFiles && !string.IsNullOrWhiteSpace(step.CopySource)) paths.Add(step.CopySource.Trim());
        if (step.Type is StepType.Install or StepType.Command or StepType.PowerShell)
            paths.AddRange(PackageStep.SplitLines(step.AdditionalFiles));
        return paths;
    }

    /// <param name="copyMode">Mode de copie effectif du paquet.</param>
    /// <param name="missing">Chemins introuvables depuis le serveur (vérifiés par le service).</param>
    public static List<string> For(PackageStep step, CopyMode copyMode, IReadOnlyCollection<string> missing)
    {
        var warnings = new List<string>();
        if (!step.Enabled) return warnings;

        if (step.Type == StepType.Install && string.IsNullOrWhiteSpace(step.CustomCommandLine)
            && System.IO.Path.GetExtension(step.File ?? "").Equals(".exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(step.Arguments))
            warnings.Add("Aucun paramètre silencieux : l'installation risque d'attendre un clic jusqu'au délai max.");

        foreach (var path in Paths(step))
        {
            if (missing.Contains(path, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"Introuvable depuis le serveur : {path}");
            else if (copyMode == CopyMode.Pull && !path.StartsWith(@"\\", StringComparison.Ordinal))
                warnings.Add($"Mode pull, mais « {path} » n'est pas sur un partage : le serveur l'enverra lui-même (comme en push).");
        }
        return warnings;
    }
}
