using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Nouvelle planification pré-remplie à partir d'un déploiement.</summary>
public class ScheduleFromDeploymentTests
{
    [Fact]
    public void TakesPackageTargetsAndOptions_WithoutTriggers()
    {
        var deployment = new Deployment
        {
            PackageId = 7,
            PackageName = "Navigateur",
            RunAsMode = RunAsMode.LocalSystem,
            CopyMode = CopyMode.Pull,
            CredentialId = 3,
            Targets = [new() { Computer = "PC-TEST-01" }, new() { Computer = "pc-test-01" }, new() { Computer = "PC-TEST-02" }],
        };

        var schedule = Schedule.From(deployment);

        Assert.Equal(0, schedule.Id);
        Assert.Equal("Navigateur", schedule.Name);
        Assert.Equal([7L], schedule.PackageIds);
        Assert.Equal(["PC-TEST-01", "PC-TEST-02"], schedule.Targets);
        Assert.Equal(RunAsMode.LocalSystem, schedule.RunAs);
        Assert.Equal(CopyMode.Pull, schedule.CopyMode);
        Assert.Equal(3L, schedule.CredentialId);
        Assert.Empty(schedule.Triggers);
        Assert.True(schedule.Enabled);
    }
}
