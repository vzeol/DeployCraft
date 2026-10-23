using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class LibraryPageTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private sealed class Service
    {
        public LibraryInfo Status { get; set; } = new();
        public LibraryInfo Catalog { get; set; } = new();
        public List<string?> Searches { get; } = [];
        public List<string> Downloads { get; } = [];
        public List<(LibraryAutoUpdate Mode, int Days)> Settings { get; } = [];
        public string? Folder { get; private set; }
        public int Refreshes { get; private set; }
        public int PackageRefreshes { get; private set; }
        public List<string> Selected { get; set; } = [];
        public LibraryOverview? Overview { get; private set; }
        public LibraryPage Page() => new(() => Task.FromResult(Status), search =>
        {
            Searches.Add(search);
            return Task.FromResult(Catalog);
        }, id => { Downloads.Add(id); return Task.CompletedTask; },
            () => { Refreshes++; return Task.CompletedTask; },
            (mode, days) => { Settings.Add((mode, days)); return Task.CompletedTask; },
            folder => { Folder = folder; return Task.CompletedTask; }, _ => "local time");
        public Task RefreshAsync(LibraryPage page, string search = "") => page.RefreshAsync(search, () => Selected, () => { },
            value => Overview = value, () => { PackageRefreshes++; return Task.CompletedTask; });
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Refresh_ReloadsOnlyForSearchOrCatalogChanges_AndPreservesSelectionThroughMerge(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service
        {
            Catalog = new() { Items = [new() { Id = "Tools", LatestVersion = "2" }] },
            Selected = ["TOOLS"],
        };
        var page = service.Page();
        await service.RefreshAsync(page, " tools ");
        var rows = page.List.Rows;
        var original = Assert.Single(rows);
        Assert.Same(original, Assert.Single(service.Overview!.Selected));
        service.Status.Items = [new() { Id = "Tools", LatestVersion = "2", Stage = "Downloading", Progress = 25 }];
        await service.RefreshAsync(page, "tools");
        Assert.Same(rows, page.List.Rows);
        Assert.NotSame(original, Assert.Single(rows));
        Assert.Same(rows[0], Assert.Single(service.Overview!.Selected));
        Assert.False(LibraryPage.DownloadAction(service.Overview.Selected).Enabled);
        service.Status.Items.Clear();
        await service.RefreshAsync(page, "tools");
        Assert.Null(rows[0].Item.Stage);
        Assert.True(rows[0].CanDownload);
        Assert.Equal(["tools"], service.Searches);
        service.Status.LastRefreshUtc = service.Catalog.LastRefreshUtc = DateTime.UtcNow;
        await service.RefreshAsync(page, "tools");
        Assert.NotSame(rows, page.List.Rows);
        await service.RefreshAsync(page, "missing");
        Assert.Equal(["tools", "tools", "missing"], service.Searches);
    }

    [Theory]
    [InlineData("fr", "vos paquets et les logiciels courants en tête", "lecture du catalogue en cours…", "aucun résultat", "1 résultat(s)", "lu le local time", "pas encore lu")]
    [InlineData("en", "your packages and common software first", "reading the catalog…", "no results", "1 result(s)", "read on local time", "not read yet")]
    public async Task Overview_PreservesSubtitleFolderAndRefreshState(string language, string defaultList, string reading,
        string noResults, string results, string readOn, string notRead)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service { Status = new() { CatalogCount = 15, Folder = "Installers", Refreshing = true, DefaultFolder = true, RefreshError = "Unavailable" } };
        var page = service.Page();
        await service.RefreshAsync(page);
        var overview = service.Overview!;
        Assert.Contains(defaultList, overview.Subtitle);
        Assert.Contains(reading, overview.Subtitle);
        Assert.False(overview.CanRefresh);
        Assert.Equal("Unavailable" + Environment.NewLine + L.Format("MainWindow_LibraryInstallersIn", "Installers") + " " +
            L.Get("MainWindow_LibraryServerFolderHint") + " " + L.Get("MainWindow_LibraryDownloadCreates"), overview.FolderText);
        service.Status.Refreshing = false;
        service.Status.DefaultFolder = false;
        service.Status.RefreshError = null;
        await service.RefreshAsync(page, "missing");
        Assert.Contains(noResults, service.Overview!.Subtitle);
        Assert.Contains(notRead, service.Overview.Subtitle);
        Assert.True(service.Overview.CanRefresh);
        Assert.Equal(L.Format("MainWindow_LibraryInstallersIn", "Installers") + ". " + L.Get("MainWindow_LibraryDownloadCreates"), service.Overview.FolderText);
        service.Status.LastRefreshUtc = service.Catalog.LastRefreshUtc = DateTime.UtcNow;
        service.Catalog.Items = [new() { Id = "Tools" }];
        service.Status.Items = [new() { Id = "Tools", PackageId = 7, UpdateAvailable = true }];
        await service.RefreshAsync(page, "tools");
        Assert.Equal(L.Format("MainWindow_LibrarySubtitle", 15, readOn, results) + " · " +
            L.Format("MainWindow_LibraryUpdatesAvailable", 1), service.Overview!.Subtitle);
    }

    [Fact]
    public async Task PackageRefresh_FollowsChangedPackageVersionsAfterInitialLoad()
    {
        var service = new Service { Status = new() { Items = [new() { Id = "Tools", PackageId = 7, PackageVersion = "1" }] } };
        var page = service.Page();
        await service.RefreshAsync(page);
        await service.RefreshAsync(page);
        Assert.Equal(0, service.PackageRefreshes);
        service.Status.Items[0].Progress = 50;
        await service.RefreshAsync(page);
        Assert.Equal(0, service.PackageRefreshes);
        service.Status.Items[0].PackageVersion = "2";
        await service.RefreshAsync(page);
        Assert.Equal(1, service.PackageRefreshes);
        service.Status.Items.Clear();
        await service.RefreshAsync(page);
        Assert.Equal(2, service.PackageRefreshes);
    }

    [Theory]
    [InlineData("fr", "Télécharger", "Mettre à jour")]
    [InlineData("en", "Download", "Update")]
    public async Task Download_UsesOnlyReadyRowsAndChoosesLabelFromDownloadableSelection(string language, string download, string update)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service();
        var page = service.Page();
        LibraryRow fresh = new(new() { Id = "Fresh", LatestVersion = "1" });
        LibraryRow newer = new(new() { Id = "Newer", LatestVersion = "2", PackageId = 7, UpdateAvailable = true });
        List<LibraryRow> blocked = [new(new() { Id = "Busy", LatestVersion = "1", Stage = "Downloading" }),
            new(new() { Id = "Unavailable", LatestVersion = "1", Problem = "Unsupported" }),
            new(new() { Id = "Unknown" }), new(new() { Id = "Current", LatestVersion = "1", PackageId = 8 })];
        Assert.False(LibraryPage.DownloadAction(blocked).Enabled);
        Assert.Equal(download, LibraryPage.DownloadAction([]).Text);
        Assert.Equal(update, LibraryPage.DownloadAction([newer, .. blocked]).Text);
        Assert.Equal(download, LibraryPage.DownloadAction([newer, fresh, .. blocked]).Text);
        await page.DownloadAsync([newer, .. blocked, fresh]);
        Assert.Equal(["Newer", "Fresh"], service.Downloads);
    }

    [Theory]
    [InlineData("Manual", "invalid", 3)]
    [InlineData("Immediate", "invalid", 3)]
    [InlineData("Delayed", "invalid", -1)]
    [InlineData("Delayed", " 7 ", 7)]
    [InlineData("Manual", "0", 0)]
    [InlineData("Delayed", "-2", -2)]
    public async Task AutoUpdate_PreservesParsingAndLeavesValidationToService(string mode, string text, int days)
    {
        var service = new Service();
        await service.Page().SetAutoUpdateAsync(mode, text);
        Assert.Equal((Enum.Parse<LibraryAutoUpdate>(mode), days), Assert.Single(service.Settings));
        Assert.Equal(mode == "Delayed", LibraryPage.ShowDays(mode));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void AutoUpdate_PreservesInputWhileEditing(bool open, bool focused, bool show) =>
        Assert.Equal(show, LibraryPage.ShowAutoUpdate(open, focused));

    [Fact]
    public async Task CatalogAndFolderActions_ForwardToInjectedService()
    {
        var service = new Service();
        var page = service.Page();
        await page.RefreshCatalogAsync();
        Assert.Equal(1, service.Refreshes);
        Assert.Same(service.Catalog, await page.FolderAsync());
        Assert.Null(Assert.Single(service.Searches));
        await page.SetFolderAsync("Installers");
        Assert.Equal("Installers", service.Folder);
    }

    [Fact]
    public async Task Refresh_ReadsSelectionAfterLoading_AndShowsRowsBeforeRefreshingPackages()
    {
        var pending = new TaskCompletionSource<LibraryInfo>();
        var selected = new List<string>();
        var order = new List<string>();
        var status = new LibraryInfo { Items = [new() { Id = "Tools", PackageId = 7, PackageVersion = "1" }] };
        var page = new LibraryPage(() => Task.FromResult(status), _ => pending.Task, _ => Task.CompletedTask,
            () => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => "time");
        var refresh = page.RefreshAsync("", () => selected, () => order.Add("changing"), overview =>
        {
            Assert.Equal("Tools", Assert.Single(overview.Selected).Id);
            order.Add("show");
        }, () => { order.Add("packages"); return Task.CompletedTask; });
        Assert.Empty(order);
        selected.Add("tools");
        pending.SetResult(new() { Items = [new() { Id = "Tools" }] });
        await refresh;
        Assert.Equal(["changing", "show"], order);
        status.Items[0].PackageVersion = "2";
        await page.RefreshAsync("", () => selected, () => order.Add("changing"), _ => order.Add("show"),
            () => { order.Add("packages"); return Task.CompletedTask; });
        Assert.Equal(["changing", "show", "changing", "show", "packages"], order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadFailure_DoesNotMutateListOrRenderPartialData(bool statusFails)
    {
        var service = new Service { Catalog = new() { Items = [new() { Id = "Tools" }] } };
        var error = new ApiException("Unavailable");
        var fail = false;
        var page = new LibraryPage(() => fail && statusFails ? Task.FromException<LibraryInfo>(error) : Task.FromResult(service.Status),
            _ => fail && !statusFails ? Task.FromException<LibraryInfo>(error) : Task.FromResult(service.Catalog),
            _ => Task.CompletedTask, () => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => "time");
        await service.RefreshAsync(page);
        var rows = page.List.Rows;
        fail = true;
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => page.RefreshAsync("new search", () => [],
            () => throw new InvalidOperationException("Must not mutate rows."),
            _ => throw new InvalidOperationException("Must not render."), () => Task.CompletedTask)));
        Assert.Same(rows, page.List.Rows);
        Assert.Equal("Tools", Assert.Single(rows).Id);
    }

    [Fact]
    public async Task PackageRefreshFailure_IsRetriedAfterRenderingOnNextRefresh()
    {
        var service = new Service { Status = new() { Items = [new() { Id = "Tools", PackageId = 7, PackageVersion = "1" }] } };
        var page = service.Page();
        await service.RefreshAsync(page);
        service.Status.Items[0].PackageVersion = "2";
        var error = new ApiException("Unavailable");
        var shown = false;

        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => page.RefreshAsync("", () => [], () => { },
            _ => shown = true, () => Task.FromException(error))));
        Assert.True(shown);
        await service.RefreshAsync(page);
        Assert.Equal(1, service.PackageRefreshes);
        await service.RefreshAsync(page);
        Assert.Equal(1, service.PackageRefreshes);
    }

    [Fact]
    public async Task DownloadFailure_StopsBeforeNextItemAndPropagates()
    {
        var error = new ApiException("Unavailable");
        var calls = new List<string>();
        var page = new LibraryPage(() => Task.FromResult(new LibraryInfo()), _ => Task.FromResult(new LibraryInfo()),
            id => { calls.Add(id); return Task.FromException(error); }, () => Task.CompletedTask,
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => "time");

        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => page.DownloadAsync([
            new(new() { Id = "First", LatestVersion = "1" }), new(new() { Id = "Second", LatestVersion = "1" }),
        ])));
        Assert.Equal(["First"], calls);
    }
}
