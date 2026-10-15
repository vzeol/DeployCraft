using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Arbre des paquets : dossiers, sous-dossiers, et glisser-déposer pour y ranger paquets et dossiers.</summary>
public partial class MainWindow
{
    private List<PackageFolder> _folders = [];
    /// <summary>Dossiers ouverts, gardés d'un rafraîchissement à l'autre.</summary>
    private readonly HashSet<long> _expandedFolders = [];
    private Point _dragStart;
    private TreeViewItem? _dragNode;

    private PackageFolder? SelectedFolder => (Nav.SelectedItem as TreeViewItem)?.Tag as PackageFolder;

    /// <summary>Dossier où ranger un nouveau paquet ou dossier : le dossier sélectionné, ou celui du paquet sélectionné.</summary>
    private long? CurrentFolderId => SelectedFolder?.Id ?? SelectedPackage?.FolderId;

    private void SetupFolderDragDrop()
    {
        Nav.AllowDrop = true;
        Nav.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(Nav);
            _dragNode = NodeAt(e.OriginalSource);
        };
        Nav.PreviewMouseMove += Nav_PreviewMouseMove;
        Nav.DragOver += Nav_DragOver;
        Nav.Drop += Nav_Drop;
    }

    private void RebuildPackageNodes(long? selectId, long? selectFolderId = null)
    {
        var filter = Filter.Text.Trim();
        var shown = _packages.Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var known = _folders.Select(f => f.Id).ToHashSet();
        _packagesNode.Items.Clear();
        AddChildren(_packagesNode, null);

        // Renvoie vrai si le dossier contient quelque chose à afficher (tout, sans filtre).
        bool AddChildren(TreeViewItem parent, long? folderId)
        {
            var any = false;
            foreach (var folder in _folders.Where(f => f.ParentId == folderId))
            {
                var node = new TreeViewItem { Header = Ui.Header(Ui.IconFolder, folder.Name), Tag = folder };
                var hasContent = AddChildren(node, folder.Id);
                if (filter.Length > 0 && !hasContent) continue;
                node.IsExpanded = filter.Length > 0 || _expandedFolders.Contains(folder.Id);
                node.Expanded += (_, e) => { if (e.OriginalSource == node) _expandedFolders.Add(folder.Id); };
                node.Collapsed += (_, e) => { if (e.OriginalSource == node) _expandedFolders.Remove(folder.Id); };
                parent.Items.Add(node);
                if (folder.Id == selectFolderId) node.IsSelected = true;
                any = true;
            }
            // Un paquet dont le dossier n'existe plus est affiché à la racine.
            foreach (var p in shown.Where(p => p.FolderId == folderId || folderId == null && p.FolderId is { } f && !known.Contains(f)))
            {
                var node = new TreeViewItem { Header = Ui.Header(Ui.IconPackage, p.Name), Tag = p };
                parent.Items.Add(node);
                if (p.Id == selectId) node.IsSelected = true;
                any = true;
            }
            return any;
        }
    }

    private static IEnumerable<TreeViewItem> Descendants(TreeViewItem node)
    {
        foreach (TreeViewItem child in node.Items)
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    private void SelectPackageNode(long packageId)
    {
        if (Descendants(_packagesNode).FirstOrDefault(n => n.Tag is Package p && p.Id == packageId) is not { } node) return;
        for (var parent = node.Parent as TreeViewItem; parent != null; parent = parent.Parent as TreeViewItem) parent.IsExpanded = true;
        node.IsSelected = true;
        node.BringIntoView();
    }

    // ---------- Commandes ----------

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var parentId = CurrentFolderId;
        if (InputDialog.Ask(this, "Nouveau dossier", "Nom du dossier :", "Nouveau dossier") is not { } name) return;
        try
        {
            var folder = await Api.SaveFolder(new PackageFolder { Name = name, ParentId = parentId });
            if (parentId is { } p) _expandedFolders.Add(p);
            await RefreshFoldersAsync(selectFolderId: folder.Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task RenameFolderAsync(PackageFolder folder)
    {
        if (InputDialog.Ask(this, "Renommer", "Nouveau nom du dossier :", folder.Name) is not { } name || name == folder.Name) return;
        try
        {
            await Api.SaveFolder(new PackageFolder { Id = folder.Id, Name = name, ParentId = folder.ParentId });
            await RefreshFoldersAsync(selectFolderId: folder.Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task DeleteFolderAsync(PackageFolder folder)
    {
        if (MessageDialog.Show(this, $"Supprimer le dossier « {folder.Name} » ?", "Supprimer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteFolder(folder.Id);
            _expandedFolders.Remove(folder.Id);
            await RefreshFoldersAsync(selectFolderId: folder.ParentId);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task RefreshFoldersAsync(long? selectId = null, long? selectFolderId = null)
    {
        _folders = await Api.Folders();
        _packages = await Api.Packages();
        RebuildPackageNodes(selectId, selectFolderId);
        if (selectId == null && selectFolderId == null) _packagesNode.IsSelected = true;
    }

    // ---------- Couper / copier / coller (Ctrl+X / C / V dans l'arbre) ----------

    /// <summary>Élément coupé ou copié dans l'arbre : un paquet, ou un dossier (seulement coupé).</summary>
    private (long Id, bool IsFolder, bool Cut)? _treeClipboard;

    private void CutOrCopyNode(bool cut)
    {
        if (SelectedPackage is { } p) _treeClipboard = (p.Id, false, cut);
        else if (cut && SelectedFolder is { } f) _treeClipboard = (f.Id, true, true);
    }

    private bool CanPaste => _treeClipboard != null && _connected
        && (Nav.SelectedItem == _packagesNode || SelectedFolder != null || SelectedPackage != null);

    /// <summary>Colle dans le dossier sélectionné (ou celui du paquet sélectionné, ou la racine).</summary>
    private async Task PasteNodeAsync()
    {
        if (!CanPaste || _treeClipboard is not { } item) return;
        var target = CurrentFolderId;
        try
        {
            if (item.IsFolder)
            {
                if (_folders.FirstOrDefault(f => f.Id == item.Id) is not { } folder || folder.ParentId == target) return;
                await Api.SaveFolder(new PackageFolder { Id = folder.Id, Name = folder.Name, ParentId = target });
                _treeClipboard = null;
                if (target is { } open) _expandedFolders.Add(open);
                await RefreshFoldersAsync(selectFolderId: folder.Id);
            }
            else if (item.Cut)
            {
                await Api.MovePackage(item.Id, target);
                _treeClipboard = null;
                if (target is { } open) _expandedFolders.Add(open);
                await RefreshFoldersAsync(selectId: item.Id);
            }
            else
            {
                var copy = await Api.Package(item.Id);
                copy.Id = 0;
                copy.FolderId = target;
                copy.Name = PackageFolder.CopyName(copy.Name, _packages.Where(p => p.FolderId == target).Select(p => p.Name));
                var saved = await Api.SavePackage(copy);
                if (target is { } open) _expandedFolders.Add(open);
                await RefreshFoldersAsync(selectId: saved.Id);
            }
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Glisser-déposer ----------

    private static TreeViewItem? NodeAt(object source)
    {
        for (var element = source as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
            if (element is TreeViewItem item) return item;
        return null;
    }

    private void Nav_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode?.Tag is not (Package or PackageFolder) || !_connected) return;
        var delta = e.GetPosition(Nav) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var node = _dragNode;
        _dragNode = null;
        DragDrop.DoDragDrop(Nav, new DataObject(typeof(TreeViewItem), node), DragDropEffects.Move);
    }

    /// <summary>Dossier où atterrit l'élément déposé sur <paramref name="target"/> (racine = null), ou faux si impossible.</summary>
    private bool DropFolder(TreeViewItem? dragged, TreeViewItem? target, out long? folderId)
    {
        folderId = null;
        if (dragged == null || target == null) return false;
        if (target == _packagesNode) folderId = null;
        else if (target.Tag is PackageFolder f) folderId = f.Id;
        else if (target.Tag is Package p) folderId = p.FolderId;
        else return false;

        return dragged.Tag switch
        {
            Package p => p.FolderId != folderId,
            PackageFolder f => f.ParentId != folderId && !PackageFolder.IsWithin(_folders, folderId, f.Id),
            _ => false,
        };
    }

    private void Nav_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(TreeViewItem)) as TreeViewItem;
        e.Effects = DropFolder(dragged, NodeAt(e.OriginalSource), out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Nav_Drop(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(TreeViewItem)) as TreeViewItem;
        if (!DropFolder(dragged, NodeAt(e.OriginalSource), out var folderId)) return;
        try
        {
            if (folderId is { } open) _expandedFolders.Add(open);
            switch (dragged!.Tag)
            {
                case Package p:
                    await Api.MovePackage(p.Id, folderId);
                    await RefreshFoldersAsync(selectId: p.Id);
                    break;
                case PackageFolder f:
                    await Api.SaveFolder(new PackageFolder { Id = f.Id, Name = f.Name, ParentId = folderId });
                    await RefreshFoldersAsync(selectFolderId: f.Id);
                    break;
            }
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
