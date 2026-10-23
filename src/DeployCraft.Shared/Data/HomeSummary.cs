namespace DeployCraft.Server.Data;

/// <summary>Figures for the console's home page, computed from the deployments and the schedules.</summary>
public record HomeSummary(
    int RunningDeployments,
    int RunningTargets,
    int QueuedTargets,
    int SucceededToday,
    int FailedToday,
    List<Deployment> RecentFailures,
    List<Schedule> NextSchedules)
{
    /// <param name="nowUtc">Reference time: "the last 24 hours" is counted from it.</param>
    public static HomeSummary From(IReadOnlyCollection<Deployment> deployments, IEnumerable<Schedule> schedules, DateTime nowUtc, int max = 5)
    {
        var active = deployments.Where(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued).ToList();
        var since = nowUtc.AddHours(-24);
        var ended = deployments.Where(d => d.EndedUtc >= since).ToList();
        return new HomeSummary(
            active.Count,
            active.Sum(d => d.Targets.Count(t => t.Status == TargetStatus.Running)),
            active.Sum(d => d.Targets.Count(t => t.Status == TargetStatus.Queued)),
            ended.Count(d => d.Status == DeploymentStatus.Successful),
            ended.Count(d => d.Status == DeploymentStatus.Failed),
            [.. ended.Where(d => d.Status == DeploymentStatus.Failed).OrderByDescending(d => d.EndedUtc).Take(max)],
            [.. schedules.Where(s => s.Enabled && s.NextRunUtc != null).OrderBy(s => s.NextRunUtc).Take(max)]);
    }
}
