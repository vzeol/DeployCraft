using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Library loading, selection, texts and actions without window controls.</summary>
public sealed class LibraryPage(Func<Task<LibraryInfo>> loadStatus, Func<string?, Task<LibraryInfo>> load,
    Func<string, Task> download, Func<Task> refresh, Func<LibraryAutoUpdate, int, Task> setAutoUpdate,
    Func<string, Task> setFolder, Func<DateTime?, string> localTime)
{
    public LibraryList List { get; } = new();
    private string _packages = "";

    public async Task RefreshAsync(string search, Func<IEnumerable<string>> selectedIds, Action changing,
        Action<LibraryOverview> show, Func<Task> refreshPackages)
    {
        search = search.Trim();
        var status = await loadStatus();
        var reload = List.NeedsReload(search, status.LastRefreshUtc);
        var catalog = reload ? await load(search) : null;
        var selected = selectedIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        changing();
        if (catalog != null) List.Load(search, catalog);
        List.Merge(status.Items);
        show(new(status, List.Rows.Where(r => selected.Contains(r.Id)).ToList(), Subtitle(search, status), FolderText(status)));

        var packages = string.Join(";", status.Items.Select(i => $"{i.PackageId}:{i.PackageVersion}"));
        if (packages != _packages)
        {
            if (_packages.Length > 0) await refreshPackages();
            _packages = packages;
        }
    }

    private string Subtitle(string search, LibraryInfo status)
    {
        var refreshed = status.Refreshing ? L.Get("MainWindow_LibraryReading")
            : status.LastRefreshUtc is { } at ? L.Format("MainWindow_LibraryReadOn", localTime(at)) : L.Get("MainWindow_LibraryNotRead");
        var updates = status.Items.Count(i => i.UpdateAvailable);
        var shown = search.Length == 0 ? L.Get("MainWindow_LibraryDefaultList")
            : List.Rows.Count == 0 ? L.Get("MainWindow_LibraryNoResult") : L.Format("MainWindow_LibraryResults", List.Rows.Count);
        return L.Format("MainWindow_LibrarySubtitle", status.CatalogCount, refreshed, shown) +
            (updates > 0 ? " · " + L.Format("MainWindow_LibraryUpdatesAvailable", updates) : "");
    }

    private static string FolderText(LibraryInfo status) =>
        (status.RefreshError is { } error ? error + Environment.NewLine : "") +
        L.Format("MainWindow_LibraryInstallersIn", status.Folder) +
        (status.DefaultFolder ? " " + L.Get("MainWindow_LibraryServerFolderHint") : ".") +
        " " + L.Get("MainWindow_LibraryDownloadCreates");

    public static LibraryDownload DownloadAction(IEnumerable<LibraryRow> selected)
    {
        var ready = selected.Where(r => r.CanDownload).ToList();
        return new(ready, ready.Count > 0 && ready.All(r => r.Item.UpdateAvailable) ? L.Get("MainWindow_Update") : L.Get("MainWindow_Download"));
    }

    public async Task DownloadAsync(IEnumerable<LibraryRow> selected)
    {
        foreach (var row in DownloadAction(selected).Rows) await download(row.Id);
    }

    public Task RefreshCatalogAsync() => refresh();
    public Task<LibraryInfo> FolderAsync() => load(null);
    public Task SetFolderAsync(string folder) => setFolder(folder);
    public static bool ShowAutoUpdate(bool dropDownOpen, bool daysFocused) => !dropDownOpen && !daysFocused;
    public static bool ShowDays(string? mode) => mode == nameof(LibraryAutoUpdate.Delayed);

    public Task SetAutoUpdateAsync(string modeTag, string daysText)
    {
        var mode = Enum.Parse<LibraryAutoUpdate>(modeTag);
        if (!int.TryParse(daysText.Trim(), out var days)) days = mode == LibraryAutoUpdate.Delayed ? -1 : 3;
        return setAutoUpdate(mode, days);
    }
}

public record LibraryOverview(LibraryInfo Status, List<LibraryRow> Selected, string Subtitle, string FolderText)
{
    public bool CanRefresh => !Status.Refreshing;
}

public record LibraryDownload(List<LibraryRow> Rows, string Text)
{
    public bool Enabled => Rows.Count > 0;
}
