using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Library view: catalog search, downloads and automatic updates.</summary>
public partial class MainWindow
{
    // ---------- Package library ----------

    private bool _refreshingLibrary;
    // The search starts a short moment after the last keystroke, not on every letter.
    private readonly DispatcherTimer _librarySearchDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly LibraryPage _libraryPage = new(Api.LibraryStatus, Api.Library, Api.DownloadFromLibrary,
        Api.RefreshLibrary, Api.SetLibraryAutoUpdate, Api.SetLibraryFolder, Ui.LocalTime);

    private List<LibraryRow> SelectedLibraryRows => LibraryGrid.SelectedItems.OfType<LibraryRow>().ToList();

    private async Task RefreshLibraryAsync()
    {
        try
        {
            await _libraryPage.RefreshAsync(LibrarySearch.Text, () => SelectedLibraryRows.Select(r => r.Id),
                () => _refreshingLibrary = true, overview =>
                {
                    LibraryGrid.ItemsSource = _libraryPage.List.Rows;
                    foreach (var row in overview.Selected)
                        if (!LibraryGrid.SelectedItems.Contains(row)) LibraryGrid.SelectedItems.Add(row);
                    _refreshingLibrary = false;
                    LibrarySubtitle.Text = overview.Subtitle;
                    LibraryFolderText.Text = overview.FolderText;
                    LibraryRefreshButton.IsEnabled = overview.CanRefresh;
                    UpdateLibraryButton();
                    ShowLibraryUpdates(overview.Status);
                    ShowAutoUpdate(overview.Status);
                }, RefreshPackagesAsync);
        }
        finally { _refreshingLibrary = false; }
    }

    private void UpdateLibraryButton()
    {
        var action = LibraryPage.DownloadAction(SelectedLibraryRows);
        LibraryDownloadButton.IsEnabled = action.Enabled;
        LibraryDownloadText.Text = action.Text;
    }

    private void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _librarySearchDelay.Stop();
        _librarySearchDelay.Start();
    }

    private void LibraryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingLibrary) UpdateLibraryButton();
    }

    private async void LibraryDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _libraryPage.DownloadAsync(SelectedLibraryRows);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void LibraryRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _libraryPage.RefreshCatalogAsync();
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private bool _showingAutoUpdate;

    /// <summary>Automatic updates setting, without overwriting an input in progress.</summary>
    private void ShowAutoUpdate(LibraryInfo status)
    {
        if (!LibraryPage.ShowAutoUpdate(LibraryAutoUpdateBox.IsDropDownOpen, LibraryAutoUpdateDays.IsKeyboardFocusWithin)) return;
        _showingAutoUpdate = true;
        LibraryAutoUpdateBox.SelectedItem = LibraryAutoUpdateBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == status.AutoUpdate.ToString());
        LibraryAutoUpdateDays.Text = status.AutoUpdateDays.ToString();
        _showingAutoUpdate = false;
        ShowAutoUpdateDays();
    }

    private void ShowAutoUpdateDays()
    {
        var delayed = LibraryPage.ShowDays((LibraryAutoUpdateBox.SelectedItem as ComboBoxItem)?.Tag as string);
        LibraryAutoUpdateDays.Visibility = LibraryAutoUpdateDaysText.Visibility = delayed ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LibraryAutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_showingAutoUpdate || LibraryAutoUpdateBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        ShowAutoUpdateDays();
        try
        {
            await _libraryPage.SetAutoUpdateAsync(tag, LibraryAutoUpdateDays.Text);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
        await RefreshAsync(packages: false);
    }

    private async void LibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = await _libraryPage.FolderAsync();
            if (Ui.PickLibraryFolder(this, info.Folder) is not { } folder) return;
            await _libraryPage.SetFolderAsync(folder);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
