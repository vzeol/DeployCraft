using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

public record PackageItem(Package Package, string Display);

public record StepChoice(int Number, string Text);

/// <summary>Un poste cible et l'étape du premier paquet à laquelle il commence.</summary>
public class TargetItem(string computer, int startStep, DeployWindow window) : INotifyPropertyChanged
{
    private int _startStep = startStep;

    public string Computer { get; } = computer;

    public int StartStep
    {
        get => _startStep;
        set
        {
            _startStep = value;
            PropertyChanged?.Invoke(this, new(nameof(StartStep)));
            PropertyChanged?.Invoke(this, new(nameof(StepText)));
        }
    }

    public string StepText => window.StepChoices.FirstOrDefault(c => c.Number == StartStep)?.Text ?? StartStep.ToString();

    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(StepText)));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// « Déployer une fois », en deux volets : à gauche les paquets et les cibles
/// (avec l'étape de départ par poste), à droite les options du déploiement.
/// </summary>
public partial class DeployWindow : Window
{
    private readonly long? _initialPackageId;
    private readonly bool _hasInitialTargets;
    private readonly ObservableCollection<PackageItem> _packages = [];
    private readonly ObservableCollection<TargetItem> _targets = [];
    private List<Package> _library = [];
    /// <summary>Préférences : valeur des paquets laissés « par défaut ».</summary>
    private DeploymentDefaults _defaults = new();

    public ObservableCollection<StepChoice> StepChoices { get; } = [];

    /// <summary>Déploiements créés (un par paquet).</summary>
    public List<Deployment> Deployments { get; private set; } = [];

    public DeployWindow(long? packageId, IEnumerable<TargetRequest> targets)
    {
        InitializeComponent();
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
                // Cibles mémorisées : proposées seulement si la fenêtre n'a pas été ouverte avec des postes.
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

    // ---------- Paquets ----------

    private void AddPackage(Package package)
    {
        if (_packages.Any(p => p.Package.Id == package.Id)) return;
        _packages.Add(new PackageItem(package, ""));
        UpdatePackages();
    }

    /// <summary>Renumérote la liste, recalcule les étapes proposées et les libellés « paramètre du paquet ».</summary>
    private void UpdatePackages()
    {
        var selected = (PackagesList.SelectedItem as PackageItem)?.Package.Id;
        var items = _packages.Select((p, i) => new PackageItem(p.Package,
            $"{i + 1}. {p.Package.Name} ({p.Package.Steps.Count} étape{(p.Package.Steps.Count > 1 ? "s" : "")})")).ToList();
        _packages.Clear();
        foreach (var item in items) _packages.Add(item);
        PackagesList.SelectedItem = _packages.FirstOrDefault(p => p.Package.Id == selected);

        Title = _packages.Count == 1 ? $"Déployer une fois — {_packages[0].Package.Name}" : "Déployer une fois";
        PackagesHint.Visibility = _packages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // L'étape de départ porte sur le premier paquet.
        var first = _packages.FirstOrDefault()?.Package;
        StepChoices.Clear();
        if (first != null)
            for (var i = 0; i < first.Steps.Count; i++)
            {
                var name = first.Steps[i].Name;
                StepChoices.Add(new StepChoice(i + 1, string.IsNullOrWhiteSpace(name) ? $"{i + 1}" : $"{i + 1}. {name}"));
            }
        foreach (var t in _targets)
        {
            if (first != null && t.StartStep > first.Steps.Count) t.StartStep = 1;
            t.Refresh();
        }

        FillOptions();
        UpdateCount();
    }

    private void AddPackage_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddPackageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in _library.Where(p => _packages.All(x => x.Package.Id != p.Id)))
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) => AddPackage(p);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(aucun autre paquet)", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void MovePackage(int delta)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        var i = _packages.IndexOf(item);
        var j = i + delta;
        if (j < 0 || j >= _packages.Count) return;
        _packages.Move(i, j);
        UpdatePackages();
    }

    private void PackageUp_Click(object sender, RoutedEventArgs e) => MovePackage(-1);
    private void PackageDown_Click(object sender, RoutedEventArgs e) => MovePackage(1);

    private void RemovePackage_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not PackageItem item) return;
        _packages.Remove(item);
        UpdatePackages();
    }


    // ---------- Options ----------

    private bool _filling;

    private List<Credential> _credentials = [];

    private async Task FillCredentialsAsync()
    {
        _filling = true;
        _credentials = await CredentialsWindow.Fill(CredentialBox, CredentialsWindow.Chosen(CredentialBox));
        _filling = false;
        UpdateHints();
    }

    /// <summary>« Paramètre du paquet (…) » : la valeur commune aux paquets choisis, ou « selon chaque paquet ».</summary>
    private string PackageSetting<T>(Func<Package, T> get, Func<T, string> text)
    {
        var values = _packages.Select(p => get(p.Package)).Distinct().ToList();
        return values.Count == 1 ? $"Paramètre du paquet ({text(values[0])})" : "Paramètre de chaque paquet";
    }

    private void FillOptions()
    {
        _filling = true;
        var runAs = (RunAsBox.SelectedItem as ComboBoxItem)?.Tag;
        var copy = (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag;

        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.RunAs ?? _defaults.RunAs, Ui.RunAsText), Tag = null });
        foreach (var mode in Enum.GetValues<RunAsMode>()) RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode });
        RunAsBox.SelectedItem = RunAsBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, runAs)) ?? RunAsBox.Items[0];

        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = PackageSetting(p => p.CopyMode ?? _defaults.CopyMode, Ui.CopyModeText), Tag = null });
        foreach (var mode in Enum.GetValues<CopyMode>()) CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode });
        CopyModeBox.SelectedItem = CopyModeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, copy)) ?? CopyModeBox.Items[0];

        _filling = false;
        UpdateHints();
    }

    private RunAsMode? ChosenRunAs => (RunAsBox.SelectedItem as ComboBoxItem)?.Tag as RunAsMode?;
    private CopyMode? ChosenCopyMode => (CopyModeBox.SelectedItem as ComboBoxItem)?.Tag as CopyMode?;

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) UpdateHints();
    }

    private void UpdateHints()
    {
        var modes = ChosenRunAs is { } r ? [r] : _packages.Select(p => p.Package.RunAs ?? _defaults.RunAs).Distinct().ToList();
        RunAsHint.Text = modes.Count != 1 ? "Chaque paquet garde son propre réglage." : modes[0] switch
        {
            RunAsMode.DeployUser when _credentials.Count == 0 =>
                "Aucun identifiant enregistré : le service tournera en Système local.",
            RunAsMode.DeployUser => "Le service temporaire tourne sous l'identifiant choisi (accès réseau avec ce compte).",
            RunAsMode.LocalSystem => "Le service temporaire tourne en Système local (accès réseau avec le compte ordinateur du poste).",
            _ => "Chaque étape s'ouvre dans la session de l'utilisateur connecté ; échec si personne n'est connecté.",
        };

        var copies = ChosenCopyMode is { } c ? [c] : _packages.Select(p => p.Package.CopyMode ?? _defaults.CopyMode).Distinct().ToList();
        CopyModeHint.Text = copies.Count != 1 ? "Chaque paquet garde son propre réglage." : copies[0] == CopyMode.Push
            ? "Le serveur copie les fichiers d'installation sur chaque poste."
            : "Chaque poste copie lui-même les fichiers depuis le partage (chemins \\\\serveur\\… seulement ; sinon push).";
    }

    private async void ManageCredentials_Click(object sender, RoutedEventArgs e)
    {
        new CredentialsWindow { Owner = this }.ShowDialog();
        try
        {
            await FillCredentialsAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Cibles ----------

    private void AddTarget(string name, int startStep)
    {
        var clean = name.Trim().TrimStart('\\');
        if (clean.Length == 0 || _targets.Any(t => t.Computer.Equals(clean, StringComparison.OrdinalIgnoreCase))) return;
        _targets.Add(new TargetItem(clean, startStep, this));
    }

    private void AddNames(string text)
    {
        foreach (var name in text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) AddTarget(name, 1);
        UpdateCount();
    }

    private void PasteNames()
    {
        if (Clipboard.ContainsText()) AddNames(Clipboard.GetText());
    }

    private void SetStartStep(int step)
    {
        foreach (var t in TargetsGrid.SelectedItems.Cast<TargetItem>()) t.StartStep = step;
    }

    private void UpdateCount()
    {
        var targets = _targets.Count == 0 ? "aucune cible" : $"{_targets.Count} poste(s)";
        CountText.Text = _packages.Count > 1 ? $"{_packages.Count} paquets · {targets}" : char.ToUpper(targets[0]) + targets[1..];
        DeployButton.IsEnabled = _targets.Count > 0 && _packages.Count > 0;
        ScheduleButton.IsEnabled = _packages.Count > 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddNames(ComputerBox.Text);
        ComputerBox.Clear();
        ComputerBox.Focus();
    }

    private void ComputerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Add_Click(sender, e);
        e.Handled = true;
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        ChooseButton.ContextMenu.PlacementTarget = ChooseButton;
        ChooseButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ChooseButton.ContextMenu.IsOpen = true;
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteNames();

    private void Ad_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(_targets.Select(t => t.Computer).ToList()) { Owner = this };
        if (picker.ShowDialog() != true) return;
        // La sélection AD remplace les postes AD précédents mais garde les saisies manuelles.
        foreach (var removed in picker.Unchecked)
            if (_targets.FirstOrDefault(t => t.Computer.Equals(removed, StringComparison.OrdinalIgnoreCase)) is { } item) _targets.Remove(item);
        AddNames(string.Join(" ", picker.Selected));
    }

    private void TextFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Liste de postes", Filter = "Fichiers texte (*.txt;*.csv)|*.txt;*.csv|Tous les fichiers|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AddNames(File.ReadAllText(dialog.FileName));
        }
        catch (IOException ex) { Ui.Error(this, ex); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TargetsGrid.SelectedItems.Cast<TargetItem>().ToList()) _targets.Remove(item);
        UpdateCount();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        UpdateCount();
    }

    private void Targets_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is ComboBox or ComboBoxItem) return; // choix de l'étape en cours
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteNames(); e.Handled = true; }
    }

    // ---------- Lancement ----------

    /// <summary>Planification à créer à partir de cette fenêtre (bouton « Planifier… »).</summary>
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
