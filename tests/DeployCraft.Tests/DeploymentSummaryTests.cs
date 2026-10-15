using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Statut affiché d'un déploiement et de ses postes, et compteurs.</summary>
public class DeploymentSummaryTests
{
    private static DeploymentTarget Target(TargetStatus status, string? error = null) => new() { Status = status, Error = error };

    private static Deployment Deployment(DeploymentStatus status, params DeploymentTarget[] targets) =>
        new() { Status = status, Targets = [.. targets] };

    [Fact]
    public void FailedDeployment_IsPartial_WhenSomeTargetsSucceeded()
    {
        var d = Deployment(DeploymentStatus.Failed, Target(TargetStatus.Successful), Target(TargetStatus.Failed, "Code 1603"));
        Assert.Equal("Partial", DeploymentSummary.StatusOf(d));
    }

    [Fact]
    public void FailedDeployment_IsAborted_WhenAllFailuresAreCancellations()
    {
        var d = Deployment(DeploymentStatus.Failed, Target(TargetStatus.Failed, "Annulé."), Target(TargetStatus.Failed, "Annulé par DOMAINE\admin."));
        Assert.Equal("Aborted", DeploymentSummary.StatusOf(d));
        Assert.Equal("Aborted", DeploymentSummary.TargetStatusOf(d.Targets[0]));
    }

    [Fact]
    public void FailedDeployment_StaysFailed_WithARealError()
    {
        var d = Deployment(DeploymentStatus.Failed, Target(TargetStatus.Failed, "Annulé."), Target(TargetStatus.Failed, "Hors ligne"));
        Assert.Equal("Failed", DeploymentSummary.StatusOf(d));
        Assert.Equal("Failed", DeploymentSummary.TargetStatusOf(d.Targets[1]));
    }

    [Fact]
    public void RunningAndSuccessful_AreKept()
    {
        Assert.Equal("Running", DeploymentSummary.StatusOf(Deployment(DeploymentStatus.Running, Target(TargetStatus.Running))));
        Assert.Equal("Successful", DeploymentSummary.StatusOf(Deployment(DeploymentStatus.Successful, Target(TargetStatus.Successful))));
    }

    [Fact]
    public void Counts_SplitEveryState()
    {
        var d = Deployment(DeploymentStatus.Running,
            Target(TargetStatus.Queued), Target(TargetStatus.Queued), Target(TargetStatus.Running),
            Target(TargetStatus.Successful), Target(TargetStatus.Failed, "Code 1"), Target(TargetStatus.Failed, "Annulé."));

        Assert.Equal(new DeploymentSummary.Counts(6, 2, 1, 1, 1, 1), DeploymentSummary.Count(d));
    }
}
