using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Package tree: folders, subfolders, and drag and drop to store packages and folders in them.</summary>
public partial class MainWindow
{
    private Point _dragStart;
    private TreeViewItem? _dragNode;

    private PackageFolder? SelectedFolder => (Nav.SelectedItem as TreeViewItem)?.Tag as PackageFolder;

    /// <summary>Folder where to put a new package or folder: the selected folder, or the one of the selected package.</summary>
    private long? CurrentFolderId => PackageList.CurrentFolder((Nav.SelectedItem as TreeViewItem)?.Tag);

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
        _packagesNode.Items.Clear();
        foreach (var row in _packageList.Tree(Filter.Text, selectId, selectFolderId))
            RenderPackageNode(_packagesNode, row);
    }

    private void RenderPackageNode(TreeViewItem parent, PackageTreeNode row)
    {
        var folder = row.Item as PackageFolder;
        var header = Ui.Header(folder != null ? Ui.IconFolder : Ui.IconPackage,
            folder?.Name ?? ((Package)row.Item).Name);
        if (row.UpdateAvailable)
            header.Children.Add(new TextBlock
            {
                Text = Ui.IconUpdate,
                FontFamily = Ui.IconFont,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Accent"),
                ToolTip = L.Get("MainWindow_UpdateInLibraryTip"),
            });
        var node = new TreeViewItem { Header = header, Tag = row.Item };
        foreach (var child in row.Children) RenderPackageNode(node, child);
        node.IsExpanded = row.Expanded;
        if (folder != null)
        {
            node.Expanded += (_, e) => { if (e.OriginalSource == node) _packageList.ExpandedFolders.Add(folder.Id); };
            node.Collapsed += (_, e) => { if (e.OriginalSource == node) _packageList.ExpandedFolders.Remove(folder.Id); };
        }
        parent.Items.Add(node);
        node.IsSelected = row.Selected;
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

    // ---------- Commands ----------

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var parentId = CurrentFolderId;
        if (InputDialog.Ask(this, L.Get("MainWindow_NewFolder"), L.Get("MainWindow_FolderNamePrompt"), L.Get("MainWindow_NewFolder")) is not { } name) return;
        try
        {
            var folder = await Api.SaveFolder(new PackageFolder { Name = name, ParentId = parentId });
            if (parentId is { } p) _packageList.ExpandedFolders.Add(p);
            await RefreshFoldersAsync(selectFolderId: folder.Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task RenameFolderAsync(PackageFolder folder)
    {
        if (InputDialog.Ask(this, L.Get("MainWindow_Rename"), L.Get("MainWindow_RenameFolderPrompt"), folder.Name) is not { } name || name == folder.Name) return;
        try
        {
            await Api.SaveFolder(new PackageFolder { Id = folder.Id, Name = name, ParentId = folder.ParentId });
            await RefreshFoldersAsync(selectFolderId: folder.Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task DeleteFolderAsync(PackageFolder folder)
    {
        if (MessageDialog.Show(this, L.Format("MainWindow_DeleteFolderConfirm", folder.Name), L.Get("MainWindow_Delete"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteFolder(folder.Id);
            _packageList.ExpandedFolders.Remove(folder.Id);
            await RefreshFoldersAsync(selectFolderId: folder.ParentId);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task RefreshFoldersAsync(long? selectId = null, long? selectFolderId = null)
    {
        await _packageList.RefreshFoldersAsync();
        RebuildPackageNodes(selectId, selectFolderId);
        if (selectId == null && selectFolderId == null) _packagesNode.IsSelected = true;
    }

    // ---------- Cut / copy / paste (Ctrl+X / C / V in the tree) ----------

    private readonly PackageTreeCommands _packageTreeCommands = new(Api.Package, Api.SavePackage, Api.MovePackage, Api.SaveFolder);

    private void CutOrCopyNode(bool cut) => _packageTreeCommands.CutOrCopy((Nav.SelectedItem as TreeViewItem)?.Tag, cut);

    private bool CanPaste => _packageTreeCommands.CanPaste(_connected,
        (Nav.SelectedItem as TreeViewItem)?.Tag, Nav.SelectedItem == _packagesNode);

    private async Task PasteNodeAsync()
    {
        if (!CanPaste) return;
        try
        {
            if (await _packageTreeCommands.PasteAsync(_packageList, CurrentFolderId) is { } selection)
                await RefreshFoldersAsync(selection.PackageId, selection.FolderId);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Drag and drop ----------

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

    /// <summary>Folder where the item dropped on <paramref name="target"/> lands (root = null), or false if impossible.</summary>
    private bool DropFolder(TreeViewItem? dragged, TreeViewItem? target, out long? folderId)
    {
        return _packageList.DropFolder(dragged?.Tag, target?.Tag, target == _packagesNode, out folderId);
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
            if (await _packageTreeCommands.MoveAsync(_packageList, dragged!.Tag, folderId) is { } selection)
                await RefreshFoldersAsync(selection.PackageId, selection.FolderId);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
