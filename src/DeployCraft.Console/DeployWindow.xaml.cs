using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// "Déployer une fois" (Deploy once), in two panes: packages and targets on the left
/// (with the start step per computer), deployment options on the right.
/// </summary>
public partial class DeployWindow : Window
{
    private readonly long? _initialPackageId;
    private readonly bool _hasInitialTargets;
    private readonly ObservableCollection<PackageItem> _packages = [];
    private readonly ObservableCollection<TargetItem> _targets = [];
    private List<Package> _library = [];
    /// <summary>Preferences: value of packages left on "default".</summary>
    private DeploymentDefaults _defaults = new();

    public ObservableCollection<StepChoice> StepChoices { get; } = [];

    /// <summary>Deployments created (one per package).</summary>
    public List<Deployment> Deployments { get; private set; } = [];

    public DeployWindow(long? packageId, IEnumerable<TargetRequest> targets)
    {
        InitializeComponent();
        _nameCheck.Tick += NameCheck_Tick;
        _initialPackageId = packageId;
        PackagesList.ItemsSource = _packages;
        TargetsGrid.ItemsSource = _targets;
        foreach (var t in targets) AddTarget(t.Computer, t.StartStep);
        _hasInitialTargets = _targets.Count > 0;
        UpdateCount();

        Loaded += async (_, _) => await LoadAsync();

        Ui.SelectOnRightClick(TargetsGrid);
        Ui.ContextMenu(TargetsGrid, () =>
        [
            new("Coller une liste de postes", PasteNames, Clipboard.ContainsText(), "Ctrl+V"),
            new("Active Directory…", () => Ad_Click(this, new())),
            new("Fichier texte…", () => TextFile_Click(this, new())),
            Ui.Separator,
            new("Démarrer à l'étape 1", () => SetStartStep(1), TargetsGrid.SelectedItems.Count > 0),
            new("Démarrer à l'étape", () => { }, TargetsGrid.SelectedItems.Count > 0 && StepChoices.Count > 1,
                Children: [.. StepChoices.Select(c => new Ui.MenuEntry(c.Text, () => SetStartStep(c.Number)))]),
            Ui.Separator,
            new("Retirer", () => Remove_Click(this, new()), TargetsGrid.SelectedItems.Count > 0, "Suppr"),
            new("Tout retirer", () => Clear_Click(this, new()), _targets.Count > 0),
        ]);
    }

    private async Task LoadAsync()
    {
        try
        {
            _library = await Api.Packages();
            _defaults = await Api.Defaults();
            await FillCredentialsAsync();
            if (_initialPackageId is { } id && _library.FirstOrDefault(p => p.Id == id) is { } package)
            {
                AddPackage(package);
                // Remembered targets: offered only if the window was not opened with computers.
                if (!_hasInitialTargets)
                    foreach (var c in await Api.PackageTargets(id)) AddTarget(c, 1);
                RememberBox.IsChecked = !_hasInitialTargets && _targets.Count > 0;
            }
            UpdatePackages();
            UpdateCount();
            ComputerBox.Focus();
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
        }
    }

    // ---------- Launch ----------

    /// <summary>Schedule to create from this window ("Planifier…" button).</summary>
    public Schedule? ScheduleRequest { get; private set; }

    private void Schedule_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ComputerBox.Text)) AddNames(ComputerBox.Text);
        ScheduleRequest = new Schedule
        {
            Name = _packages.Count > 0 ? _packages[0].Package.Name : "",
            PackageIds = _packages.Select(p => p.Package.Id).ToList(),
            Targets = _targets.Select(t => t.Computer).ToList(),
            RunAs = ChosenRunAs,
            CopyMode = ChosenCopyMode,
            CredentialId = CredentialsWindow.Chosen(CredentialBox),
        };
        DialogResult = false;
    }

    private async void Deploy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ComputerBox.Text)) AddNames(ComputerBox.Text);
        TargetsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DeployButton.IsEnabled = false;
        try
        {
            var computers = _targets.Select(t => t.Computer).ToList();
            if (RememberBox.IsChecked == true)
                foreach (var p in _packages) await Api.SavePackageTargets(p.Package.Id, computers);

            Deployments = await Api.Deploy(
                _packages.Select(p => p.Package.Id),
                _targets.Select(t => new TargetRequest(t.Computer, t.StartStep)),
                new DeployOptions(ChosenRunAs, ChosenCopyMode, PriorityBox.IsChecked == true, CredentialsWindow.Chosen(CredentialBox)));
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            DeployButton.IsEnabled = true;
        }
    }
}
