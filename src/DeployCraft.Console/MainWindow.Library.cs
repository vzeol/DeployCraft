using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Library view: catalog search, downloads and automatic updates.</summary>
public partial class MainWindow
{
    // ---------- Bibliothèque de paquets ----------

    private string _libraryPackages = "";
    private bool _refreshingLibrary;
    // La recherche part un court instant après la dernière touche, pas à chaque lettre.
    private readonly DispatcherTimer _librarySearchDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly LibraryList _library = new();

    private List<LibraryRow> SelectedLibraryRows => LibraryGrid.SelectedItems.OfType<LibraryRow>().ToList();

    private async Task RefreshLibraryAsync()
    {
        var search = LibrarySearch.Text.Trim();
        var status = await Api.LibraryStatus();
        // Liste rechargée seulement si la recherche ou le catalogue ont changé.
        if (_library.NeedsReload(search, status.LastRefreshUtc))
        {
            var list = await Api.Library(search);
            var selected = SelectedLibraryRows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _library.Load(search, list);
            _refreshingLibrary = true;
            LibraryGrid.ItemsSource = _library.Rows;
            foreach (var row in _library.Rows.Where(r => selected.Contains(r.Id))) LibraryGrid.SelectedItems.Add(row);
            _refreshingLibrary = false;
        }
        MergeLibraryStatus(status.Items);

        var refreshed = status.Refreshing ? "lecture du catalogue en cours…"
            : status.LastRefreshUtc is { } at ? $"lu le {Ui.LocalTime(at)}" : "pas encore lu";
        var updates = status.Items.Count(i => i.UpdateAvailable);
        var shown = search.Length == 0 ? "vos paquets et les logiciels courants en tête"
            : _library.Rows.Count == 0 ? "aucun résultat" : $"{_library.Rows.Count:N0} résultat(s)";
        LibrarySubtitle.Text = $"Catalogue public winget ({status.CatalogCount:N0} logiciels, {refreshed}) · {shown}" +
                               (updates > 0 ? $" · {updates} mise(s) à jour disponible(s)" : "");
        LibraryFolderText.Text = (status.RefreshError is { } error ? error + Environment.NewLine : "") +
            $"Les installeurs sont rangés dans {status.Folder}" +
            (status.DefaultFolder ? " (dossier du serveur : préférez un partage, lisible aussi par les postes en mode pull)." : ".") +
            " Chaque téléchargement crée ou met à jour un paquet du même nom, prêt à déployer.";
        LibraryRefreshButton.IsEnabled = !status.Refreshing;
        UpdateLibraryButton();
        ShowLibraryUpdates(status);
        ShowAutoUpdate(status);

        // Un téléchargement terminé crée ou met à jour un paquet : l'arbre suit.
        var packages = string.Join(";", status.Items.Select(i => $"{i.PackageId}:{i.PackageVersion}"));
        if (packages != _libraryPackages)
        {
            if (_libraryPackages.Length > 0) await RefreshPackagesAsync();
            _libraryPackages = packages;
        }
    }

    /// <summary>Lignes dont l'état a changé remplacées, la sélection suit.</summary>
    private void MergeLibraryStatus(List<LibraryItem> items)
    {
        _refreshingLibrary = true;
        _library.Merge(items, (old, row) =>
        {
            if (LibraryGrid.SelectedItems.Contains(old)) LibraryGrid.SelectedItems.Add(row);
        });
        _refreshingLibrary = false;
    }

    private void UpdateLibraryButton()
    {
        var ready = SelectedLibraryRows.Where(r => r.CanDownload).ToList();
        LibraryDownloadButton.IsEnabled = ready.Count > 0;
        LibraryDownloadText.Text = ready.Count > 0 && ready.All(r => r.Item.UpdateAvailable) ? "Mettre à jour" : "Télécharger";
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
            foreach (var row in SelectedLibraryRows.Where(r => r.CanDownload)) await Api.DownloadFromLibrary(row.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void LibraryRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Api.RefreshLibrary();
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private bool _showingAutoUpdate;

    /// <summary>Réglage des mises à jour automatiques, sans écraser une saisie en cours.</summary>
    private void ShowAutoUpdate(LibraryInfo status)
    {
        if (LibraryAutoUpdateBox.IsDropDownOpen || LibraryAutoUpdateDays.IsKeyboardFocusWithin) return;
        _showingAutoUpdate = true;
        LibraryAutoUpdateBox.SelectedItem = LibraryAutoUpdateBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == status.AutoUpdate.ToString());
        LibraryAutoUpdateDays.Text = status.AutoUpdateDays.ToString();
        _showingAutoUpdate = false;
        ShowAutoUpdateDays();
    }

    private void ShowAutoUpdateDays()
    {
        var delayed = (LibraryAutoUpdateBox.SelectedItem as ComboBoxItem)?.Tag as string == nameof(LibraryAutoUpdate.Delayed);
        LibraryAutoUpdateDays.Visibility = LibraryAutoUpdateDaysText.Visibility = delayed ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LibraryAutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_showingAutoUpdate || LibraryAutoUpdateBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        ShowAutoUpdateDays();
        var mode = Enum.Parse<LibraryAutoUpdate>(tag);
        // Le délai n'est vérifié que s'il sert ; sinon une saisie illisible garde la valeur d'usine.
        if (!int.TryParse(LibraryAutoUpdateDays.Text.Trim(), out var days)) days = mode == LibraryAutoUpdate.Delayed ? -1 : 3;
        try
        {
            await Api.SetLibraryAutoUpdate(mode, days);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
        await RefreshAsync(packages: false);
    }

    private async void LibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = await Api.Library();
            if (Ui.PickLibraryFolder(this, info.Folder) is not { } folder) return;
            await Api.SetLibraryFolder(folder);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
