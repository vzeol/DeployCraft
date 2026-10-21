using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>New schedule pre-filled from a deployment.</summary>
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

    [Fact]
    public void Attach_AddsOnce_Detach_KeepsAtLeastOnePackage()
    {
        var schedule = new Schedule { PackageIds = [1] };

        Assert.True(schedule.Attach(2));
        Assert.False(schedule.Attach(2));
        Assert.Equal([1L, 2L], schedule.PackageIds);

        Assert.True(schedule.Detach(1));
        Assert.False(schedule.Detach(2)); // last remaining package
        Assert.False(schedule.Detach(9));
        Assert.Equal([2L], schedule.PackageIds);
    }
}
