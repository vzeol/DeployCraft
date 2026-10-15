using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Étapes désactivées, « arrêter en succès » et pause : consignes du serveur, enchaînement par le runner, résultat du poste.</summary>
public sealed class StepFlowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class NoFacts : ITargetFacts
    {
        public string WindowsFamily => "11";
        public bool Is64Bit => true;
        public int PowerShellVersion => 5;
        public bool UserLoggedOn => false;
        public bool PathExists(string path) => false;
        public bool RegistryExists(string key, string value) => false;
    }

    /// <summary>Exécute le job sur un poste simulé : les étapes dont le nom commence par « ko » échouent.</summary>
    private static (RunStatus Status, List<string> Ran) Run(params JobStep[] steps)
    {
        var ran = new List<string>();
        var status = new RunStatus();
        StepSequence.Run(new Job { Steps = [.. steps] }, new NoFacts(), status,
            (_, step, result) =>
            {
                ran.Add(step.Name);
                result.ExitCode = step.Name.StartsWith("ko") ? 1 : 0;
                result.State = result.ExitCode == 0 ? StepSequence.Success : StepSequence.Failed;
            },
            save: () => { }, aborted: () => false, log: _ => { });
        return (status, ran);
    }

    private static string[] States(RunStatus status) => status.Steps.Select(s => s.State).ToArray();

    [Fact]
    public void DisabledStep_IsSkipped_AndThePackageGoesOn()
    {
        var (status, ran) = Run(new JobStep { Name = "a", Disabled = true }, new JobStep { Name = "b" });

        Assert.Equal(["b"], ran);
        Assert.Equal(["skipped", "success"], States(status));
        Assert.Equal("Étape désactivée.", status.Steps[0].Error);
    }

    [Fact]
    public void DisabledStep_SaysSo_EvenWhenThePackageConditionsAreUnmet()
    {
        var status = new RunStatus();
        StepSequence.Run(new Job
        {
            Conditions = new JobConditions { Windows = ["10"] },
            Steps = [new JobStep { Name = "a", Disabled = true }, new JobStep { Name = "b" }],
        }, new NoFacts(), status, (_, _, _) => throw new InvalidOperationException(), () => { }, () => false, _ => { });

        Assert.Equal("Étape désactivée.", status.Steps[0].Error);
        Assert.StartsWith("Conditions du paquet non remplies.", status.Steps[1].Error);
    }

    [Fact]
    public void StopSuccess_OnFailure_StopsThePackage_AndExplainsTheSkippedSteps()
    {
        var (status, ran) = Run(new JobStep { Name = "ko", StopSuccessOnError = true }, new JobStep { Name = "b" });

        Assert.Equal(["ko"], ran);
        Assert.Equal(["stopped", "skipped"], States(status));
        Assert.Equal(1, status.Steps[0].ExitCode);
        Assert.Equal("Paquet arrêté en succès par l'étape 1 « ko ».", status.Steps[1].Error);
    }

    [Fact]
    public void StopSuccess_OnSuccess_GoesOn()
    {
        var (status, ran) = Run(new JobStep { Name = "a", StopSuccessOnError = true }, new JobStep { Name = "b" });

        Assert.Equal(["a", "b"], ran);
        Assert.Equal(["success", "success"], States(status));
    }

    [Fact]
    public void Stop_And_Continue_KeepTheirBehaviour()
    {
        var (stop, stopRan) = Run(new JobStep { Name = "ko" }, new JobStep { Name = "b" });
        Assert.Equal(["ko"], stopRan);
        Assert.Equal(["failed", "skipped"], States(stop));
        Assert.Null(stop.Steps[1].Error);

        var (cont, contRan) = Run(new JobStep { Name = "ko", ContinueOnError = true }, new JobStep { Name = "b" });
        Assert.Equal(["ko", "b"], contRan);
        Assert.Equal(["failed", "success"], States(cont));
    }

    [Fact]
    public void JobStep_CarriesEnabledAndErrorMode()
    {
        var package = new Package { TimeoutMinutes = 10 };

        var stopSuccess = TargetSession.ToJobStep(new PackageStep { Name = "a", ErrorMode = ErrorMode.StopSuccess }, package, RunAsMode.DeployUser);
        Assert.True(stopSuccess.StopSuccessOnError);
        Assert.False(stopSuccess.ContinueOnError);
        Assert.False(stopSuccess.Disabled);

        var disabled = TargetSession.ToJobStep(new PackageStep { Name = "b", Enabled = false, ErrorMode = ErrorMode.Continue }, package, RunAsMode.DeployUser);
        Assert.True(disabled.Disabled);
        Assert.True(disabled.ContinueOnError);
        Assert.False(disabled.StopSuccessOnError);
    }

    [Fact]
    public void Pause_WaitsTheWholeDuration_InShortSlices()
    {
        var slept = new List<int>();

        Assert.True(StepSequence.Wait(2, () => false, slept.Add));
        Assert.Equal(2000, slept.Sum());
        Assert.All(slept, ms => Assert.InRange(ms, 1, 500));
    }

    [Fact]
    public void Pause_StopsAsSoonAsTheDeploymentIsAborted()
    {
        var slept = 0;

        Assert.False(StepSequence.Wait(3600, () => slept >= 1000, ms => slept += ms));
        Assert.Equal(1000, slept);
    }

    [Fact]
    public void JobStep_CarriesThePauseDuration_OnlyForAPause()
    {
        var package = new Package();

        var pause = TargetSession.ToJobStep(new PackageStep { Type = StepType.Pause, PauseSeconds = 45 }, package, RunAsMode.DeployUser);
        Assert.Equal("pause", pause.Type);
        Assert.Equal(45, pause.Seconds);

        // Pause plus longue que le délai de l'étape : le serveur ne doit pas abandonner le poste pendant l'attente.
        var longPause = TargetSession.ToJobStep(new PackageStep { Type = StepType.Pause, PauseSeconds = 7200, TimeoutMinutes = 60 }, package, RunAsMode.DeployUser);
        Assert.True(longPause.TimeoutSeconds > 7200);

        Assert.Equal(0, TargetSession.ToJobStep(new PackageStep { Type = StepType.Command, PauseSeconds = 45 }, package, RunAsMode.DeployUser).Seconds);
    }

    [Fact]
    public void PackageSavedWithSections_IsReadAsIs_AndRunsInItsStoredOrder_WithASingleStop()
    {
        // Paquet enregistré en 0.11.0, quand les étapes avaient une section (déjà triées Avant, Principales, Après).
        var package = System.Text.Json.JsonSerializer.Deserialize<Package>("""
            {"name":"Ancien","steps":[
              {"name":"avant","type":"Command","command":"echo 1","section":"Before"},
              {"name":"ko","type":"Command","command":"exit 1","section":"Main"},
              {"name":"nettoyage","type":"Command","command":"echo 3","section":"After"}]}
            """, Store.Json)!;
        var resolved = package.Resolve(new DeploymentDefaults());
        var job = new Job();
        foreach (var step in TargetSession.StepsFrom(resolved, 1))
        {
            var j = TargetSession.ToJobStep(step, resolved, RunAsMode.DeployUser);
            job.Steps.Add(new JobStep { Name = j.Name, ContinueOnError = j.ContinueOnError, StopSuccessOnError = j.StopSuccessOnError });
        }

        var ran = new List<string>();
        var status = new RunStatus();
        StepSequence.Run(job, new NoFacts(), status,
            (_, s, r) => { ran.Add(s.Name); r.State = s.Name == "ko" ? StepSequence.Failed : StepSequence.Success; },
            () => { }, () => false, _ => { });

        Assert.Equal(["avant", "ko"], ran); // l'ancienne étape « Après » est une étape ordinaire : l'échec l'arrête
        Assert.Equal(["success", "failed", "skipped"], States(status));
    }

    private static StepOutcome Step(string state, string? error = null, int? code = null) =>
        new() { Name = state, State = state, Error = error, ExitCode = code };

    [Fact]
    public void Outcome_StoppedStep_CountsTheComputerSuccessful()
    {
        var (status, error) = DeploymentEngine.Outcome([Step("success"), Step("stopped", code: 1), Step("skipped")]);

        Assert.Equal(TargetStatus.Successful, status);
        Assert.Contains("Arrêt en succès", error);
        Assert.Contains("code retour 1", error);
    }

    [Fact]
    public void Outcome_FailedStep_WinsOverEverythingElse()
    {
        var (status, error) = DeploymentEngine.Outcome([Step("failed", code: 5), Step("stopped", code: 1)]);

        Assert.Equal(TargetStatus.Failed, status);
        Assert.Contains("code retour 5", error);
    }

    [Fact]
    public void Outcome_DisabledSteps_DoNotChangeASuccess()
    {
        Assert.Equal((TargetStatus.Successful, null), DeploymentEngine.Outcome([Step("skipped", "Étape désactivée."), Step("success")]));
        Assert.Equal((TargetStatus.Successful, "Étape désactivée."), DeploymentEngine.Outcome([Step("skipped", "Étape désactivée.")]));
        Assert.Equal(TargetStatus.Failed, DeploymentEngine.Outcome([]).Status);
    }

    [Fact]
    public void Store_KeepsEnabledAndErrorMode_AndOldStepsStayEnabled()
    {
        var store = new Store(Path.Combine(_dir, "test.db"));
        var saved = store.SavePackage(new Package
        {
            Name = "Outil",
            Steps = [new() { Name = "a", Enabled = false, ErrorMode = ErrorMode.StopSuccess }],
        });

        var step = store.GetPackage(saved.Id)!.Steps[0];
        Assert.False(step.Enabled);
        Assert.Equal(ErrorMode.StopSuccess, step.ErrorMode);

        // Étapes enregistrées avant cette version : pas de champ « enabled ».
        var old = System.Text.Json.JsonSerializer.Deserialize<PackageStep>("""{"name":"a","errorMode":"Continue"}""", Store.Json)!;
        Assert.True(old.Enabled);
        Assert.Equal(ErrorMode.Continue, old.ErrorMode);
    }
}
