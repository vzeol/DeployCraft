namespace DeployCraft.Server.Data;

/// <summary>Displayed statuses and counters of a deployment, from the state of its computers.</summary>
public static class DeploymentSummary
{
    /// <summary>Start of the error of a cancelled computer ("Annulé." or "Annulé par ...").</summary>
    public const string AbortedPrefix = "Annulé";

    public static bool IsAborted(DeploymentTarget target) =>
        target.Status == TargetStatus.Failed && target.Error?.StartsWith(AbortedPrefix, StringComparison.Ordinal) == true;

    /// <summary>Status of a computer: the service's, or "Aborted" for a cancelled computer.</summary>
    public static string TargetStatusOf(DeploymentTarget target) => IsAborted(target) ? "Aborted" : target.Status.ToString();

    /// <summary>
    /// Status of a deployment: finished as failed, it becomes "Partial" if some computers succeeded, "Aborted" if all
    /// its failures are cancellations.
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

    /// <summary>File name of a step's output, without forbidden characters (e.g. "PC-TEST-01-deploiement12-etape3.txt").</summary>
    public static string OutputFileName(string computer, long deploymentId, int step)
    {
        var name = $"{computer}-deploiement{deploymentId}-etape{step}.txt";
        return string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    public record Counts(int Total, int Queued, int Running, int Successful, int Failed, int Aborted);
}
