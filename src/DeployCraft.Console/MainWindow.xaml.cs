using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = MainWindowCore.RefreshInterval };
    private readonly TreeViewItem _homeNode;
    private readonly TreeViewItem _deploymentsNode;
    private readonly TreeViewItem _schedulesNode;
    private readonly TreeViewItem _libraryNode;
    private readonly TreeViewItem _packagesNode;
    private readonly PackageList _packageList = new(Api.Folders, Api.Packages, Api.Defaults, Api.LibraryStatus);
    private readonly MainWindowCore _core;
    private readonly MainWindowUpdates _updates;
    private MainView _view => _core.View;
    private bool _connected => _core.Connected;

    public MainWindow()
    {
        _core = new MainWindowCore(Api.Status, Api.Deployments, RefreshPackagesAsync, ShowStatus, RefreshViewAsync, UpdateButtons);
        _updates = new MainWindowUpdates(UpdateCheck.FindAsync, Api.Deployments, () => _core.ServerMachine,
            Environment.MachineName, UpdateCheck.CurrentVersion);
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        var size = new WindowDimensions(Width, Height, MinWidth, MinHeight).FitWorkArea(workArea.Width, workArea.Height);
        // Lower the minimum first: it must not override the work-area limit on a very small desktop.
        MinWidth = size.MinWidth;
        MinHeight = size.MinHeight;
        Width = size.Width;
        Height = size.Height;
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
            var update = await _updates.CheckAsync();
            ShowUpdate();
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
        MainView.Package => PackageDeployments,
        MainView.Schedules => ScheduleDeployments,
        _ => AllDeployments,
    };

    private Schedule? SelectedSchedule => (SchedulesGrid.SelectedItem as ScheduleRow)?.Schedule;

    /// <summary>The Edit menu commands target the tree (package) or the deployments panel depending on focus.</summary>
    private bool TreeHasFocus => Nav.IsKeyboardFocusWithin;

    // ---------- Refresh ----------

    private Task RefreshAsync(bool packages) => _core.RefreshAsync(packages);

    private void ShowStatus()
    {
        StatusServer.Text = _core.ServerText;
        StatusAccount.Text = _core.AccountText;
        BannerText.Text = _core.BannerText;
        Banner.Visibility = string.IsNullOrEmpty(_core.BannerText) ? Visibility.Collapsed : Visibility.Visible;
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

    private async Task RefreshViewAsync(MainView view, List<Deployment> list)
    {
        StatusRunning.Text = _core.RunningText;

        if (view == MainView.Home) await RefreshHomeAsync(list);
        else if (view == MainView.Deployments) AllDeployments.SetDeployments(list);
        else if (view == MainView.Library) await RefreshLibraryAsync();
        else if (view == MainView.Schedules)
        {
            await RefreshSchedulesAsync();
            var id = SelectedSchedule?.Id;
            ScheduleDeployments.SetDeployments(ScheduleList.Deployments(list, id));
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

    private void SwitchTo(MainView view)
    {
        _core.Navigate(view);
        HomeView.Visibility = view == MainView.Home ? Visibility.Visible : Visibility.Collapsed;
        DeploymentsView.Visibility = view == MainView.Deployments ? Visibility.Visible : Visibility.Collapsed;
        PackageView.Visibility = view == MainView.Package ? Visibility.Visible : Visibility.Collapsed;
        SchedulesView.Visibility = view == MainView.Schedules ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = view == MainView.Library ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasPackage = _core.CanUsePackage(SelectedPackage != null);
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
        SwitchTo(MainView.Package);
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
        else if (item == _homeNode) SwitchTo(MainView.Home);
        else if (item == _deploymentsNode) SwitchTo(MainView.Deployments);
        else if (item == _schedulesNode) SwitchTo(MainView.Schedules);
        else if (item == _libraryNode) SwitchTo(MainView.Library);
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
