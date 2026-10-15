using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Paquet imbriqué : remplacé par ses étapes au lancement (noms, sections, conditions, délais), boucles refusées.</summary>
public class NestedPackageTests
{
    private readonly Dictionary<long, Package> _library = [];

    private Package Add(long id, string name, params PackageStep[] steps)
    {
        var package = new Package { Id = id, Name = name, Steps = [.. steps] };
        _library[id] = package;
        return package;
    }

    private Package? Find(long id) => _library.GetValueOrDefault(id);

    private static PackageStep Cmd(string name, StepSection section = StepSection.Main) =>
        new() { Name = name, Type = StepType.Command, Command = "echo " + name, Section = section };

    private static PackageStep Nested(long id, string name = "", StepSection section = StepSection.Main) =>
        new() { Name = name, Type = StepType.Nested, NestedPackageId = id, Section = section };

    [Fact]
    public void NestedStep_IsReplacedByTheNestedSteps_InPlace()
    {
        Add(2, "Outils", Cmd("a"), Cmd("b"));
        var parent = Add(1, "Poste", Cmd("début"), Nested(2), Cmd("fin"));

        var flat = parent.Flatten(Find);

        Assert.Equal(["début", "Outils › a", "Outils › b", "fin"], flat.Steps.Select(s => s.Name));
        Assert.Equal(StepType.Nested, parent.Steps[1].Type); // l'original n'est pas modifié
    }

    [Fact]
    public void NestedSections_StayInsideTheBlock_InTheSectionOfTheParentStep()
    {
        Add(2, "Outils", Cmd("après", StepSection.After), Cmd("principale"), Cmd("avant", StepSection.Before));
        var parent = Add(1, "Poste", Cmd("x"), Nested(2, "Bloc", StepSection.After));

        var flat = parent.Flatten(Find);

        Assert.Equal(["x", "Bloc › avant", "Bloc › principale", "Bloc › après"], flat.Steps.Select(s => s.Name));
        Assert.All(flat.Steps.Skip(1), s => Assert.Equal(StepSection.After, s.Section));
    }

    [Fact]
    public void Conditions_OfTheStepAndOfTheNestedPackage_AreAddedToEachStep()
    {
        var nested = Add(2, "Outils", Cmd("a"));
        nested.Conditions = new Conditions { Architecture = ArchitectureCondition.X64 };
        var step = Nested(2, "Bloc");
        step.Conditions = new Conditions { LoggedOn = LoggedOnCondition.NotLoggedOn };
        var parent = Add(1, "Poste", step);

        var a = parent.Flatten(Find).Steps[0];

        Assert.Equal(["Conditions de l'étape « Bloc »", "Conditions du paquet imbriqué « Outils »"], a.ExtraConditions!.Select(g => g.Label));
        var job = TargetSession.ToJobStep(a, parent, RunAsMode.DeployUser);
        Assert.Equal(2, job.ExtraConditions!.Count);
        Assert.Equal("x64", job.ExtraConditions[1].Conditions!.Architecture);
    }

    [Fact]
    public void Runner_SkipsAStepWhoseNestedPackageConditionsAreUnmet()
    {
        var status = new RunStatus();
        var ran = new List<string>();
        StepSequence.Run(new Job
        {
            Steps =
            [
                new JobStep
                {
                    Name = "Outils › a",
                    ExtraConditions = [new JobConditionGroup { Label = "Conditions du paquet imbriqué « Outils »", Conditions = new JobConditions { Architecture = "x86" } }],
                },
                new JobStep { Name = "fin" },
            ],
        }, new Facts(), status, (_, s, r) => { ran.Add(s.Name); r.State = StepSequence.Success; }, () => { }, () => false, _ => { });

        Assert.Equal(["fin"], ran);
        Assert.Equal("skipped", status.Steps[0].State);
        Assert.StartsWith("Conditions du paquet imbriqué « Outils » non remplies.", status.Steps[0].Error);
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

    [Fact]
    public void DisabledNestedStep_DisablesAllItsSteps_AndDisabledInnerStepsStayDisabled()
    {
        var off = Cmd("b");
        off.Enabled = false;
        Add(2, "Outils", Cmd("a"), off);
        var disabledParent = Nested(2);
        disabledParent.Enabled = false;

        Assert.All(Add(1, "Poste", disabledParent).Flatten(Find).Steps, s => Assert.False(s.Enabled));
        Assert.Equal([true, false], Add(3, "Autre", Nested(2)).Flatten(Find).Steps.Select(s => s.Enabled));
    }

    [Fact]
    public void Timeout_ComesFromTheStep_ThenTheNestedPackage_ThenThePreferences()
    {
        var own = Cmd("propre");
        own.TimeoutMinutes = 7;
        var nested = Add(2, "Outils", own, Cmd("sans"));
        nested.TimeoutMinutes = 15;
        Add(3, "Simple", Cmd("sans"));
        var parent = Add(1, "Poste", Nested(2), Nested(3));
        parent.TimeoutMinutes = 99;

        var resolved = parent.Resolve(new DeploymentDefaults { TimeoutMinutes = 45 }, Find);

        Assert.Equal([7, 15, 45], resolved.Steps.Select(s => s.TimeoutMinutes));
    }

    [Fact]
    public void Loops_AreRefused_DirectOrIndirect()
    {
        Add(1, "A", Nested(1));
        Assert.Contains("boucle", Assert.Throws<ArgumentException>(() => _library[1].Flatten(Find)).Message);

        Add(2, "B", Nested(3));
        Add(3, "C", Nested(2));
        var message = Assert.Throws<ArgumentException>(() => _library[2].Flatten(Find)).Message;
        Assert.Contains("boucle", message);
        Assert.Contains("B › C › B", message); // le chemin de la boucle
    }

    [Fact]
    public void Depth_IsLimited()
    {
        Add(10, "Feuille", Cmd("x"));
        for (var id = 11; id <= 16; id++) Add(id, $"N{id}", Nested(id - 1));

        Assert.Single(_library[15].Flatten(Find).Steps); // 5 niveaux d'imbrication
        Assert.Contains("niveaux", Assert.Throws<ArgumentException>(() => _library[16].Flatten(Find)).Message);
    }

    [Fact]
    public void MissingOrUnchosenNestedPackage_IsAClearError()
    {
        Assert.Contains("introuvable", Assert.Throws<ArgumentException>(() => Add(1, "A", Nested(42)).Flatten(Find)).Message);
        Assert.Contains("choisissez", Assert.Throws<ArgumentException>(() =>
            Add(2, "B", new PackageStep { Type = StepType.Nested }).Flatten(Find)).Message);
    }

    [Fact]
    public void UsedPackage_IsListedWithItsUsers()
    {
        Add(2, "Outils", Cmd("a"));
        Add(1, "Poste", Nested(2));
        Add(3, "Serveur", Cmd("b"));

        Assert.Equal(["Poste"], Package.UsersOf(_library.Values, 2).Select(p => p.Name));
        Assert.Empty(Package.UsersOf(_library.Values, 3));
    }

    [Fact]
    public void AnUnflattenedNestedStep_IsNeverSentToTheComputer() =>
        Assert.Throws<DeployException>(() => TargetSession.ToJobStep(Nested(2), new Package(), RunAsMode.DeployUser));
}
