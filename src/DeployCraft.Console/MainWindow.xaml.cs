using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public partial class MainWindow : Window
{
    private enum View { Home, Deployments, Package, Schedules, Library }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TreeViewItem _homeNode;
    private readonly TreeViewItem _deploymentsNode;
    private readonly TreeViewItem _schedulesNode;
    private readonly TreeViewItem _libraryNode;
    private readonly TreeViewItem _packagesNode;
    private List<Package> _packages = [];
    private View _view = View.Home;
    // Faux tant que le service refuse l'accès ou ne répond pas : les commandes sont alors grisées.
    private bool _connected;
    private readonly RefreshQueue _refresh;

    public MainWindow()
    {
        InitializeComponent();
        _refresh = new RefreshQueue(RefreshOnceAsync);

        _homeNode = new TreeViewItem { Header = Ui.Header(Ui.IconHome, "Accueil"), Tag = "home" };
        _deploymentsNode = new TreeViewItem { Header = Ui.Header(Ui.IconHistory, "Tous les déploiements"), Tag = "deployments" };
        _schedulesNode = new TreeViewItem { Header = Ui.Header(Ui.IconSchedule, "Toutes les planifications"), Tag = "schedules" };
        _libraryNode = new TreeViewItem { Header = Ui.Header(Ui.IconLibrary, "Bibliothèque de paquets"), Tag = "library" };
        _packagesNode = new TreeViewItem { Header = Ui.Header(Ui.IconFolder, "Paquets"), IsExpanded = true };
        Nav.Items.Add(_homeNode);
        Nav.Items.Add(_deploymentsNode);
        Nav.Items.Add(_schedulesNode);
        Nav.Items.Add(_libraryNode);
        Nav.Items.Add(_packagesNode);
        SetupFolderDragDrop();

        foreach (var panel in new[] { AllDeployments, PackageDeployments, ScheduleDeployments })
        {
            panel.DeployRequested += (id, targets) => Deploy(id, targets);
            panel.ScheduleRequested += OpenSchedule;
            panel.Changed += () => _ = RefreshAsync(packages: false);
        }

        _librarySearchDelay.Tick += async (_, _) =>
        {
            _librarySearchDelay.Stop();
            await RefreshAsync(packages: false);
        };

        PreviewKeyDown += OnShortcut;
        SetupContextMenus();

        _timer.Tick += async (_, _) => await RefreshAsync(packages: false);
        // Compte à rebours des planifications : chaque seconde, sans recharger les données.
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) =>
        {
            foreach (var grid in new[] { SchedulesGrid, PackageSchedulesGrid })
                if (grid.IsVisible && grid.ItemsSource is IEnumerable<ScheduleRow> rows)
                    foreach (var row in rows) row.Tick();
        };
        countdown.Start();
        if (ConsolePrefs.Get<double>(NavWidthPref) is var width and >= 180 and <= 800) NavColumn.Width = new GridLength(width);
        Loaded += async (_, _) =>
        {
            _homeNode.IsSelected = true;
            await RefreshAsync(packages: true);
            _timer.Start();
            if (await WelcomeWindow.NeededAsync()) Welcome_Click(this, new());
            var update = await UpdateCheck.FindAsync();
            ShowUpdate(update);
            if (update != null) await ProposeUpdateAsync(update);
        };
    }

    private Package? SelectedPackage => (Nav.SelectedItem as TreeViewItem)?.Tag as Package;

    // Largeur de l'arbre choisie à la souris, retrouvée à la prochaine ouverture.
    private const string NavWidthPref = "NavWidth";
    private const double NavDefaultWidth = 250;

    private void NavSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        ConsolePrefs.Set(NavWidthPref, NavColumn.ActualWidth);

    private void NavSplitter_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavColumn.Width = new GridLength(NavDefaultWidth);
        ConsolePrefs.Set(NavWidthPref, NavDefaultWidth);
    }

    /// <summary>Panneau des déploiements visible (celui sur lequel agit le menu Édition).</summary>
    private DeploymentsPanel CurrentPanel => _view switch
    {
        View.Package => PackageDeployments,
        View.Schedules => ScheduleDeployments,
        _ => AllDeployments,
    };

    private Schedule? SelectedSchedule => (SchedulesGrid.SelectedItem as ScheduleRow)?.Schedule;

    /// <summary>Les commandes du menu Édition visent l'arbre (paquet) ou le panneau des déploiements selon le focus.</summary>
    private bool TreeHasFocus => Nav.IsKeyboardFocusWithin;

    // ---------- Rafraîchissement ----------

    private Task RefreshAsync(bool packages) => _refresh.RunAsync(packages);

    private async Task RefreshOnceAsync(bool packages)
    {
        try
        {
            await RefreshStatusAsync();
            if (packages) await RefreshPackagesAsync();
            await RefreshViewAsync();
        }
        catch (ApiException) { /* déjà signalé dans le bandeau */ }
        finally
        {
            UpdateButtons();
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var s = await Api.Status();
            _connected = true;
            _serverMachine = s.Machine;
            StatusServer.Text = $"Service : {s.Machine} ({s.RunningAs}) — vous : {s.ConnectedAs}";
            StatusAccount.Text = s.DefaultCredential is { } c ? $"Identifiant par défaut : {c.Account}" : "Aucun identifiant";

            ShowBanner(ServerIssues.For(s));
        }
        catch (ApiException ex)
        {
            _connected = false;
            StatusServer.Text = ex.AccessDenied ? "Accès refusé" : "Service injoignable";
            StatusAccount.Text = "";
            ShowBanner(ex.Message);
            throw;
        }
    }

    private void ShowBanner(string text)
    {
        BannerText.Text = text;
        Banner.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RefreshPackagesAsync()
    {
        var selectedId = SelectedPackage?.Id;
        var selectedFolderId = SelectedFolder?.Id;
        _folders = await Api.Folders();
        _packages = await Api.Packages();
        try
        {
            _defaults = await Api.Defaults();
        }
        catch (ApiException) { /* les Détails afficheront les valeurs d'usine */ }
        try
        {
            _libraryUpdates = LibraryList.PackagesWithUpdate(await Api.LibraryStatus());
        }
        catch (ApiException) { /* le repère attendra la prochaine lecture */ }
        RebuildPackageNodes(selectedId, selectedFolderId);
    }

    // Préférences du serveur : valeurs des réglages laissés « par défaut » dans l'onglet Détails.
    private DeploymentDefaults? _defaults;

    // Paquets dont la bibliothèque a une nouvelle version : repérés dans l'arbre.
    private HashSet<long> _libraryUpdates = [];

    /// <summary>Met à jour les repères de l'arbre si la liste des mises à jour disponibles a changé.</summary>
    private void ShowLibraryUpdates(LibraryInfo status)
    {
        var updates = LibraryList.PackagesWithUpdate(status);
        if (updates.SetEquals(_libraryUpdates)) return;
        _libraryUpdates = updates;
        RebuildPackageNodes(SelectedPackage?.Id, SelectedFolder?.Id);
    }

    private async Task RefreshViewAsync()
    {
        var list = await Api.Deployments();
        var running = list.Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
        StatusRunning.Text = running == 0 ? "Aucun déploiement en cours" : $"{running} déploiement(s) en cours";

        if (_view == View.Home) await RefreshHomeAsync(list);
        else if (_view == View.Deployments) AllDeployments.SetDeployments(list);
        else if (_view == View.Library) await RefreshLibraryAsync();
        else if (_view == View.Schedules)
        {
            await RefreshSchedulesAsync();
            var id = SelectedSchedule?.Id;
            ScheduleDeployments.SetDeployments(list.Where(d => id == null ? d.ScheduleId != null : d.ScheduleId == id));
        }
        else if (SelectedPackage is { } pkg)
        {
            PackageDeployments.SetDeployments(list.Where(d => d.PackageId == pkg.Id));
            var schedules = (await Api.Schedules()).Where(s => s.PackageIds.Contains(pkg.Id)).Select(s => new ScheduleRow(s)).ToList();
            var selected = (PackageSchedulesGrid.SelectedItem as ScheduleRow)?.Id;
            PackageSchedulesGrid.ItemsSource = schedules;
            PackageSchedulesGrid.SelectedItem = schedules.FirstOrDefault(r => r.Id == selected);
            PackageSchedulesCount.Text = schedules.Count == 0 ? "Ce paquet ne fait partie d'aucune planification."
                : $"{schedules.Count} planification(s) avec ce paquet";
        }
    }

    // ---------- Vues ----------

    private void SwitchTo(View view)
    {
        _view = view;
        HomeView.Visibility = view == View.Home ? Visibility.Visible : Visibility.Collapsed;
        DeploymentsView.Visibility = view == View.Deployments ? Visibility.Visible : Visibility.Collapsed;
        PackageView.Visibility = view == View.Package ? Visibility.Visible : Visibility.Collapsed;
        SchedulesView.Visibility = view == View.Schedules ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = view == View.Library ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasPackage = _connected && SelectedPackage != null && _view == View.Package;
        EditButton.IsEnabled = hasPackage;
        DeployButton.IsEnabled = hasPackage;
        DeleteButton.IsEnabled = hasPackage;
        NewPackageButton.IsEnabled = NewScheduleButton.IsEnabled = SettingsButton.IsEnabled = _connected;
        MenuEdit.IsEnabled = MenuOptions.IsEnabled = MainArea.IsEnabled = _connected;
    }

    private void ShowPackage(Package p)
    {
        PackageTitle.Text = string.IsNullOrWhiteSpace(p.Version) ? p.Name : $"{p.Name} {p.Version}";
        var steps = p.Steps.Count == 1 ? "1 étape" : $"{p.Steps.Count} étapes";
        PackageSubtitle.Text = string.IsNullOrWhiteSpace(p.Description)
            ? $"Paquet · {steps} · modifié le {Ui.LocalTime(p.ModifiedUtc)}"
            : $"{p.Description} · {steps} · modifié le {Ui.LocalTime(p.ModifiedUtc)}";
        StepsGrid.ItemsSource = p.Steps.Select((s, i) => new StepRow(i + 1, StepSummary.Name(s), StepSummary.TypeText(s.Type),
            s.Conditions.IsEmpty ? "" : "Oui", StepSummary.Details(s, FindPackage))).ToList();
        ShowPackageDetails(p);
        PackageDeployments.SetDeployments([]);
        PackageSchedulesGrid.ItemsSource = null;
        PackageSchedulesCount.Text = "";
        SwitchTo(View.Package);
    }

    /// <summary>Onglet Détails : ce que fait le paquet et d'où viennent ses réglages (paquet ou Préférences).</summary>
    private void ShowPackageDetails(Package p)
    {
        var rows = PackageDetailRows.For(p, _defaults, _folders, _packages);
        PackageDetailsGrid.Children.Clear();
        PackageDetailsGrid.RowDefinitions.Clear();
        foreach (var (label, value) in rows)
        {
            PackageDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = PackageDetailsGrid.RowDefinitions.Count - 1;
            var name = new TextBlock { Text = label, Margin = new Thickness(0, 4, 12, 4), Foreground = (Brush)FindResource("MutedBrush") };
            var text = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            PackageDetailsGrid.Children.Add(name);
            PackageDetailsGrid.Children.Add(text);
        }
    }

    private Package? FindPackage(long id) => _packages.FirstOrDefault(p => p.Id == id);

    // ---------- Navigation ----------

    private async void Nav_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Nav.SelectedItem is not TreeViewItem item) return;
        if (item.Tag is Package p) ShowPackage(p);
        else if (item == _homeNode) SwitchTo(View.Home);
        else if (item == _deploymentsNode) SwitchTo(View.Deployments);
        else if (item == _schedulesNode) SwitchTo(View.Schedules);
        else if (item == _libraryNode) SwitchTo(View.Library);
        else return;
        await RefreshAsync(packages: false);
    }

    private void Nav_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPackage != null) EditPackage_Click(sender, e);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e) => RebuildPackageNodes(SelectedPackage?.Id);

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(packages: true);

    // ---------- Menu Affichage ----------

    private void ShowNode(TreeViewItem node)
    {
        node.IsSelected = true;
        node.BringIntoView();
        Nav.Focus();
    }

    private void ViewHome_Click(object sender, RoutedEventArgs e) => ShowNode(_homeNode);

    private void ViewDeployments_Click(object sender, RoutedEventArgs e) => ShowNode(_deploymentsNode);

    private void ViewSchedules_Click(object sender, RoutedEventArgs e) => ShowNode(_schedulesNode);

    private void ViewLibrary_Click(object sender, RoutedEventArgs e) => ShowNode(_libraryNode);

    private void ViewPackages_Click(object sender, RoutedEventArgs e)
    {
        _packagesNode.IsExpanded = true;
        ShowNode(_packagesNode);
    }

    /// <summary>Ctrl+F : place le curseur dans le filtre des paquets.</summary>
    private void ViewFilter_Click(object sender, RoutedEventArgs e)
    {
        Filter.Focus();
        Filter.SelectAll();
    }
}
