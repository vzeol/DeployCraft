using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Chiffres de la page d'accueil.</summary>
public class HomeSummaryTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Deployment Deployment(long id, DeploymentStatus status, DateTime? ended, params TargetStatus[] targets) => new()
    {
        Id = id,
        Status = status,
        EndedUtc = ended,
        Targets = [.. targets.Select(t => new DeploymentTarget { Status = t })],
    };

    [Fact]
    public void CountsRunningWork_AndTheLast24Hours()
    {
        var summary = HomeSummary.From(
        [
            Deployment(1, DeploymentStatus.Running, null, TargetStatus.Running, TargetStatus.Queued, TargetStatus.Successful),
            Deployment(2, DeploymentStatus.Queued, null, TargetStatus.Queued),
            Deployment(3, DeploymentStatus.Successful, Now.AddHours(-2)),
            Deployment(4, DeploymentStatus.Failed, Now.AddHours(-1)),
            Deployment(5, DeploymentStatus.Failed, Now.AddHours(-3)),
            Deployment(6, DeploymentStatus.Failed, Now.AddDays(-2)),
        ], [], Now);

        Assert.Equal(2, summary.RunningDeployments);
        Assert.Equal(1, summary.RunningTargets);
        Assert.Equal(2, summary.QueuedTargets);
        Assert.Equal(1, summary.SucceededToday);
        Assert.Equal(2, summary.FailedToday);
        Assert.Equal([4L, 5L], summary.RecentFailures.Select(d => d.Id));
    }

    [Fact]
    public void NextSchedules_AreEnabledOnes_SoonestFirst()
    {
        var summary = HomeSummary.From([],
        [
            new Schedule { Id = 1, NextRunUtc = Now.AddHours(5) },
            new Schedule { Id = 2, NextRunUtc = Now.AddHours(1) },
            new Schedule { Id = 3, NextRunUtc = Now.AddMinutes(5), Enabled = false },
            new Schedule { Id = 4 },
        ], Now);

        Assert.Equal([2L, 1L], summary.NextSchedules.Select(s => s.Id));
    }
}
