using System.Collections.ObjectModel;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Rows of the package library. The list is loaded once (whole catalog or a search); afterwards only the rows whose
/// state changes are replaced, so the grid keeps its scroll position and selection.
/// </summary>
public sealed class LibraryList
{
    private Dictionary<string, int> _positions = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<LibraryRow> Rows { get; private set; } = [];
    public string? Search { get; private set; }
    public DateTime? CatalogDate { get; private set; }

    /// <summary>The list must be reloaded when the search or the catalog changed.</summary>
    public bool NeedsReload(string search, DateTime? catalogDate) => search != Search || catalogDate != CatalogDate;

    public void Load(string search, LibraryInfo list)
    {
        Search = search;
        CatalogDate = list.LastRefreshUtc;
        Rows = new(list.Items.Select(i => new LibraryRow(i)));
        _positions = Rows.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.OrdinalIgnoreCase);
        _active = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the rows whose state changed: download in progress, error, package created. A row that no longer has a
    /// state of its own (package deleted, error cleared) goes back to "not downloaded yet". Calls
    /// <paramref name="replaced"/> with the old and new row of each replacement.
    /// </summary>
    public void Merge(IEnumerable<LibraryItem> items, Action<LibraryRow, LibraryRow>? replaced = null)
    {
        var active = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _active.Union(active.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!_positions.TryGetValue(id, out var position)) continue;
            var current = Rows[position];
            var item = active.GetValueOrDefault(id) ?? new LibraryItem
            {
                Id = current.Item.Id,
                Name = current.Item.Name,
                LatestVersion = current.Item.LatestVersion,
                Installer = current.Item.Installer,
                Problem = current.Item.Problem,
            };
            if (SameState(current.Item, item)) continue;
            Rows[position] = new LibraryRow(item);
            replaced?.Invoke(current, Rows[position]);
        }
        _active = active.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameState(LibraryItem a, LibraryItem b) =>
        a.Stage == b.Stage && a.Progress == b.Progress && a.Error == b.Error && a.PackageId == b.PackageId &&
        a.PackageVersion == b.PackageVersion && a.UpdateAvailable == b.UpdateAvailable && a.Installer == b.Installer &&
        a.Problem == b.Problem && a.LatestVersion == b.LatestVersion;

    /// <summary>Packages for which the library has a newer version: marked in the tree.</summary>
    public static HashSet<long> PackagesWithUpdate(LibraryInfo status) =>
        status.Items.Where(i => i.UpdateAvailable && i.PackageId != null).Select(i => i.PackageId!.Value).ToHashSet();
}
