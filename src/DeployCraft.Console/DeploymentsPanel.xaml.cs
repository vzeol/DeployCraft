using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Master/detail view of the deployments: deployments grid + details card,
/// then grid of computers of the selected deployment + computer card.
/// </summary>
public partial class DeploymentsPanel : UserControl
{
    private readonly DeploymentDetails _details = new(Ui.LocalTime, Ui.Duration);
    private readonly DeploymentList _list = new(Ui.LocalTime, Ui.Duration);
    private readonly DeploymentActions _actions = new(Api.Abort, Api.DeleteDeployment);

    /// <summary>Deployment request (package, targets pre-filled with their start step).</summary>
    public event Action<long, IReadOnlyList<TargetRequest>>? DeployRequested;
    /// <summary>New schedule pre-filled from a deployment, to open in its window.</summary>
    public event Action<Schedule>? ScheduleRequested;
    /// <summary>The history changed (deletion, cancellation, direct launch): the window must refresh.</summary>
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

    // ---------- Actions (Edit menu bar and right-click) ----------

    /// <summary>The computers grid has focus: Ctrl+R then targets the selected computers.</summary>
    public bool TargetsFocused => TargetsGrid.IsKeyboardFocusWithin;

    public bool CanAbort => DeploymentActions.CanAbort(Selected);
    public bool CanRedeploy => DeploymentActions.CanRedeploy(Selected);
    public bool CanRedeployFailed => DeploymentActions.CanRedeployFailed(Selected);
    public bool CanDelete => DeploymentActions.CanDelete(Selected);

    public void AbortDeployment()
    {
        if (Selected is { } d) _ = AbortAsync(d, null);
    }

    public void Redeploy()
    {
        if (Selected is { } d) DeployRequested?.Invoke(d.PackageId, DeploymentActions.Fresh(d.Targets.Select(t => t.Computer)));
    }

    public void RedeployFailed()
    {
        if (Selected is { } d)
            DeployRequested?.Invoke(d.PackageId, DeploymentActions.Fresh(DeploymentActions.Failed(d.Targets).Select(t => t.Computer)));
    }

    public void RedeployFromFailedStep() => RedeployFromFailedStep(Selected is { } d ? DeploymentActions.Failed(d.Targets) : []);

    public void DeleteSelected() => _ = DeleteSelectedAsync();

    /// <summary>A single deployment: each computer resumes at its own failed step.</summary>
    private void RedeployFromFailedStep(List<DeploymentTarget> targets)
    {
        if (Selected is not { } d || targets.Count == 0) return;
        DeployRequested?.Invoke(d.PackageId, DeploymentActions.Resume(targets));
    }

    private async Task AbortAsync(Deployment d, List<DeploymentTarget>? targets)
    {
        try
        {
            if (await _actions.AbortAsync(d, targets, text => MessageDialog.Show(Owner!, text,
                    L.Get("DeploymentsPanel_Cancel"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes))
                Changed?.Invoke();
        }
        catch (ApiException ex) { Ui.Error(Owner, ex); }
    }

    private async Task DeleteSelectedAsync()
    {
        try
        {
            if (await _actions.DeleteAsync(Selected, text => MessageDialog.Show(Owner!, text,
                    L.Get("DeploymentsPanel_Delete"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))
                Changed?.Invoke();
        }
        catch (ApiException ex) { Ui.Error(Owner, ex); }
    }

    // ---------- Data ----------

    public void SetDeployments(IEnumerable<Deployment> deployments)
    {
        _list.SetDeployments(deployments);
        ApplyDeploymentFilter();
    }

    /// <summary>Selects a deployment (possibly at the next refresh if it is not listed yet).</summary>
    public void Select(long id)
    {
        _list.Select(id);
        ApplyDeploymentFilter();
    }

    private void ApplyDeploymentFilter()
    {
        var view = _list.Rows(DeploymentFilter.Text, (DeploymentsGrid.SelectedItem as DeploymentRow)?.Id);
        DeploymentsGrid.ItemsSource = view.Rows;
        DeploymentsGrid.SelectedItem = view.Selected;
        if (view.Selected != null) DeploymentsGrid.ScrollIntoView(view.Selected);
        DeploymentsTitle.Text = view.Title;
        ShowDeployment();
    }

    // ---------- Deployment card + computers grid ----------

    private void ShowDeployment()
    {
        var d = Selected;
        NoDeployment.Visibility = d == null ? Visibility.Visible : Visibility.Collapsed;
        DeploymentDetails.Visibility = d == null ? Visibility.Collapsed : Visibility.Visible;
        if (d == null)
        {
            TargetsGrid.ItemsSource = null;
            TargetsTitle.Text = L.Get("DeploymentsPanel_Computers");
            ShowTarget();
            return;
        }

        var card = _details.Deployment(d);
        DStatusIcon.Text = Ui.StatusIcon(card.Status);
        DStatusIcon.Foreground = DStatus.Foreground = Ui.StatusBrush(card.Status);
        DTitle.Text = card.Title;
        DStatus.Text = card.StatusText;
        DPackage.Text = card.Package;
        DCreated.Text = card.Created;
        DDuration.Text = card.Duration;
        DLaunchedBy.Text = card.LaunchedBy;
        DRunAs.Text = card.RunAs;
        DTotal.Text = card.Total;
        DFailed.Text = card.Failed;
        DCounts.Text = card.Counts;
        RetryLink.IsEnabled = card.CanRetry;
        DCounts.Visibility = card.Running ? Visibility.Visible : Visibility.Collapsed;

        ApplyTargetFilter();
    }

    private void ApplyTargetFilter()
    {
        if (Selected is not { } d) return;
        var view = _list.Targets(d, TargetFilter.Text, SelectedTargets.Select(t => t.Id));
        TargetsGrid.ItemsSource = view.Rows;
        foreach (var row in view.Selected) TargetsGrid.SelectedItems.Add(row);
        TargetsTitle.Text = view.Title;
        ShowTarget();
    }

    // ---------- Computer card ----------

    private void ShowTarget()
    {
        var row = TargetsGrid.SelectedItem as TargetRow;
        NoTarget.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
        TargetDetails.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null) return;

        var card = _details.Target(row);
        TName.Text = card.Name;
        TStatus.Text = card.StatusText;
        TSteps.Text = card.Steps;
        TCurrent.Text = card.Current;
        TStarted.Text = card.Started;
        TEnded.Text = card.Ended;
        TDuration.Text = card.Duration;
        TError.Text = card.Error;
        TErrorTitle.Text = card.ErrorTitle;
        TStatus.Foreground = Ui.StatusBrush(card.Status);
        TCurrentRow.Height = card.Running ? GridLength.Auto : new GridLength(0);
        System.Windows.Automation.AutomationProperties.SetName(TErrorTitle, TErrorTitle.Text);
        TError.SetResourceReference(TextBlock.ForegroundProperty, card.Failed ? "KoBrush" : "MutedBrush");
        TErrorTitle.Visibility = TError.Visibility = card.HasError ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSteps()
    {
        if (TargetsGrid.SelectedItem is not TargetRow row || Selected is not { } d) return;
        new StepsWindow(d, row.Target) { Owner = Owner }.Show();
    }

    // ---------- Events ----------

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
        if (Selected is { } d && TargetsGrid.SelectedItem is TargetRow row) DeployRequested?.Invoke(d.PackageId, DeploymentActions.Fresh([row.Computer]));
    }

    // ---------- Right-click menus ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(DeploymentsGrid);
        Ui.SelectOnRightClick(TargetsGrid);

        Ui.ContextMenu(DeploymentsGrid, () => Selected == null
            ? []
            :
            [
                new(L.Get("DeploymentsPanel_CtxCancelDeployment"), AbortDeployment, CanAbort),
                Ui.Separator,
                new(L.Get("DeploymentsPanel_Redeploy"), Redeploy, CanRedeploy, "Ctrl+R"),
                new(L.Get("DeploymentsPanel_CtxRedeployFailed"), RedeployFailed, CanRedeployFailed, L.Get("DeploymentsPanel_KeyCtrlShiftR")),
                new(L.Get("DeploymentsPanel_CtxRedeployFromStep"), RedeployFromFailedStep, CanRedeployFailed),
                Ui.Separator,
                new(L.Get("DeploymentsPanel_CtxNewScheduleFrom"), () => ScheduleRequested?.Invoke(Schedule.From(Selected!))),
                Ui.Separator,
                new(L.Get("DeploymentsPanel_Delete"), DeleteSelected, CanDelete, L.Get("DeploymentsPanel_KeyDelete")),
            ]);

        Ui.ContextMenu(TargetsGrid, () =>
        {
            if (Selected is not { } d || SelectedTargets is not { Count: > 0 } targets) return [];
            var actions = DeploymentActions.Targets(targets);
            return
            [
                new(L.Get("DeploymentsPanel_Cancel"), () => _ = AbortAsync(d, actions.Active), actions.Active.Count > 0),
                Ui.Separator,
                new(L.Get("DeploymentsPanel_Redeploy"), () => DeployRequested?.Invoke(d.PackageId, actions.Fresh), Gesture: "Ctrl+R"),
                new(L.Get("DeploymentsPanel_CtxRedeployFromStep"), () => RedeployFromFailedStep(actions.Failed), actions.Failed.Count > 0),
                Ui.Separator,
                new(L.Get("DeploymentsPanel_CtxStepDetails"), OpenSteps, actions.CanOpenSteps),
                new(L.Get("DeploymentsPanel_CtxCopyName"), () => Clipboard.SetText(actions.Names)),
                new(L.Get("DeploymentsPanel_CtxCopyError"), () => Clipboard.SetText(actions.Errors), actions.CanCopyError),
            ];
        });

        // Shortcuts specific to the panel (the others are handled by the window via the Edit menu).
        TargetsGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control && Selected is { } d && SelectedTargets.Count > 0)
            {
                DeployRequested?.Invoke(d.PackageId, DeploymentActions.Fresh(SelectedTargets.Select(t => t.Computer)));
                e.Handled = true;
            }
        };
    }
}
