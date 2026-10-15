using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record DeploymentRow(Deployment Deployment, long Id, string Status, string PackageName, DateTime CreatedUtc,
    string Created, string Duration, int Total, int Ok, int Ko)
{
    public string Prioritized => Deployment.Targets.Any(t => t.Prioritized) ? "Oui" : "";
}

public record TargetRow(DeploymentTarget Target, long DeploymentId, string Computer, string Status, string Steps,
    string Detail, string Started, string Duration);

/// <summary>
/// Vue maître/détail des déploiements : grille des déploiements + carte de détails,
/// puis grille des postes du déploiement sélectionné + carte du poste.
/// </summary>
public partial class DeploymentsPanel : UserControl
{
    private List<Deployment> _all = [];
    private long? _pendingSelection;

    /// <summary>Demande de déploiement (paquet, cibles pré-remplies avec leur étape de départ).</summary>
    public event Action<long, IReadOnlyList<TargetRequest>>? DeployRequested;
    /// <summary>L'historique a changé (suppression, annulation, lancement direct) : la fenêtre doit rafraîchir.</summary>
    public event Action? Changed;

    public DeploymentsPanel()
    {
        InitializeComponent();
        SetupContextMenus();
    }

    public bool ShowPackageColumn
    {
        set => PackageColumn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private Deployment? Selected => (DeploymentsGrid.SelectedItem as DeploymentRow)?.Deployment;
    private List<DeploymentTarget> SelectedTargets => TargetsGrid.SelectedItems.Cast<TargetRow>().Select(r => r.Target).ToList();
    private Window? Owner => Window.GetWindow(this);

    private static bool IsActive(DeploymentTarget t) => t.Status is TargetStatus.Queued or TargetStatus.Running;
    private static bool IsActive(Deployment d) => d.Status is DeploymentStatus.Queued or DeploymentStatus.Running;

    // ---------- Actions (barre de menus Édition et clic droit) ----------

    /// <summary>La grille des postes a le focus : Ctrl+R vise alors les postes sélectionnés.</summary>
    public bool TargetsFocused => TargetsGrid.IsKeyboardFocusWithin;

    public bool CanAbort => Selected is { } d && IsActive(d);
    public bool CanRedeploy => Selected != null;
    public bool CanRedeployFailed => Selected is { } d && d.Targets.Any(t => t.Status == TargetStatus.Failed);
    public bool CanDelete => Selected is { } d && !IsActive(d);

    public void AbortDeployment()
    {
        if (Selected is { } d) _ = AbortAsync(d, null);
    }

    public void Redeploy()
    {
        if (Selected is { } d) DeployRequested?.Invoke(d.PackageId, Fresh(d.Targets.Select(t => t.Computer)));
    }

    public void RedeployFailed()
    {
        if (Selected is { } d)
            DeployRequested?.Invoke(d.PackageId, Fresh(d.Targets.Where(t => t.Status == TargetStatus.Failed).Select(t => t.Computer)));
    }

    public void RedeployFromFailedStep() => RedeployFromFailedStep(Selected?.Targets.Where(t => t.Status == TargetStatus.Failed).ToList() ?? []);

    public void DeleteSelected() => _ = DeleteSelectedAsync();

    /// <summary>
    /// Étape (numérotée dans le paquet) à laquelle reprendre pour un poste en échec :
    /// la première étape en échec, ou le point de départ s'il a échoué avant d'exécuter une étape.
    /// </summary>
    private static int FailedStep(DeploymentTarget t)
    {
        var index = t.Steps.FindIndex(s => s.State == "failed");
        return index < 0 ? t.StartStep : t.StartStep + index;
    }

    /// <summary>Un seul déploiement : chaque poste reprend à sa propre étape en échec.</summary>
    private void RedeployFromFailedStep(List<DeploymentTarget> targets)
    {
        if (Selected is not { } d || targets.Count == 0) return;
        DeployRequested?.Invoke(d.PackageId, targets.Select(t => new TargetRequest(t.Computer, FailedStep(t))).ToList());
    }

    private static List<TargetRequest> Fresh(IEnumerable<string> computers) => computers.Select(c => new TargetRequest(c)).ToList();

    private async Task AbortAsync(Deployment d, List<DeploymentTarget>? targets)
    {
        var what = targets == null ? $"le déploiement {d.Id} ({d.PackageName})"
            : targets.Count == 1 ? $"le déploiement sur {targets[0].Computer}" : $"le déploiement sur ces {targets.Count} postes";
        if (MessageBox.Show(Owner!, $"Annuler {what} ?\nUne installation en cours sera interrompue.",
                "Annuler", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await Api.Abort(d.Id, targets?.Select(t => t.Id));
            Changed?.Invoke();
        }
        catch (ApiException ex) { Ui.Error(Owner, ex); }
    }

    private async Task DeleteSelectedAsync()
    {
        if (Selected is not { } d || IsActive(d)) return;
        if (MessageBox.Show(Owner!, $"Supprimer le déploiement {d.Id} ({d.PackageName}) de l'historique ?",
                "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.DeleteDeployment(d.Id);
            Changed?.Invoke();
        }
        catch (ApiException ex) { Ui.Error(Owner, ex); }
    }

    // ---------- Données ----------

    public void SetDeployments(IEnumerable<Deployment> deployments)
    {
        _all = deployments.ToList();
        ApplyDeploymentFilter();
    }

    /// <summary>Sélectionne un déploiement (éventuellement au prochain rafraîchissement s'il n'est pas encore listé).</summary>
    public void Select(long id)
    {
        _pendingSelection = id;
        ApplyDeploymentFilter();
    }

    private void ApplyDeploymentFilter()
    {
        var filter = DeploymentFilter.Text.Trim();
        var rows = _all.Select(ToRow)
            .Where(r => filter.Length == 0 || Matches(filter, r.Id.ToString(), Ui.StatusText(r.Status), r.PackageName, r.Created))
            .ToList();

        var selectedId = _pendingSelection ?? (DeploymentsGrid.SelectedItem as DeploymentRow)?.Id;
        DeploymentsGrid.ItemsSource = rows;
        var toSelect = rows.FirstOrDefault(r => r.Id == selectedId) ?? (selectedId == null ? rows.FirstOrDefault() : null);
        if (toSelect != null && _pendingSelection == toSelect.Id) _pendingSelection = null;
        DeploymentsGrid.SelectedItem = toSelect;
        if (toSelect != null) DeploymentsGrid.ScrollIntoView(toSelect);

        DeploymentsTitle.Text = rows.Count switch { 0 => "Aucun déploiement", 1 => "1 déploiement", _ => $"{rows.Count} déploiements" };
        ShowDeployment();
    }

    private static DeploymentRow ToRow(Deployment d) => new(
        d, d.Id, DisplayStatus(d), d.PackageName, d.CreatedUtc, Ui.LocalTime(d.CreatedUtc), Ui.Duration(d.CreatedUtc, d.EndedUtc),
        d.Targets.Count, d.Targets.Count(t => t.Status == TargetStatus.Successful), d.Targets.Count(t => t.Status == TargetStatus.Failed));

    /// <summary>Un déploiement terminé avec une partie des postes en échec est affiché « Partiel ».</summary>
    private static string DisplayStatus(Deployment d) =>
        d.Status == DeploymentStatus.Failed && d.Targets.Any(t => t.Status == TargetStatus.Successful) ? "Partial" : d.Status.ToString();

    private static bool Matches(string filter, params string?[] values) =>
        values.Any(v => v?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

    // ---------- Carte du déploiement + grille des postes ----------

    private void ShowDeployment()
    {
        var d = Selected;
        NoDeployment.Visibility = d == null ? Visibility.Visible : Visibility.Collapsed;
        DeploymentDetails.Visibility = d == null ? Visibility.Collapsed : Visibility.Visible;
        if (d == null)
        {
            TargetsGrid.ItemsSource = null;
            TargetsTitle.Text = "Postes";
            ShowTarget();
            return;
        }

        var status = DisplayStatus(d);
        DStatusIcon.Text = Ui.StatusIcon(status);
        DStatusIcon.Foreground = DStatus.Foreground = Ui.StatusBrush(status);
        DTitle.Text = $"Déploiement {d.Id}";
        DStatus.Text = Ui.StatusText(status);
        DPackage.Text = d.StartStep > 1 ? $"{d.PackageName} (depuis l'étape {d.StartStep})" : d.PackageName;
        DCreated.Text = Ui.LocalTime(d.CreatedUtc);
        DDuration.Text = Ui.Duration(d.CreatedUtc, d.EndedUtc);
        DLaunchedBy.Text = d.LaunchedBy ?? "—";
        DRunAs.Text = d.RunAsMode switch
        {
            RunAsMode.DeployUser => d.RunAs ?? "—",
            RunAsMode.LocalSystem => "Système local",
            _ => "Utilisateur connecté",
        }
        // L'identifiant sert aussi à copier les fichiers et à créer le service, même sans tourner sous ce compte.
        + (d.RunAsMode != RunAsMode.DeployUser && d.CredentialId != null ? $" (accès : {d.RunAs})" : "")
        + (d.CopyMode == CopyMode.Pull ? " · pull" : "");
        var failed = d.Targets.Count(t => t.Status == TargetStatus.Failed);
        DTotal.Text = $"{d.Targets.Count} ";
        DFailed.Text = $"{failed} ";
        RetryLink.IsEnabled = failed > 0;
        var running = IsActive(d);
        DCounts.Text = running
            ? $"{d.Targets.Count(t => t.Status == TargetStatus.Running)} en cours · {d.Targets.Count(t => t.Status == TargetStatus.Queued)} en attente"
            : "";
        DCounts.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        ApplyTargetFilter();
    }

    private void ApplyTargetFilter()
    {
        if (Selected is not { } d) return;
        var filter = TargetFilter.Text.Trim();

        var rows = d.Targets.Select(t => new TargetRow(
                t, d.Id, t.Computer, t.Status.ToString(),
                $"{t.Steps.Count(s => s.State == "success" || s.State == "skipped" && t.Status == TargetStatus.Successful)} sur {d.Snapshot.Steps.Count - t.StartStep + 1}",
                t.Status == TargetStatus.Running ? t.Stage ?? "" : t.Error ?? "",
                Ui.LocalTime(t.StartedUtc), Ui.Duration(t.StartedUtc, t.EndedUtc)))
            .Where(r => filter.Length == 0 || Matches(filter, r.Computer, Ui.StatusText(r.Status), r.Detail))
            .ToList();

        var selectedIds = SelectedTargets.Select(t => t.Id).ToHashSet();
        TargetsGrid.ItemsSource = rows;
        var reselect = rows.Where(r => selectedIds.Contains(r.Target.Id)).ToList();
        if (reselect.Count == 0 && rows.Count > 0) reselect.Add(rows[0]);
        foreach (var row in reselect) TargetsGrid.SelectedItems.Add(row);
        TargetsTitle.Text = rows.Count == 1 ? "1 poste" : $"{rows.Count} postes";
        ShowTarget();
    }

    // ---------- Carte du poste ----------

    private void ShowTarget()
    {
        var row = TargetsGrid.SelectedItem as TargetRow;
        NoTarget.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
        TargetDetails.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null) return;

        var t = row.Target;
        TName.Text = t.Computer;
        TStatus.Text = Ui.StatusText(row.Status);
        TStatus.Foreground = Ui.StatusBrush(row.Status);
        TSteps.Text = row.Steps + " étapes";
        TCurrent.Text = t.Status == TargetStatus.Running ? t.Stage ?? "" : "";
        TCurrentRow.Height = t.Status == TargetStatus.Running ? GridLength.Auto : new GridLength(0);
        TStarted.Text = string.IsNullOrEmpty(row.Started) ? "—" : row.Started;
        TEnded.Text = t.EndedUtc == null ? "—" : Ui.LocalTime(t.EndedUtc);
        TDuration.Text = string.IsNullOrEmpty(row.Duration) ? "—" : row.Duration;
        TError.Text = t.Error ?? "";
        TErrorTitle.Visibility = TError.Visibility = string.IsNullOrEmpty(t.Error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenSteps()
    {
        if (TargetsGrid.SelectedItem is not TargetRow row || Selected is not { } d) return;
        new StepsWindow(d, row.Target) { Owner = Owner }.Show();
    }

    // ---------- Événements ----------

    private void DeploymentFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyDeploymentFilter();
    private void TargetFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyTargetFilter();

    private void DeploymentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == DeploymentsGrid) ShowDeployment();
    }

    private void TargetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == TargetsGrid) ShowTarget();
    }

    private void TargetsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSteps();
    private void ShowSteps_Click(object sender, RoutedEventArgs e) => OpenSteps();
    private void RedeployAll_Click(object sender, RoutedEventArgs e) => Redeploy();
    private void RedeployFailed_Click(object sender, RoutedEventArgs e) => RedeployFailed();

    private void RedeployTarget_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } d && TargetsGrid.SelectedItem is TargetRow row) DeployRequested?.Invoke(d.PackageId, Fresh([row.Computer]));
    }

    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(DeploymentsGrid);
        Ui.SelectOnRightClick(TargetsGrid);

        Ui.ContextMenu(DeploymentsGrid, () => Selected == null
            ? []
            :
            [
                new("Annuler le déploiement", AbortDeployment, CanAbort),
                Ui.Separator,
                new("Redéployer", Redeploy, CanRedeploy, "Ctrl+R"),
                new("Redéployer sur les postes en échec", RedeployFailed, CanRedeployFailed, "Ctrl+Maj+R"),
                new("Redéployer depuis l'étape en échec", RedeployFromFailedStep, CanRedeployFailed),
                Ui.Separator,
                new("Supprimer", DeleteSelected, CanDelete, "Suppr"),
            ]);

        Ui.ContextMenu(TargetsGrid, () =>
        {
            if (Selected is not { } d || SelectedTargets is not { Count: > 0 } targets) return [];
            var failed = targets.Where(t => t.Status == TargetStatus.Failed).ToList();
            var active = targets.Where(IsActive).ToList();
            var names = targets.Select(t => t.Computer).ToList();
            return
            [
                new("Annuler", () => _ = AbortAsync(d, active), active.Count > 0),
                Ui.Separator,
                new("Redéployer", () => DeployRequested?.Invoke(d.PackageId, Fresh(names)), Gesture: "Ctrl+R"),
                new("Redéployer depuis l'étape en échec", () => RedeployFromFailedStep(failed), failed.Count > 0),
                Ui.Separator,
                new("Détail des étapes…", OpenSteps, targets.Count == 1),
                new("Copier le nom du poste", () => Clipboard.SetText(string.Join(Environment.NewLine, names))),
                new("Copier l'erreur", () => Clipboard.SetText(string.Join(Environment.NewLine, targets.Select(t => t.Error).Where(e => !string.IsNullOrEmpty(e)))),
                    targets.Any(t => !string.IsNullOrEmpty(t.Error))),
            ];
        });

        // Raccourcis propres au panneau (les autres sont gérés par la fenêtre via le menu Édition).
        TargetsGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control && Selected is { } d && SelectedTargets.Count > 0)
            {
                DeployRequested?.Invoke(d.PackageId, Fresh(SelectedTargets.Select(t => t.Computer)));
                e.Handled = true;
            }
        };
    }
}
