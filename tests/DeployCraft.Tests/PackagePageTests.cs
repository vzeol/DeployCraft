using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class PackagePageTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    [Theory]
    [InlineData("fr", 0, "", "", "Tools", "Paquet · 0 étapes · modifié le local time")]
    [InlineData("fr", 1, "1.0", "Description", "Tools 1.0", "Description · 1 étape · modifié le local time")]
    [InlineData("fr", 2, " ", " ", "Tools", "Paquet · 2 étapes · modifié le local time")]
    [InlineData("en", 0, "", "", "Tools", "Package · 0 steps · modified on local time")]
    [InlineData("en", 1, "1.0", "Description", "Tools 1.0", "Description · 1 step · modified on local time")]
    [InlineData("en", 2, " ", " ", "Tools", "Package · 2 steps · modified on local time")]
    public async Task Overview_PreservesTitleSubtitleStepsAndDetails(string language, int steps, string version,
        string description, string title, string subtitle)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var nested = new Package { Id = 2, Name = "Nested tools" };
        var package = new Package
        {
            Id = 1,
            Name = "Tools",
            Version = version,
            Description = description,
            Steps = Enumerable.Range(0, steps).Select(_ => new PackageStep { Type = StepType.Nested, NestedPackageId = 2 }).ToList(),
        };
        var list = PackageListTests.List([package, nested]);
        await list.RefreshAsync();
        var page = new PackagePage(list, () => Task.FromResult(new List<Schedule>()), _ => "local time");

        var overview = page.Overview(package);

        Assert.Equal(title, overview.Title);
        Assert.Equal(subtitle, overview.Subtitle);
        Assert.Equal(steps, overview.Steps.Count);
        Assert.Equal(Enumerable.Range(1, steps), overview.Steps.Select(s => s.Number));
        Assert.All(overview.Steps, s => Assert.Contains("Nested tools", s.Details));
        Assert.Equal(PackageDetailRows.For(package, list.Defaults, list.Folders, list.Packages), overview.Details);
    }

    [Theory]
    [InlineData("fr", 0, "Ce paquet ne fait partie d'aucune planification.")]
    [InlineData("fr", 1, "1 planification(s) avec ce paquet")]
    [InlineData("fr", 2, "2 planification(s) avec ce paquet")]
    [InlineData("en", 0, "This package is not part of any schedule.")]
    [InlineData("en", 1, "1 schedule(s) with this package")]
    [InlineData("en", 2, "2 schedule(s) with this package")]
    public async Task Refresh_FiltersActivityAndKeepsOnlyAnExistingSelectedSchedule(string language, int count, string text)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var schedules = Enumerable.Range(1, count).Select(i => new Schedule { Id = i, PackageIds = [7] }).ToList();
        schedules.Add(new Schedule { Id = 99, PackageIds = [8] });
        var page = new PackagePage(PackageListTests.List(), () => Task.FromResult(schedules), _ => "local time");
        var deployment = new Deployment { PackageId = 7 };
        List<Deployment>? shown = null;

        var activity = await page.RefreshAsync(7, [deployment, new() { PackageId = 8 }], rows => shown = rows.ToList(), () => 1);

        Assert.Same(deployment, Assert.Single(Assert.IsType<List<Deployment>>(shown)));
        Assert.Equal(count, activity.Schedules.Count);
        Assert.Equal(text, activity.ScheduleCount);
        Assert.Equal(count == 0 ? null : (long?)1, activity.SelectedSchedule?.Id);
        var refreshed = await page.RefreshAsync(7, [], _ => { }, () => 99);
        Assert.Null(refreshed.SelectedSchedule);
    }

    [Fact]
    public async Task Refresh_ShowsDeploymentsBeforeSchedulesAndReadsSelectionAfterLoading()
    {
        var completion = new TaskCompletionSource<List<Schedule>>();
        var selected = 1L;
        var shown = false;
        var page = new PackagePage(PackageListTests.List(), () => completion.Task, _ => "local time");

        var refresh = page.RefreshAsync(7, [], _ => shown = true, () => selected);

        Assert.True(shown);
        Assert.False(refresh.IsCompleted);
        selected = 2;
        completion.SetResult([new() { Id = 1, PackageIds = [7] }, new() { Id = 2, PackageIds = [7] }]);
        Assert.Equal(2, (await refresh).SelectedSchedule?.Id);
    }

    [Fact]
    public async Task ScheduleFailure_PropagatesAfterShowingDeployments()
    {
        var error = new ApiException("Unavailable");
        var shown = false;
        var page = new PackagePage(PackageListTests.List(), () => Task.FromException<List<Schedule>>(error), _ => "local time");

        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => page.RefreshAsync(7, [], _ => shown = true,
            () => throw new InvalidOperationException("Selection must not be read."))));
        Assert.True(shown);
    }
}
