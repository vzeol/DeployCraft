extern alias ConsoleApp;

using ConsoleApp::DeployCraft.Console;
using ConsoleApp::DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Logic of the console main window that does not need a window: lists, details, banner, refresh.</summary>
public class ConsolePresentationTests
{
    [Fact]
    public void StepSummary_DescribesEachStep()
    {
        var nested = new Package { Id = 7, Name = "Base", Steps = [new(), new()] };

        Assert.Equal("Copie (désactivée)", StepSummary.Name(new PackageStep { Name = "Copie", Enabled = false }));
        Assert.Equal("Copie de fichiers", StepSummary.TypeText(StepType.CopyFiles));
        Assert.Equal("app.msi /qn", StepSummary.Details(new PackageStep { Type = StepType.Install, File = "app.msi", Arguments = "/qn" }, _ => null));
        Assert.Equal("Paquet : Base (2 étapes)", StepSummary.Details(new PackageStep { Type = StepType.Nested, NestedPackageId = 7 }, id => id == 7 ? nested : null));
        Assert.Equal("first", StepSummary.Details(new PackageStep { Type = StepType.Command, Command = "first\nsecond" }, _ => null));
    }

    [Fact]
    public void PackageDetails_ShowWhereSettingsComeFrom()
    {
        var package = new Package { Name = "Tool", TimeoutMinutes = 15, Steps = [new() { Enabled = true }, new() { Enabled = false }] };
        var rows = PackageDetailRows.For(package, new DeploymentDefaults { TimeoutMinutes = 60 }, [], [package]).ToDictionary(r => r.Label, r => r.Value);

        Assert.Equal("15 min", rows["Délai max par étape"]);
        Assert.StartsWith("Préférences (", rows["Mode de copie"]);
        Assert.Equal("2, dont 1 désactivée(s)", rows["Étapes"]);
        Assert.Equal("—", rows["Version"]);
    }

    [Fact]
    public void ServerIssues_ListsEveryProblem()
    {
        var ok = new ServerStatus("SERVEUR-01", "SYSTEM", true, new Credential { User = "admin" }, 0, "1.0.0", "admin");
        Assert.Equal("", ServerIssues.For(ok));

        var bad = ok with { Elevated = false, DefaultCredential = null, UnreadableCredentials = 1 };
        Assert.Equal(3, ServerIssues.For(bad).Split(Environment.NewLine).Length);
    }

    [Fact]
    public async Task RefreshQueue_ReplaysARequestMadeDuringARefresh()
    {
        var passes = new List<bool>();
        var gate = new TaskCompletionSource();
        RefreshQueue? queue = null;
        queue = new RefreshQueue(async packages =>
        {
            passes.Add(packages);
            if (passes.Count == 1)
            {
                await queue!.RunAsync(packages: true);
                await queue.RunAsync(packages: false);
                await gate.Task;
            }
        });

        var first = queue.RunAsync(packages: false);
        gate.SetResult();
        await first;

        Assert.Equal([false, true], passes);
    }

    private static LibraryItem Item(string id, long? packageId = null, string? stage = null) =>
        new() { Id = id, Name = id, LatestVersion = "1.0", PackageId = packageId, Stage = stage };

    [Fact]
    public void LibraryList_ReplacesOnlyChangedRows()
    {
        var list = new LibraryList();
        list.Load("", new LibraryInfo { Items = [Item("A"), Item("B"), Item("C")] });
        var first = list.Rows[0];
        var replaced = new List<string>();

        list.Merge([Item("B", stage: "Téléchargement")], (_, row) => replaced.Add(row.Id));

        Assert.Equal(["B"], replaced);
        Assert.Same(first, list.Rows[0]);
        Assert.StartsWith("Téléchargement", list.Rows[1].State);
    }

    [Fact]
    public void LibraryList_RowWithoutStateAnymore_GoesBackToNotDownloaded()
    {
        var list = new LibraryList();
        list.Load("", new LibraryInfo { Items = [Item("A")] });
        list.Merge([Item("A", packageId: 3)]);
        Assert.Equal("À jour", list.Rows[0].State);

        list.Merge([]);

        Assert.Equal("Pas encore téléchargé", list.Rows[0].State);
    }

    [Fact]
    public void LibraryList_ReloadsOnlyWhenSearchOrCatalogChange()
    {
        var date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var list = new LibraryList();
        list.Load("vlc", new LibraryInfo { LastRefreshUtc = date });

        Assert.False(list.NeedsReload("vlc", date));
        Assert.True(list.NeedsReload("7zip", date));
        Assert.True(list.NeedsReload("vlc", date.AddDays(1)));
    }

    [Fact]
    public void ScheduleRow_CountsDown()
    {
        var row = new ScheduleRow(new Schedule { Enabled = true, NextRunUtc = DateTime.UtcNow.AddDays(2).AddHours(1) });

        Assert.StartsWith("dans 2 j", row.Countdown);
        Assert.Equal("Désactivée", new ScheduleRow(new Schedule { Enabled = false }).Next);
    }

    private static List<PackageStep> Steps(params string[] names) => names.Select(n => new PackageStep { Name = n }).ToList();

    [Fact]
    public void StepList_InsertsAfterTheSelectedStepOrAtTheEnd()
    {
        var steps = Steps("a", "b");
        StepList.InsertAfter(steps, steps[0], new PackageStep { Name = "x" });
        StepList.InsertAfter(steps, null, new PackageStep { Name = "z" });

        Assert.Equal(["a", "x", "b", "z"], steps.Select(s => s.Name));
    }

    [Fact]
    public void StepList_MovesWithinBounds()
    {
        var steps = Steps("a", "b", "c");

        Assert.False(StepList.CanMove(steps, steps[0], -1));
        Assert.False(StepList.CanMove(steps, null, 1));
        StepList.Move(steps, steps[0], 1);
        StepList.Move(steps, steps[2], 1);

        Assert.Equal(["b", "a", "c"], steps.Select(s => s.Name));
    }

    [Fact]
    public void StepList_DuplicateIsADeepCopy()
    {
        var step = new PackageStep { Name = "Install", Conditions = new Conditions { FilePath = "app-folder" } };

        var copy = StepList.Duplicate(step);
        copy.Conditions.FilePath = "changed";

        Assert.Equal("Install (copie)", copy.Name);
        Assert.Equal("app-folder", step.Conditions.FilePath);
        Assert.Equal("", StepList.Duplicate(new PackageStep { Name = " " }).Name);
    }

    [Fact]
    public void StepList_RemoveSelectsTheNextStep()
    {
        var steps = Steps("a", "b", "c");

        Assert.Equal("c", StepList.Remove(steps, steps[1])?.Name);
        Assert.Equal("a", StepList.Remove(steps, steps[1])?.Name);
        Assert.Null(StepList.Remove(steps, steps[0]));
    }

    [Theory]
    [InlineData("setup.msi", true, true, true)]
    [InlineData("patch.msp", true, false, true)]
    [InlineData("kb.msu", true, true, false)]
    [InlineData("setup.exe", false, false, true)]
    public void StepTexts_InstallOptionsDependOnTheFile(string file, bool msi, bool operation, bool repair) =>
        Assert.Equal((msi, operation, repair), StepTexts.InstallOptions(file));

    [Fact]
    public void StepTexts_HeaderAndHints()
    {
        Assert.Equal("Étape 2 (Commande)", StepTexts.Header(new PackageStep { Type = StepType.Command, Name = "" }, 1));
        Assert.Equal("1. Tool (désactivée)", StepTexts.Header(new PackageStep { Name = "Tool", Enabled = false }, 0));
        Assert.Contains("/VERYSILENT", StepTexts.SilentHint("setup.EXE"));
        Assert.Contains("45 min", StepTexts.TimeoutHint(StepType.Command, 45));
    }

    [Fact]
    public void PathCheck_AsksOnlyNewPathsUnlessAgain()
    {
        var check = new PathCheck();
        check.Record(["a.msi", "b.msi"], ["A.MSI"]);

        Assert.Contains("a.msi", check.Missing);
        Assert.Equal(["c.msi"], check.ToCheck(["a.msi", "c.msi"]));
        Assert.Equal(2, check.ToCheck(["a.msi", "b.msi"], again: true).Count);

        check.Record(["a.msi"], []);
        Assert.Empty(check.Missing);
    }

    [Fact]
    public void StepRow_ListsSteps()
    {
        var rows = StepRow.For([new PackageStep { Name = "Pause", Type = StepType.Pause, PauseSeconds = 30 }], _ => null);

        Assert.Equal(new StepRow(1, "Pause", "Pause", "", "30 s"), Assert.Single(rows));
    }

    [Theory]
    [InlineData(StepType.Install, true, false, true)]
    [InlineData(StepType.Command, false, true, true)]
    [InlineData(StepType.Pause, false, false, false)]
    [InlineData(StepType.Reboot, false, false, false)]
    public void StepTexts_FieldsDependOnTheType(StepType type, bool install, bool command, bool codes)
    {
        var fields = StepTexts.Fields(type);

        Assert.Equal(install, fields.Install);
        Assert.Equal(command || install, fields.AdditionalFiles);
        Assert.Equal(command, fields.Command);
        Assert.Equal(codes, fields.SuccessCodes);
    }

    [Fact]
    public void StepDetailRow_ShowsStepsBeforeTheStartStepAsSkipped()
    {
        var deployment = new Deployment
        {
            Id = 4,
            Snapshot = new Package { Steps = [new() { Name = "a" }, new() { Name = "b" }, new() { Name = "c" }] },
        };
        var target = new DeploymentTarget
        {
            StartStep = 2,
            Status = TargetStatus.Running,
            Steps = [new StepOutcome { Name = "b", State = "success", ExitCode = 0 }],
        };

        var rows = StepDetailRow.For(deployment, target);

        Assert.Equal(["skipped", "success", "Queued"], rows.Select(r => r.Status));
        Assert.Equal("0", rows[1].ReturnCode);
    }
}
