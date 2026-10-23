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
    private readonly PackageList _packageList = new(Api.Folders, Api.Packages, Api.Defaults, Api.LibraryStatus);
    private View _view = View.Home;
    // False while the service refuses access or does not respond: commands are then greyed out.
    private bool _connected;
    private readonly RefreshQueue _refresh;

    public MainWindow()
    {
        InitializeComponent();
        _refresh = new RefreshQueue(RefreshOnceAsync);
        _packagePage = new PackagePage(_packageList, Api.Schedules, Ui.LocalTime);

        _homeNode = new TreeViewItem { Header = Ui.Header(Ui.IconHome, L.Get("MainWindow_Home")), Tag = "home" };
        _deploymentsNode = new TreeViewItem { Header = Ui.Header(Ui.IconHistory, L.Get("MainWindow_AllDeployments")), Tag = "deployments" };
        _schedulesNode = new TreeViewItem { Header = Ui.Header(Ui.IconSchedule, L.Get("MainWindow_AllSchedules")), Tag = "schedules" };
        _libraryNode = new TreeViewItem { Header = Ui.Header(Ui.IconLibrary, L.Get("MainWindow_Library")), Tag = "library" };
        _packagesNode = new TreeViewItem { Header = Ui.Header(Ui.IconFolder, L.Get("MainWindow_Packages")), IsExpanded = true };
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
        // Schedule countdown: every second, without reloading the data.
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

    // Tree width chosen with the mouse, restored at the next opening.
    private const string NavWidthPref = "NavWidth";
    private const double NavDefaultWidth = 250;

    private void NavSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        ConsolePrefs.Set(NavWidthPref, NavColumn.ActualWidth);

    private void NavSplitter_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavColumn.Width = new GridLength(NavDefaultWidth);
        ConsolePrefs.Set(NavWidthPref, NavDefaultWidth);
    }

    /// <summary>Deployments panel visible (the one the Edit menu acts on).</summary>
    private DeploymentsPanel CurrentPanel => _view switch
    {
        View.Package => PackageDeployments,
        View.Schedules => ScheduleDeployments,
        _ => AllDeployments,
    };

    private Schedule? SelectedSchedule => (SchedulesGrid.SelectedItem as ScheduleRow)?.Schedule;

    /// <summary>The Edit menu commands target the tree (package) or the deployments panel depending on focus.</summary>
    private bool TreeHasFocus => Nav.IsKeyboardFocusWithin;

    // ---------- Refresh ----------

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
            StatusServer.Text = L.Format("MainWindow_StatusService", s.Machine, s.RunningAs, s.ConnectedAs);
            StatusAccount.Text = s.DefaultCredential is { } c ? L.Format("MainWindow_StatusDefaultCredential", c.Account) : L.Get("MainWindow_StatusNoCredential");

            ShowBanner(ServerIssues.For(s));
        }
        catch (ApiException ex)
        {
            _connected = false;
            StatusServer.Text = ex.AccessDenied ? L.Get("MainWindow_StatusAccessDenied") : L.Get("MainWindow_StatusUnreachable");
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
        await _packageList.RefreshAsync();
        RebuildPackageNodes(selectedId, selectedFolderId);
    }

    /// <summary>Updates the tree markers if the list of available updates changed.</summary>
    private void ShowLibraryUpdates(LibraryInfo status)
    {
        if (_packageList.UpdateLibrary(status)) RebuildPackageNodes(SelectedPackage?.Id, SelectedFolder?.Id);
    }

    private async Task RefreshViewAsync()
    {
        var list = await Api.Deployments();
        var running = list.Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
        StatusRunning.Text = running == 0 ? L.Get("MainWindow_StatusNoneRunning") : L.Format("MainWindow_StatusRunning", running);

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
            var activity = await _packagePage.RefreshAsync(pkg.Id, list, PackageDeployments.SetDeployments,
                () => (PackageSchedulesGrid.SelectedItem as ScheduleRow)?.Id);
            PackageSchedulesGrid.ItemsSource = activity.Schedules;
            PackageSchedulesGrid.SelectedItem = activity.SelectedSchedule;
            PackageSchedulesCount.Text = activity.ScheduleCount;
        }
    }

    // ---------- Views ----------

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
        var overview = _packagePage.Overview(p);
        PackageTitle.Text = overview.Title;
        PackageSubtitle.Text = overview.Subtitle;
        StepsGrid.ItemsSource = overview.Steps;
        ShowPackageDetails(overview.Details);
        PackageDeployments.SetDeployments([]);
        PackageSchedulesGrid.ItemsSource = null;
        PackageSchedulesCount.Text = "";
        SwitchTo(View.Package);
    }

    /// <summary>Details tab: what the package does and where its settings come from (package or Preferences).</summary>
    private void ShowPackageDetails(IEnumerable<(string Label, string Value)> rows)
    {
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

    private readonly PackagePage _packagePage;

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

    // ---------- View menu ----------

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

    /// <summary>Ctrl+F: puts the cursor in the package filter.</summary>
    private void ViewFilter_Click(object sender, RoutedEventArgs e)
    {
        Filter.Focus();
        Filter.SelectAll();
    }
}
