namespace DeployCraft.Server.Data;

/// <summary>Statuts affichés et compteurs d'un déploiement, d'après l'état de ses postes.</summary>
public static class DeploymentSummary
{
    /// <summary>Début de l'erreur d'un poste annulé (« Annulé. » ou « Annulé par … »).</summary>
    public const string AbortedPrefix = "Annulé";

    public static bool IsAborted(DeploymentTarget target) =>
        target.Status == TargetStatus.Failed && target.Error?.StartsWith(AbortedPrefix, StringComparison.Ordinal) == true;

    /// <summary>Statut d'un poste : celui du service, ou « Aborted » pour un poste annulé.</summary>
    public static string TargetStatusOf(DeploymentTarget target) => IsAborted(target) ? "Aborted" : target.Status.ToString();

    /// <summary>
    /// Statut d'un déploiement : terminé en échec, il devient « Partial » si des postes ont réussi, « Aborted » si tous
    /// ses échecs sont des annulations.
    /// </summary>
    public static string StatusOf(Deployment deployment)
    {
        if (deployment.Status != DeploymentStatus.Failed) return deployment.Status.ToString();
        if (deployment.Targets.Any(t => t.Status == TargetStatus.Successful)) return "Partial";
        var failed = deployment.Targets.Where(t => t.Status == TargetStatus.Failed).ToList();
        return failed.Count > 0 && failed.All(IsAborted) ? "Aborted" : "Failed";
    }

    public static Counts Count(Deployment deployment) => new(
        deployment.Targets.Count,
        deployment.Targets.Count(t => t.Status == TargetStatus.Queued),
        deployment.Targets.Count(t => t.Status == TargetStatus.Running),
        deployment.Targets.Count(t => t.Status == TargetStatus.Successful),
        deployment.Targets.Count(t => t.Status == TargetStatus.Failed && !IsAborted(t)),
        deployment.Targets.Count(IsAborted));

    /// <summary>Nom de fichier de la sortie d'une étape, sans caractère interdit (ex. « PC-TEST-01-deploiement12-etape3.txt »).</summary>
    public static string OutputFileName(string computer, long deploymentId, int step)
    {
        var name = $"{computer}-deploiement{deploymentId}-etape{step}.txt";
        return string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    public record Counts(int Total, int Queued, int Running, int Successful, int Failed, int Aborted);
}
