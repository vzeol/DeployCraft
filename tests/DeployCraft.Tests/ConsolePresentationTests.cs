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
}
