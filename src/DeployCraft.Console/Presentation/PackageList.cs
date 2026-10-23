using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Package loading and tree decisions, independent of window controls.</summary>
public sealed class PackageList(Func<Task<List<PackageFolder>>> loadFolders, Func<Task<List<Package>>> loadPackages,
    Func<Task<DeploymentDefaults>> loadDefaults, Func<Task<LibraryInfo>> loadLibrary)
{
    public List<PackageFolder> Folders { get; private set; } = [];
    public List<Package> Packages { get; private set; } = [];
    public DeploymentDefaults? Defaults { get; private set; }
    public HashSet<long> ExpandedFolders { get; } = [];
    private HashSet<long> _updates = [];

    public async Task RefreshAsync()
    {
        await RefreshFoldersAsync();
        try { Defaults = await loadDefaults(); }
        catch (ApiException) { /* Keep the previous defaults, or the factory values on the first load. */ }
        try { UpdateLibrary(await loadLibrary()); }
        catch (ApiException) { /* Keep the previous markers until the next successful load. */ }
    }

    public async Task RefreshFoldersAsync()
    {
        Folders = await loadFolders();
        Packages = await loadPackages();
    }

    public async Task<Package?> AfterSavedAsync(Package saved)
    {
        Packages = await loadPackages();
        if (saved.FolderId is { } folder) ExpandedFolders.Add(folder);
        return Find(saved.Id);
    }

    public Package? Find(long id) => Packages.FirstOrDefault(p => p.Id == id);

    public bool UpdateLibrary(LibraryInfo status)
    {
        var updates = LibraryList.PackagesWithUpdate(status);
        if (updates.SetEquals(_updates)) return false;
        _updates = updates;
        return true;
    }

    public List<PackageTreeNode> Tree(string filter, long? selectId = null, long? selectFolderId = null)
    {
        filter = filter.Trim();
        var shown = Packages.Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var known = Folders.Select(f => f.Id).ToHashSet();
        return Children(null);

        List<PackageTreeNode> Children(long? folderId)
        {
            var nodes = new List<PackageTreeNode>();
            foreach (var folder in Folders.Where(f => f.ParentId == folderId))
            {
                var children = Children(folder.Id);
                if (filter.Length > 0 && children.Count == 0) continue;
                nodes.Add(new(folder, children, filter.Length > 0 || ExpandedFolders.Contains(folder.Id),
                    folder.Id == selectFolderId, false));
            }
            // Packages with a missing folder remain visible at the root.
            foreach (var p in shown.Where(p => p.FolderId == folderId || folderId == null && p.FolderId is { } f && !known.Contains(f)))
                nodes.Add(new(p, [], false, p.Id == selectId, _updates.Contains(p.Id)));
            return nodes;
        }
    }

    public static long? CurrentFolder(object? selected) => selected switch
    {
        PackageFolder f => f.Id,
        Package p => p.FolderId,
        _ => null,
    };

    public bool DropFolder(object? dragged, object? target, bool targetIsRoot, out long? folderId)
    {
        folderId = CurrentFolder(target);
        if (!targetIsRoot && target is not (Package or PackageFolder)) return false;
        return dragged switch
        {
            Package p => p.FolderId != folderId,
            PackageFolder f => f.ParentId != folderId && !PackageFolder.IsWithin(Folders, folderId, f.Id),
            _ => false,
        };
    }
}

public record PackageTreeNode(object Item, List<PackageTreeNode> Children, bool Expanded, bool Selected, bool UpdateAvailable);
