using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Home loading and texts without opening a window or calling the service.</summary>
public sealed class HomePageTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private static HomePage Page(LibraryInfo? library = null) =>
        new(() => Task.FromResult(new List<Schedule>()), () => Task.FromResult(library ?? new LibraryInfo()),
            () => Now, _ => "local time");

    [Theory]
    [InlineData("fr", 0, "aucun déploiement en cours", "Tout est calme.")]
    [InlineData("fr", 1, "déploiement en cours", "1 poste en cours, 2 postes en attente")]
    [InlineData("fr", 2, "déploiements en cours", "2 postes en cours, 4 postes en attente")]
    [InlineData("en", 0, "no deployment running", "All quiet.")]
    [InlineData("en", 1, "deployment running", "1 computer running, 2 computers waiting")]
    [InlineData("en", 2, "deployments running", "2 computers running, 4 computers waiting")]
    public async Task Overview_PreservesRunningCountsAndPluralTexts(string language, int running, string text, string targets)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var deployments = Enumerable.Range(0, running).Select(_ => new Deployment
        {
            Status = DeploymentStatus.Running,
            Targets = [new() { Status = TargetStatus.Running }, new(), new()],
        }).ToList();
        HomeOverview? overview = null;

        await Page().RefreshAsync(deployments, value => overview = value, _ => { });

        Assert.NotNull(overview);
        Assert.Equal(running, overview.RunningDeployments);
        Assert.Equal(text, overview.RunningText);
        Assert.Equal(targets, overview.TargetsText);
    }

    [Theory]
    [InlineData("fr", "0 poste en cours, 0 poste en attente", "Aucune planification à venir.")]
    [InlineData("en", "0 computer running, 0 computer waiting", "No upcoming schedule.")]
    public async Task Overview_PreservesZeroTargetSingularAndEmptyScheduleText(string language, string targets, string noUpcoming)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        HomeOverview? overview = null;

        await Page().RefreshAsync([new Deployment { Status = DeploymentStatus.Queued }], value => overview = value, _ => { });

        Assert.NotNull(overview);
        Assert.Equal(targets, overview.TargetsText);
        Assert.Equal(noUpcoming, overview.NoUpcomingText);
        Assert.Empty(overview.RecentFailures);
        Assert.Empty(overview.NextSchedules);
    }

    [Theory]
    [InlineData("fr", 0, 0, "Aucun paquet téléchargé depuis la bibliothèque pour l'instant.")]
    [InlineData("fr", 1, 0, "1 paquet de la bibliothèque, tous à jour.")]
    [InlineData("fr", 2, 0, "2 paquets de la bibliothèque, tous à jour.")]
    [InlineData("fr", 1, 1, "1 mise à jour disponible sur 1 paquet de la bibliothèque.")]
    [InlineData("fr", 2, 2, "2 mises à jour disponibles sur 2 paquets de la bibliothèque.")]
    [InlineData("en", 0, 0, "No package downloaded from the library yet.")]
    [InlineData("en", 1, 0, "1 package from the library, all up to date.")]
    [InlineData("en", 2, 0, "2 packages from the library, all up to date.")]
    [InlineData("en", 1, 1, "1 update available out of 1 package from the library.")]
    [InlineData("en", 2, 2, "2 updates available out of 2 packages from the library.")]
    public async Task Library_PreservesEveryTextBranch(string language, int downloaded, int updates, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var library = new LibraryInfo
        {
            Items = Enumerable.Range(1, downloaded).Select(i => new LibraryItem { PackageId = i, UpdateAvailable = i <= updates }).ToList(),
        };
        LibraryInfo? shown = null;

        var text = await Page(library).RefreshAsync([], _ => { }, value => shown = value);

        Assert.Equal(expected, text);
        Assert.Same(library, shown);
    }

    [Theory]
    [InlineData("fr", "n° 17 · failure time")]
    [InlineData("en", "No. 17 · failure time")]
    public async Task Refresh_ShowsOverviewBeforeLibraryCompletes_AndUsesClockAfterSchedulesLoad(string language, string failureDetail)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var schedules = new TaskCompletionSource<List<Schedule>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var library = new TaskCompletionSource<LibraryInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var libraryRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        HomeOverview? overview = null;
        var page = new HomePage(
            () => { events.Add("schedules"); return schedules.Task; },
            () => { events.Add("library"); libraryRequested.SetResult(); return library.Task; },
            () => { events.Add("clock"); return Now; },
            date => date == Now.AddHours(1) ? "schedule time" : "failure time");
        var refresh = page.RefreshAsync([
            new Deployment { Id = 17, PackageName = "Tools", Status = DeploymentStatus.Failed, EndedUtc = Now.AddHours(-1) },
            new Deployment { Status = DeploymentStatus.Successful, EndedUtc = Now.AddHours(-2) },
            new Deployment { Status = DeploymentStatus.Failed, EndedUtc = Now.AddDays(-2) },
        ], value => { overview = value; events.Add("overview"); }, _ => events.Add("markers"));

        Assert.Equal(["schedules"], events);
        schedules.SetResult([new Schedule { Id = 23, Name = "Daily tools", NextRunUtc = Now.AddHours(1) }]);
        await libraryRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(refresh.IsCompleted);
        Assert.Equal(["schedules", "clock", "overview", "library"], events);
        Assert.NotNull(overview);
        Assert.Equal(1, overview.SucceededToday);
        Assert.Equal(1, overview.FailedToday);
        Assert.Equal(new HomeLink(17, "Tools", failureDetail), Assert.Single(overview.RecentFailures));
        Assert.Equal(new HomeLink(23, "Daily tools", "schedule time"), Assert.Single(overview.NextSchedules));
        Assert.Null(overview.NoUpcomingText);

        library.SetResult(new LibraryInfo());
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("markers", events.Last());
    }

    [Theory]
    [InlineData("fr", "État de la bibliothèque indisponible.")]
    [InlineData("en", "Library status unavailable.")]
    public async Task LibraryFailure_PreservesOverviewAndPreviousMarkers(string language, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var overviewShown = false;
        var markersChanged = false;
        var page = new HomePage(() => Task.FromResult(new List<Schedule>()),
            () => Task.FromException<LibraryInfo>(new ApiException("Unavailable")), () => Now, _ => "local time");

        var text = await page.RefreshAsync([], _ => overviewShown = true, _ => markersChanged = true);

        Assert.Equal(expected, text);
        Assert.True(overviewShown);
        Assert.False(markersChanged);
    }

    [Fact]
    public async Task ScheduleFailure_PropagatesWithoutChangingThePageOrLoadingLibrary()
    {
        var error = new ApiException("Unavailable");
        var changed = false;
        var page = new HomePage(() => Task.FromException<List<Schedule>>(error),
            () => throw new InvalidOperationException("Library must not load."),
            () => throw new InvalidOperationException("Clock must not be read."), _ => "local time");

        var actual = await Assert.ThrowsAsync<ApiException>(() => page.RefreshAsync([], _ => changed = true, _ => changed = true));

        Assert.Same(error, actual);
        Assert.False(changed);
    }

    [Fact]
    public async Task UnexpectedLibraryFailure_IsNotHidden()
    {
        var error = new InvalidOperationException("Unexpected failure");
        var page = new HomePage(() => Task.FromResult(new List<Schedule>()),
            () => Task.FromException<LibraryInfo>(error), () => Now, _ => "local time");

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => page.RefreshAsync([], _ => { }, _ => { })));
    }

    [Fact]
    public async Task RepeatedRefresh_ReloadsSchedulesAndLibrary()
    {
        var calls = 0;
        var shown = new List<HomeOverview>();
        var page = new HomePage(() => Task.FromResult(new List<Schedule> { new() { Id = ++calls, NextRunUtc = Now } }),
            () => Task.FromResult(new LibraryInfo { Items = [new() { PackageId = calls, UpdateAvailable = calls > 1 }] }),
            () => Now, _ => "local time");

        await page.RefreshAsync([], shown.Add, _ => { });
        var text = await page.RefreshAsync([], shown.Add, _ => { });

        Assert.Equal([1L, 2L], shown.Select(o => Assert.Single(o.NextSchedules).Id));
        Assert.Equal("1 mise à jour disponible sur 1 paquet de la bibliothèque.", text);
    }
}
