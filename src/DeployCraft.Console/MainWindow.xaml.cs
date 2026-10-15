using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record StepRow(int Number, string Name, string Type, string Details);

public record ScheduleRow(Schedule Schedule) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fait avancer le compte à rebours affiché.</summary>
    public void Tick() => PropertyChanged?.Invoke(this, new(nameof(Countdown)));

    public long Id => Schedule.Id;
    public string Name => Schedule.Name;
    public string Enabled => Schedule.Enabled ? "Oui" : "Non";
    public string Triggers => Schedule.Triggers.Count == 0 ? "(aucun : démarrage manuel)" : string.Join(" ; ", Schedule.Triggers.Select(TriggerCalendar.Describe));
    public DateTime? NextUtc => Schedule.NextRunUtc;
    public string Next => Schedule.Enabled ? Ui.LocalTime(NextUtc) is { Length: > 0 } t ? t : "—" : "Désactivée";

    /// <summary>Compte à rebours jusqu'à la prochaine exécution (le service vérifie toutes les 15 s).</summary>
    public string Countdown
    {
        get
        {
            if (!Schedule.Enabled || NextUtc is not { } next) return "";
            var left = next - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return "imminent";
            if (left.TotalMinutes < 1) return $"dans {left.Seconds} s";
            if (left.TotalHours < 1) return $"dans {(int)left.TotalMinutes} min {left.Seconds:00} s";
            if (left.TotalDays < 1) return $"dans {(int)left.TotalHours} h {left.Minutes:00} min";
            return $"dans {(int)left.TotalDays} j {left.Hours} h";
        }
    }
    public DateTime? LastUtc => Schedule.LastRunUtc;
    public string Last => Ui.LocalTime(LastUtc);
    public string Result => Schedule.LastResult ?? "";
}

public partial class MainWindow : Window
{
    private enum View { Deployments, Package, Schedules }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TreeViewItem _deploymentsNode;
    private readonly TreeViewItem _schedulesNode;
    private readonly TreeViewItem _packagesNode;
    private List<Package> _packages = [];
    private View _view = View.Deployments;
    private bool _refreshing;
    private bool _pendingRefresh;
    private bool _pendingPackages;

    public MainWindow()
    {
        InitializeComponent();

        _deploymentsNode = new TreeViewItem { Header = Ui.Header(Ui.IconHistory, "Tous les déploiements"), Tag = "deployments" };
        _schedulesNode = new TreeViewItem { Header = Ui.Header(Ui.IconSchedule, "Toutes les planifications"), Tag = "schedules" };
        _packagesNode = new TreeViewItem { Header = Ui.Header(Ui.IconFolder, "Paquets"), IsExpanded = true };
        Nav.Items.Add(_deploymentsNode);
        Nav.Items.Add(_schedulesNode);
        Nav.Items.Add(_packagesNode);

        foreach (var panel in new[] { AllDeployments, PackageDeployments, ScheduleDeployments })
        {
            panel.DeployRequested += (id, targets) => Deploy(id, targets);
            panel.Changed += () => _ = RefreshAsync(packages: false);
        }

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
        Loaded += async (_, _) =>
        {
            _deploymentsNode.IsSelected = true;
            await RefreshAsync(packages: true);
            _timer.Start();
        };
    }

    private Package? SelectedPackage => (Nav.SelectedItem as TreeViewItem)?.Tag as Package;

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

    // ---------- Raccourcis clavier ----------

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        // Pas de raccourci pendant la saisie dans une zone de texte (sauf F5).
        if (e.Key != Key.F5 && Keyboard.FocusedElement is TextBox) return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Ctrl+R sur la grille des postes : laissé au panneau (redéploie les postes sélectionnés).
        if (key == Key.R && ctrl && CurrentPanel.TargetsFocused) return;

        Action? action = (key, ctrl, ctrlShift, alt) switch
        {
            (Key.F5, _, _, _) => () => Refresh_Click(this, e),
            (Key.N, true, _, _) => () => NewPackage_Click(this, e),
            (Key.N, _, true, _) => () => NewSchedule_Click(this, e),
            (Key.O, true, _, _) => () => EditPackage_Click(this, e),
            (Key.D, true, _, _) => () => DeployOnce_Click(this, e),
            (Key.R, true, _, _) => () => Redeploy_Click(this, e),
            (Key.R, _, true, _) => () => RedeployFailed_Click(this, e),
            (Key.Insert, _, _, true) => () => Duplicate_Click(this, e),
            (Key.OemComma, true, _, _) => () => Settings_Click(this, e),
            (Key.F2, _, _, _) when Keyboard.Modifiers == ModifierKeys.None => () => Rename_Click(this, e),
            (Key.Delete, _, _, _) when Keyboard.Modifiers == ModifierKeys.None => () => Delete_Click(this, e),
            _ => null,
        };
        if (action == null) return;
        action();
        e.Handled = true;
    }

    /// <summary>Active ou grise les entrées des menus Fichier et Édition selon la sélection.</summary>
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        var package = SelectedPackage != null;
        MenuDuplicate.IsEnabled = MenuOpen.IsEnabled = MenuDeployOnce.IsEnabled = MenuRename.IsEnabled = package;
        var panel = CurrentPanel;
        MenuAbort.IsEnabled = panel.CanAbort;
        MenuRedeploy.IsEnabled = panel.CanRedeploy;
        MenuRedeployFailed.IsEnabled = MenuRedeployFromStep.IsEnabled = panel.CanRedeployFailed;
        MenuDelete.IsEnabled = TreeHasFocus ? package : panel.CanDelete;
    }

    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(Nav);
        Ui.SelectOnRightClick(StepsGrid);

        Ui.ContextMenu(Nav, () =>
        {
            if (SelectedPackage != null)
                return
                [
                    new("Déployer une fois…", () => DeployOnce_Click(this, new()), Gesture: "Ctrl+D"),
                    new("Modifier…", () => EditPackage_Click(this, new()), Gesture: "Ctrl+O"),
                    Ui.Separator,
                    new("Dupliquer", () => Duplicate_Click(this, new()), Gesture: "Alt+Inser"),
                    new("Renommer", () => Rename_Click(this, new()), Gesture: "F2"),
                    Ui.Separator,
                    new("Supprimer", () => DeletePackage_Click(this, new()), Gesture: "Suppr"),
                ];
            if (Nav.SelectedItem == _packagesNode)
                return [new("Nouveau paquet…", () => NewPackage_Click(this, new()), Gesture: "Ctrl+N")];
            return [new("Actualiser", () => Refresh_Click(this, new()), Gesture: "F5")];
        });

        Ui.ContextMenu(StepsGrid, () => SelectedPackage != null
            ? [new("Modifier le paquet…", () => EditPackage_Click(this, new()))]
            : []);

        Ui.SelectOnRightClick(SchedulesGrid);
        Ui.ContextMenu(SchedulesGrid, () =>
        {
            if (SelectedSchedule is not { } s) return [new("Nouvelle planification…", () => NewSchedule_Click(this, new()), Gesture: "Ctrl+Maj+N")];
            return
            [
                new("Démarrer maintenant", () => RunSchedule_Click(this, new())),
                new("Modifier…", () => EditSchedule_Click(this, new())),
                Ui.Separator,
                new(s.Enabled ? "Désactiver" : "Activer", () => ToggleSchedule(s)),
                new("Dupliquer", () => DuplicateSchedule(s)),
                Ui.Separator,
                new("Supprimer", () => DeleteSchedule(s), Gesture: "Suppr"),
            ];
        });

        Ui.SelectOnRightClick(PackageSchedulesGrid);
        Ui.ContextMenu(PackageSchedulesGrid, () => PackageSchedulesGrid.SelectedItem is ScheduleRow
            ? [new("Modifier…", () => EditPackageSchedule_Click(this, new())), new("Nouvelle planification…", () => NewScheduleForPackage_Click(this, new()))]
            : [new("Nouvelle planification…", () => NewScheduleForPackage_Click(this, new()))]);
    }

    // ---------- Rafraîchissement ----------

    private async Task RefreshAsync(bool packages)
    {
        // Une demande arrivée pendant un rafraîchissement est rejouée juste après, pas perdue.
        _pendingPackages |= packages;
        if (_refreshing) { _pendingRefresh = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _pendingRefresh = false;
                var withPackages = _pendingPackages;
                _pendingPackages = false;
                await RefreshStatusAsync();
                if (withPackages) await RefreshPackagesAsync();
                await RefreshViewAsync();
            } while (_pendingRefresh);
        }
        catch (ApiException) { /* déjà signalé dans le bandeau */ }
        finally
        {
            _refreshing = false;
            UpdateButtons();
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var s = await Api.Status();
            StatusServer.Text = $"Service : {s.Machine} ({s.RunningAs})";
            StatusAccount.Text = s.DefaultCredential is { } c ? $"Identifiant par défaut : {c.Account}" : "Aucun identifiant";

            var issues = new List<string>();
            if (!s.Elevated) issues.Add("Le service DeployCraft ne tourne pas en administrateur : les déploiements échoueront.");
            if (s.DefaultCredential == null) issues.Add("Aucun identifiant enregistré (Options › Identifiants). Nécessaire pour les postes du domaine.");
            if (s.UnreadableCredentials > 0) issues.Add("Un ou plusieurs mots de passe d'identifiants ne peuvent pas être relus : ressaisissez-les (Options › Identifiants).");
            ShowBanner(string.Join(Environment.NewLine, issues));
        }
        catch (ApiException ex)
        {
            StatusServer.Text = "Service injoignable";
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
        _packages = await Api.Packages();
        RebuildPackageNodes(selectedId);
    }

    private void RebuildPackageNodes(long? selectId)
    {
        var filter = Filter.Text.Trim();
        _packagesNode.Items.Clear();
        foreach (var p in _packages.Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var node = new TreeViewItem { Header = Ui.Header(Ui.IconPackage, p.Name), Tag = p };
            _packagesNode.Items.Add(node);
            if (p.Id == selectId) node.IsSelected = true;
        }
    }

    private async Task RefreshViewAsync()
    {
        var list = await Api.Deployments();
        var running = list.Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
        StatusRunning.Text = running == 0 ? "Aucun déploiement en cours" : $"{running} déploiement(s) en cours";

        if (_view == View.Deployments) AllDeployments.SetDeployments(list);
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

    private bool _refreshingSchedules;

    private async Task RefreshSchedulesAsync(long? select = null)
    {
        var rows = (await Api.Schedules()).Select(s => new ScheduleRow(s)).ToList();
        var selected = select ?? SelectedSchedule?.Id;
        _refreshingSchedules = true;
        SchedulesGrid.ItemsSource = rows;
        SchedulesGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        _refreshingSchedules = false;
        var on = rows.Count(r => r.Schedule.Enabled);
        SchedulesSubtitle.Text = rows.Count == 0
            ? "Aucune planification : créez-en une pour déployer automatiquement (ou réessayer jusqu'à réussite)"
            : $"{rows.Count} planification(s), {on} activée(s) · en dessous, les déploiements {(SelectedSchedule is { } s ? $"de « {s.Name} »" : "de toutes les planifications")}";
        RunScheduleButton.IsEnabled = SelectedSchedule != null;
    }

    private async void SchedulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSchedules) return;
        RunScheduleButton.IsEnabled = SelectedSchedule != null;
        await RefreshAsync(packages: false);
    }

    // ---------- Vues ----------

    private void SwitchTo(View view)
    {
        _view = view;
        DeploymentsView.Visibility = view == View.Deployments ? Visibility.Visible : Visibility.Collapsed;
        PackageView.Visibility = view == View.Package ? Visibility.Visible : Visibility.Collapsed;
        SchedulesView.Visibility = view == View.Schedules ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasPackage = SelectedPackage != null && _view == View.Package;
        EditButton.IsEnabled = hasPackage;
        DeployButton.IsEnabled = hasPackage;
        DeleteButton.IsEnabled = hasPackage;
    }

    private void ShowPackage(Package p)
    {
        PackageTitle.Text = string.IsNullOrWhiteSpace(p.Version) ? p.Name : $"{p.Name} {p.Version}";
        var steps = p.Steps.Count == 1 ? "1 étape" : $"{p.Steps.Count} étapes";
        PackageSubtitle.Text = string.IsNullOrWhiteSpace(p.Description)
            ? $"Paquet · {steps} · modifié le {Ui.LocalTime(p.ModifiedUtc)}"
            : $"{p.Description} · {steps} · modifié le {Ui.LocalTime(p.ModifiedUtc)}";
        StepsGrid.ItemsSource = p.Steps.Select((s, i) => new StepRow(i + 1, StepName(s), StepTypeText(s.Type), StepDetails(s, FindPackage))).ToList();
        PackageDeployments.SetDeployments([]);
        PackageSchedulesGrid.ItemsSource = null;
        PackageSchedulesCount.Text = "";
        SwitchTo(View.Package);
    }

    /// <summary>Titre de l'étape, précédé de sa section (sauf Principales) et signalée si elle est désactivée.</summary>
    public static string StepName(PackageStep s) => SectionPrefix(s.Section) + (s.Enabled ? s.Name : $"{s.Name} (désactivée)");

    public static string SectionPrefix(StepSection section) => section switch
    {
        StepSection.Before => "Avant · ",
        StepSection.After => "Après · ",
        _ => "",
    };

    private Package? FindPackage(long id) => _packages.FirstOrDefault(p => p.Id == id);

    /// <summary>Résumé d'une étape pour les listes : fichier, première ligne de commande, durée de pause, paquet imbriqué.</summary>
    public static string StepDetails(PackageStep s, Func<long, Package?> lookup) => s.Type switch
    {
        StepType.Install => $"{s.File} {s.Arguments}".Trim(),
        StepType.Pause => $"{s.PauseSeconds} s",
        StepType.Nested => s.NestedPackageId is { } id && lookup(id) is { } p
            ? $"Paquet : {p.Name} ({p.Steps.Count} étape{(p.Steps.Count > 1 ? "s" : "")})"
            : "Paquet : (introuvable)",
        _ => (s.Command ?? "").Split('\n')[0].Trim(),
    };

    public static string StepTypeText(StepType t) => t switch
    {
        StepType.Install => "Installer",
        StepType.Command => "Commande",
        StepType.Pause => "Pause",
        StepType.Nested => "Paquet imbriqué",
        _ => "PowerShell",
    };

    // ---------- Navigation ----------

    private async void Nav_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Nav.SelectedItem is not TreeViewItem item) return;
        if (item.Tag is Package p) ShowPackage(p);
        else if (item == _deploymentsNode) SwitchTo(View.Deployments);
        else if (item == _schedulesNode) SwitchTo(View.Schedules);
        else return;
        await RefreshAsync(packages: false);
    }

    private void Nav_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPackage != null) EditPackage_Click(sender, e);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e) => RebuildPackageNodes(SelectedPackage?.Id);

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(packages: true);

    // ---------- Paquets ----------

    private async void NewPackage_Click(object sender, RoutedEventArgs e)
    {
        var window = new PackageWindow(new Package { Steps = [PackageWindow.NewStep(StepType.Install)] }) { Owner = this };
        window.ShowDialog();
        if (window.Saved) await AfterPackageSaved(window.Package);
    }

    private async void EditPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } selected) return;
        try
        {
            var window = new PackageWindow(await Api.Package(selected.Id)) { Owner = this };
            window.ShowDialog();
            if (window.Saved) await AfterPackageSaved(window.Package);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async Task AfterPackageSaved(Package saved)
    {
        _packages = await Api.Packages();
        RebuildPackageNodes(saved.Id);
        if (_packages.FirstOrDefault(p => p.Id == saved.Id) is { } fresh) ShowPackage(fresh);
        await RefreshAsync(packages: false);
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } source) return;
        try
        {
            var copy = await Api.Package(source.Id);
            copy.Id = 0;
            copy.Name += " (copie)";
            await AfterPackageSaved(await Api.SavePackage(copy));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } selected) return;
        if (InputDialog.Ask(this, "Renommer", "Nouveau nom du paquet :", selected.Name) is not { } name || name == selected.Name) return;
        try
        {
            var package = await Api.Package(selected.Id);
            package.Name = name;
            await AfterPackageSaved(await Api.SavePackage(package));
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeletePackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is not { } p) return;
        if (MessageDialog.Show(this, $"Supprimer le paquet « {p.Name} » ?\nL'historique de ses déploiements est conservé.",
                "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeletePackage(p.Id);
            _deploymentsNode.IsSelected = true;
            await RefreshAsync(packages: true);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Édition (agit sur le déploiement sélectionné) ----------

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanAbort) CurrentPanel.AbortDeployment();
    }

    private void Redeploy_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeploy) CurrentPanel.Redeploy();
    }

    private void RedeployFailed_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeployFailed) CurrentPanel.RedeployFailed();
    }

    private void RedeployFromStep_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPanel.CanRedeployFailed) CurrentPanel.RedeployFromFailedStep();
    }

    /// <summary>Suppr : le paquet si l'arbre a le focus, la planification si sa grille l'a, sinon le déploiement sélectionné.</summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TreeHasFocus) DeletePackage_Click(sender, e);
        else if (SchedulesGrid.IsKeyboardFocusWithin && SelectedSchedule is { } s) DeleteSchedule(s);
        else if (CurrentPanel.CanDelete) CurrentPanel.DeleteSelected();
    }

    // ---------- Planifications ----------

    private void NewSchedule_Click(object sender, RoutedEventArgs e)
    {
        var schedule = new Schedule();
        if (SelectedPackage is { } p && _view == View.Package) { schedule.PackageIds = [p.Id]; schedule.Name = p.Name; }
        OpenSchedule(schedule);
    }

    private void NewScheduleForPackage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPackage is { } p) OpenSchedule(new Schedule { PackageIds = [p.Id], Name = p.Name });
    }

    private async void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedule is not { } s) return;
        try { OpenSchedule(await Api.Schedule(s.Id)); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void EditPackageSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (PackageSchedulesGrid.SelectedItem is not ScheduleRow row) return;
        try { OpenSchedule(await Api.Schedule(row.Id)); }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    /// <summary>Ouvre la fenêtre d'une planification ; après enregistrement, affiche la page des planifications.</summary>
    public async void OpenSchedule(Schedule schedule)
    {
        var window = new ScheduleWindow(schedule) { Owner = this };
        window.ShowDialog();
        if (window.Saved is not { } saved) return;
        if (_view != View.Package) await ShowSchedule(saved.Id);
        else await RefreshAsync(packages: false);
    }

    private async Task ShowSchedule(long id)
    {
        _schedulesNode.IsSelected = true;
        SwitchTo(View.Schedules);
        try { await RefreshSchedulesAsync(select: id); }
        catch (ApiException) { }
        await RefreshAsync(packages: false);
    }

    private async void RunSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedule is not { } s) return;
        try
        {
            var run = await Api.RunSchedule(s.Id);
            if (run.Deployments.Count == 0)
                MessageDialog.Show(this, run.Result ?? "Rien à déployer.", s.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void ToggleSchedule(Schedule s)
    {
        try
        {
            var full = await Api.Schedule(s.Id);
            full.Enabled = !full.Enabled;
            await Api.SaveSchedule(full);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DuplicateSchedule(Schedule s)
    {
        try
        {
            var copy = await Api.Schedule(s.Id);
            copy.Id = 0;
            copy.Name += " (copie)";
            copy.Enabled = false; // une copie active doublerait les déploiements : on l'active à la main
            await ShowSchedule((await Api.SaveSchedule(copy)).Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void DeleteSchedule(Schedule s)
    {
        if (MessageDialog.Show(this, $"Supprimer la planification « {s.Name} » ?\nSes déploiements passés restent dans l'historique.",
                "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteSchedule(s.Id);
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Déploiement ----------

    private void DeployOnce_Click(object sender, RoutedEventArgs e)
    {
        Deploy(SelectedPackage?.Id, []);
    }

    private async void Deploy(long? packageId, IReadOnlyList<TargetRequest> targets)
    {
        var window = new DeployWindow(packageId, targets) { Owner = this };
        var ok = window.ShowDialog() == true;
        if (window.ScheduleRequest is { } schedule) { OpenSchedule(schedule); return; }
        if (!ok || window.Deployments.FirstOrDefault() is not { } d) return;

        // On affiche le nouveau déploiement là où l'utilisateur se trouve.
        if (_view == View.Package && SelectedPackage?.Id == d.PackageId) PackageDeployments.Select(d.Id);
        else
        {
            _deploymentsNode.IsSelected = true;
            AllDeployments.Select(d.Id);
        }
        await RefreshAsync(packages: false);
    }

    // ---------- Options et aide ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    private async void Credentials_Click(object sender, RoutedEventArgs e)
    {
        new CredentialsWindow { Owner = this }.ShowDialog();
        try
        {
            await RefreshAsync(packages: false);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private void AdminCmd_Click(object sender, RoutedEventArgs e) => StartElevated("cmd.exe");
    private void AdminPowerShell_Click(object sender, RoutedEventArgs e) => StartElevated("powershell.exe");

    private void StartElevated(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception) { /* demande d'élévation refusée */ }
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageDialog.Show(this, $"DeployCraft {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\nDéploiement de logiciels sur le parc Windows.",
            "À propos", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
