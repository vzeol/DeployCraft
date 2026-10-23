using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Clipboard and move operations for the package tree, without controls or dialogs.</summary>
public sealed class PackageTreeCommands(Func<long, Task<Package>> loadPackage, Func<Package, Task<Package>> savePackage,
    Func<long, long?, Task> movePackage, Func<PackageFolder, Task<PackageFolder>> saveFolder)
{
    private (long Id, bool IsFolder, bool Cut)? _clipboard;

    public void CutOrCopy(object? selected, bool cut)
    {
        if (selected is Package p) _clipboard = (p.Id, false, cut);
        else if (cut && selected is PackageFolder f) _clipboard = (f.Id, true, true);
    }

    public bool CanPaste(bool connected, object? selected, bool selectedIsRoot) =>
        _clipboard != null && connected && (selectedIsRoot || selected is Package or PackageFolder);

    public async Task<PackageTreeSelection?> PasteAsync(PackageList list, long? target)
    {
        if (_clipboard is not { } item) return null;
        PackageTreeSelection selection;
        if (item.IsFolder)
        {
            if (list.Folders.FirstOrDefault(f => f.Id == item.Id) is not { } folder || folder.ParentId == target) return null;
            await saveFolder(new PackageFolder { Id = folder.Id, Name = folder.Name, ParentId = target });
            _clipboard = null;
            selection = new(null, folder.Id);
        }
        else if (item.Cut)
        {
            await movePackage(item.Id, target);
            _clipboard = null;
            selection = new(item.Id, null);
        }
        else
        {
            var copy = await loadPackage(item.Id);
            copy.Id = 0;
            copy.FolderId = target;
            copy.Name = PackageFolder.CopyName(copy.Name, list.Packages.Where(p => p.FolderId == target).Select(p => p.Name));
            var saved = await savePackage(copy);
            selection = new(saved.Id, null);
        }
        if (target is { } open) list.ExpandedFolders.Add(open);
        return selection;
    }

    public async Task<PackageTreeSelection?> MoveAsync(PackageList list, object item, long? target)
    {
        if (target is { } open) list.ExpandedFolders.Add(open);
        switch (item)
        {
            case Package p:
                await movePackage(p.Id, target);
                return new(p.Id, null);
            case PackageFolder f:
                await saveFolder(new PackageFolder { Id = f.Id, Name = f.Name, ParentId = target });
                return new(null, f.Id);
            default:
                return null;
        }
    }
}

public record PackageTreeSelection(long? PackageId, long? FolderId);
