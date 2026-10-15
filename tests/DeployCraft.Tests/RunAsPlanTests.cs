using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>« Exécuter en tant que » par étape : identité réelle, tranches lancées l'une après l'autre, arrêt entre tranches.</summary>
public class RunAsPlanTests
{
    private const RunAsMode D = RunAsMode.DeployUser, S = RunAsMode.LocalSystem, U = RunAsMode.LoggedOnUser;

    [Fact]
    public void Effective_StepChoice_ThenDeployment_AndSystemWithoutAccount()
    {
        Assert.Equal(S, RunAsPlan.Effective(new PackageStep(), S, hasAccount: true));
        Assert.Equal(U, RunAsPlan.Effective(new PackageStep { RunAs = U }, D, hasAccount: true));
        // Le choix du déploiement ne remplace que les étapes « Paramètre du paquet ».
        Assert.Equal(D, RunAsPlan.Effective(new PackageStep { RunAs = D }, U, hasAccount: true));
        Assert.Equal(S, RunAsPlan.Effective(new PackageStep { RunAs = D }, S, hasAccount: false));
    }

    [Fact]
    public void Split_GroupsConsecutiveStepsOfTheSameService()
    {
        var tranches = RunAsPlan.Split([D, D, S, U, D]);

        Assert.Equal(
            [new RunAsPlan.Tranche(0, 2, true), new RunAsPlan.Tranche(2, 2, false), new RunAsPlan.Tranche(4, 1, true)],
            tranches);
        Assert.Single(RunAsPlan.Split([S, S, S])); // un paquet sans réglage par étape : un seul service, comme avant
        Assert.Empty(RunAsPlan.Split([]));
    }

    private static RunnerStatus Status(string state, bool halt, string? reason, params string[] states) => new()
    {
        State = state,
        Halt = halt,
        HaltReason = reason,
        Steps = [.. states.Select((s, i) => new RunnerStepResult { Name = $"e{i}", State = s })],
    };

    [Fact]
    public void Combine_KeepsEarlierResults_AndMarksLaterStepsSkippedWhenATrancheHalts()
    {
        var done = new List<RunnerStepResult> { new() { Name = "a", State = "success" } };

        var halted = RunAsPlan.Combine(done, Status("done", true, "Paquet arrêté en succès par l'étape 2 « b ».", "stopped"), ["c", "d"], last: false);
        Assert.Equal("done", halted.State);
        Assert.Equal(["success", "stopped", "skipped", "skipped"], halted.Steps.Select(s => s.State));
        Assert.Equal("Paquet arrêté en succès par l'étape 2 « b ».", halted.Steps[3].Error);

        var goingOn = RunAsPlan.Combine(done, Status("done", false, null, "success"), ["c"], last: false);
        Assert.Equal("running", goingOn.State);
        Assert.Equal(2, goingOn.Steps.Count);

        Assert.Equal("done", RunAsPlan.Combine(done, Status("done", false, null, "success"), [], last: true).State);

        // Runner interrompu par une erreur sans demander l'arrêt : on n'enchaîne pas non plus.
        var crashed = Status("done", false, null, "failed");
        crashed.Error = "Accès refusé.";
        var afterCrash = RunAsPlan.Combine(done, crashed, ["c"], last: false);
        Assert.Equal("done", afterCrash.State);
        Assert.True(RunAsPlan.Stops(crashed));
        Assert.Equal("Tranche précédente interrompue : Accès refusé.", afterCrash.Steps[^1].Error);
        Assert.Equal("running", RunAsPlan.Combine(done, Status("running", false, null, "running"), ["c"], last: false).State);
    }

    private sealed class Facts : ITargetFacts
    {
        public string WindowsFamily => "11";
        public bool Is64Bit => true;
        public int PowerShellVersion => 5;
        public bool UserLoggedOn => false;
        public bool PathExists(string path) => false;
        public bool RegistryExists(string key, string value) => false;
    }

    private static RunStatus Run(Job job, string failing = "ko")
    {
        var status = new RunStatus();
        StepSequence.Run(job, new Facts(), status,
            (_, s, r) => r.State = s.Name.StartsWith(failing) ? StepSequence.Failed : StepSequence.Success,
            () => { }, () => false, _ => { });
        return status;
    }

    [Fact]
    public void Runner_SaysWhetherTheNextTrancheMayRun_AndNumbersStepsFromThePackage()
    {
        var ok = Run(new Job { Steps = [new JobStep { Name = "a" }] });
        Assert.False(ok.Halt);

        var failed = Run(new Job { Steps = [new JobStep { Name = "ko" }] });
        Assert.True(failed.Halt);
        Assert.Null(failed.HaltReason);

        var continued = Run(new Job { Steps = [new JobStep { Name = "ko", ContinueOnError = true }] });
        Assert.False(continued.Halt);

        var stopped = Run(new Job { FirstNumber = 4, Steps = [new JobStep { Name = "ko", StopSuccessOnError = true }] });
        Assert.True(stopped.Halt);
        Assert.Equal("Paquet arrêté en succès par l'étape 4 « ko ».", stopped.HaltReason);

        var unmet = Run(new Job { Conditions = new JobConditions { Windows = ["10"] }, Steps = [new JobStep { Name = "a" }] });
        Assert.True(unmet.Halt);
        Assert.StartsWith("Conditions du paquet non remplies.", unmet.HaltReason);
    }

    [Fact]
    public void NestedSteps_TakeTheIdentityOfTheNestedStep_UnlessTheyHaveTheirOwn()
    {
        var nested = new Package { Id = 2, Name = "Outils", Steps = [new() { Name = "a", Type = StepType.Command }, new() { Name = "b", Type = StepType.Command, RunAs = U }] };
        var parent = new Package { Id = 1, Name = "Poste", Steps = [new() { Type = StepType.Nested, NestedPackageId = 2, RunAs = S }] };

        var flat = parent.Flatten(id => id == 2 ? nested : null);

        Assert.Equal([S, U], flat.Steps.Select(s => s.RunAs));
    }

    [Fact]
    public void JobStep_RunsInTheLoggedOnUserSession_OnlyForThatIdentity()
    {
        var package = new Package();
        Assert.True(TargetSession.ToJobStep(new PackageStep(), package, U).RunAsLoggedOnUser);
        Assert.False(TargetSession.ToJobStep(new PackageStep(), package, S).RunAsLoggedOnUser);
    }
}
