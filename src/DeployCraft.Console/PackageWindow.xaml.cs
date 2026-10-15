using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Éditeur de paquet : propriétés et étapes à gauche, détail de l'élément sélectionné à droite.</summary>
public partial class PackageWindow : Window
{
    /// <summary>Format presse-papiers des étapes : on peut coller dans un autre paquet, même dans une autre fenêtre.</summary>
    private const string StepFormat = "DeployCraft.Step";

    private readonly TreeViewItem _propertiesNode;
    private readonly TreeViewItem _stepsNode;
    private string _savedSnapshot;
    private bool _loadingStep;
    private bool _loadingOptions;
    /// <summary>Valeurs des Préférences, affichées pour les réglages laissés « par défaut ».</summary>
    private DeploymentDefaults _defaults = new();
    /// <summary>Paquets du service : choix d'un paquet imbriqué et résumé des étapes.</summary>
    private List<Package> _library = [];

    /// <summary>Paquet tel qu'enregistré en dernier (valide si <see cref="Saved"/>).</summary>
    public Package Package { get; private set; }
    public bool Saved { get; private set; }

    public PackageWindow(Package package)
    {
        InitializeComponent();
        Package = Clone(package);
        _savedSnapshot = Snapshot();

        NameBox.Text = Package.Name;
        VersionBox.Text = Package.Version;
        DescriptionBox.Text = Package.Description;
        PackageConditions.Load(Package.Conditions, forPackage: true);
        FillOptions();

        _propertiesNode = new TreeViewItem { Header = Ui.Header(Ui.IconProperties, "Propriétés"), Tag = "properties" };
        _stepsNode = new TreeViewItem { Header = Ui.Header(Ui.IconSteps, "Étapes"), IsExpanded = true };
        Tree.Items.Add(_propertiesNode);
        Tree.Items.Add(_stepsNode);
        RebuildSteps(select: null);
        _propertiesNode.IsSelected = true;

        UpdateTitle();
        SetupContextMenus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, e); e.Handled = true; }
        };
        Activated += (_, _) => UpdateButtons();
        Loaded += async (_, _) =>
        {
            if (Package.Id == 0) NameBox.Focus();
            try
            {
                _defaults = await Api.Defaults();
                _library = await Api.Packages();
                FillOptions();
                if (SelectedStep is { } step) ShowStep(step);
            }
            catch (ApiException) { /* les libellés gardent les valeurs d'usine */ }
        };
    }

    public static PackageStep NewStep(StepType type) => new()
    {
        Type = type,
        SuccessCodes = type == StepType.Install ? Defaults.SuccessCodes : "0",
    };

    private PackageStep? SelectedStep => (Tree.SelectedItem as TreeViewItem)?.Tag as PackageStep;

    // ---------- Arbre ----------

    private void RebuildSteps(PackageStep? select)
    {
        _stepsNode.Items.Clear();
        for (var i = 0; i < Package.Steps.Count; i++)
        {
            var step = Package.Steps[i];
            var node = new TreeViewItem { Header = StepHeader(step, i), Tag = step };
            _stepsNode.Items.Add(node);
            if (step == select) node.IsSelected = true;
        }
        UpdateButtons();
    }

    private static StackPanel StepHeader(PackageStep step, int index)
    {
        var header = Ui.Header(Ui.StepIcon(step.Type), (string.IsNullOrWhiteSpace(step.Name)
            ? $"Étape {index + 1} ({MainWindow.StepTypeText(step.Type)})"
            : $"{index + 1}. {step.Name}") + (step.Enabled ? "" : " (désactivée)"));
        if (!step.Enabled) header.Opacity = 0.55;
        return header;
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SelectedStep is { } step) ShowStep(step);
        else if (Tree.SelectedItem == _stepsNode) ShowStepsOverview();
        else ShowOnly(PropertiesView);
        UpdateButtons();
    }

    private void ShowOnly(FrameworkElement view)
    {
        foreach (var v in new FrameworkElement[] { PropertiesView, StepsOverview, StepView })
            v.Visibility = v == view ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStepsOverview()
    {
        StepsGrid.ItemsSource = Package.Steps.Select((s, i) => new PackageStepRow(i + 1, MainWindow.StepName(s), MainWindow.StepTypeText(s.Type),
            s.Conditions.IsEmpty ? "" : "Oui",
            MainWindow.StepDetails(s, FindPackage))).ToList();
        ShowOnly(StepsOverview);
    }

    private void StepsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StepsGrid.SelectedItem is PackageStepRow row) SelectStep(Package.Steps[row.Number - 1]);
    }

    private void SelectStep(PackageStep step)
    {
        foreach (TreeViewItem node in _stepsNode.Items)
            if (node.Tag == step) { node.IsSelected = true; node.BringIntoView(); }
    }

    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(Tree);
        Ui.SelectOnRightClick(StepsGrid);

        Ui.MenuEntry[] AddEntries() =>
        [
            new("Nouvelle étape : Installer", () => AddInstall_Click(this, new())),
            new("Nouvelle étape : Commande", () => AddCommand_Click(this, new())),
            new("Nouvelle étape : PowerShell", () => AddPowerShell_Click(this, new())),
            new("Nouvelle étape : Pause", () => AddPause_Click(this, new())),
            new("Nouvelle étape : Paquet imbriqué", () => AddNested_Click(this, new())),
            Ui.Separator,
            new("Coller", () => Paste_Click(this, new()), CanPaste, "Ctrl+V"),
        ];

        IEnumerable<Ui.MenuEntry> StepEntries(PackageStep step)
        {
            return
            [
                .. AddEntries(),
                new("Couper", () => { SelectStep(step); Cut_Click(this, new()); }, Gesture: "Ctrl+X"),
                new("Copier", () => { SelectStep(step); Copy_Click(this, new()); }, Gesture: "Ctrl+C"),
                new("Dupliquer", () => { SelectStep(step); Duplicate_Click(this, new()); }),
                new(step.Enabled ? "Désactiver" : "Activer", () => SetEnabled(step, !step.Enabled)),
                Ui.Separator,
                new("Monter", () => { SelectStep(step); Move(-1); }, CanMove(step, -1)),
                new("Descendre", () => { SelectStep(step); Move(1); }, CanMove(step, 1)),
                Ui.Separator,
                new("Supprimer", () => { SelectStep(step); DeleteStep_Click(this, new()); }, Gesture: "Suppr"),
            ];
        }

        Ui.ContextMenu(Tree, () => SelectedStep is { } step ? StepEntries(step)
            : Tree.SelectedItem == _stepsNode ? AddEntries()
            : []);

        Ui.ContextMenu(StepsGrid, () => StepsGrid.SelectedItem is PackageStepRow row
            ? [new("Modifier", () => SelectStep(Package.Steps[row.Number - 1])), Ui.Separator, .. StepEntries(Package.Steps[row.Number - 1])]
            : AddEntries());

        Tree.KeyDown += (_, e) =>
        {
            var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
            if (e.Key == Key.Delete && SelectedStep != null) DeleteStep_Click(this, e);
            else if (ctrl && e.Key == Key.C && SelectedStep != null) Copy_Click(this, e);
            else if (ctrl && e.Key == Key.X && SelectedStep != null) Cut_Click(this, e);
            else if (ctrl && e.Key == Key.V && CanPaste) Paste_Click(this, e);
            else return;
            e.Handled = true;
        };
    }

    private void UpdateButtons()
    {
        var step = SelectedStep;
        UpButton.IsEnabled = CanMove(step, -1);
        DownButton.IsEnabled = CanMove(step, 1);
        CutButton.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = step != null;
        PasteButton.IsEnabled = CanPaste;
    }

    // ---------- Détail d'une étape ----------

    private void ShowStep(PackageStep step)
    {
        _loadingStep = true;
        StepView.DataContext = step;
        ShowOnly(StepView);

        var install = step.Type == StepType.Install;
        var pause = step.Type == StepType.Pause;
        var nested = step.Type == StepType.Nested;
        foreach (UIElement el in new UIElement[] { FileLabel, FileBox, BrowseButton, FileHint, ArgsLabel, ArgsBox, SilentHint, IncludeDirBox })
            el.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        CommandLabel.Visibility = CommandBox.Visibility = install || pause || nested ? Visibility.Collapsed : Visibility.Visible;
        foreach (UIElement el in new UIElement[] { NestedLabel, NestedBox, NestedHint })
            el.Visibility = nested ? Visibility.Visible : Visibility.Collapsed;
        if (nested)
        {
            NestedBox.ItemsSource = _library.Where(p => p.Id != Package.Id).OrderBy(p => p.Name).ToList();
            NestedBox.SelectedValue = step.NestedPackageId;
        }
        foreach (UIElement el in new UIElement[] { PauseLabel, PauseBox, PauseHint })
            el.Visibility = pause ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement el in new UIElement[] { CodesLabel, CodesBox, CodesHint })
            el.Visibility = pause || nested ? Visibility.Collapsed : Visibility.Visible;
        CommandLabel.Text = step.Type == StepType.PowerShell ? "Script" : "Commande";
        CommandBox.MinHeight = step.Type == StepType.PowerShell ? 220 : 26;
        CommandBox.TextWrapping = step.Type == StepType.PowerShell ? TextWrapping.NoWrap : TextWrapping.Wrap;

        StepConditions.Load(step.Conditions, forPackage: false);
        StepTimeoutHint.Text = pause ? "Sans effet sur une pause : elle dure toujours la durée choisie."
            : nested ? "Sans effet : chaque étape du paquet imbriqué garde son délai (sinon celui du paquet imbriqué)."
            : $"Vide : délai du paquet ({PackageTimeout} min). Au-delà, le processus est arrêté et l'étape échoue.";
        StepTimeoutBox.IsEnabled = ErrorModeBox.IsEnabled = !nested;
        ErrorModeBox.SelectedIndex = (int)step.ErrorMode;
        UpdateErrorModeHint(step);
        UpdateSilentHint();
        _loadingStep = false;
    }

    private void StepName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Tree.SelectedItem is TreeViewItem { Tag: PackageStep step } node)
            node.Header = StepHeader(step, Package.Steps.IndexOf(step));
    }

    private void InstallFields_Changed(object sender, TextChangedEventArgs e) => UpdateSilentHint();

    /// <summary>Un .exe sans paramètre silencieux attend un clic qui n'arrivera jamais : on prévient.</summary>
    private void UpdateSilentHint()
    {
        if (SelectedStep is not { Type: StepType.Install } step) return;
        var ext = Path.GetExtension(step.File ?? "").ToLowerInvariant();
        if (ext == ".exe" && string.IsNullOrWhiteSpace(step.Arguments))
        {
            SilentHint.Foreground = (System.Windows.Media.Brush)FindResource("KoBrush");
            SilentHint.Text = "⚠ Aucun paramètre silencieux : l'installation risque de bloquer jusqu'au délai max. " +
                              "Courants : /S (NSIS), /VERYSILENT /NORESTART (Inno Setup), /quiet /norestart, /s /v\"/qn\" (InstallShield).";
        }
        else
        {
            SilentHint.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            SilentHint.Text = ext switch
            {
                ".msi" or ".msp" => "MSI : /qn /norestart sont ajoutés automatiquement.",
                ".msu" => "MSU : /quiet /norestart sont ajoutés automatiquement.",
                _ => "",
            };
        }
    }

    private void ErrorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStep || SelectedStep is not { } step || ErrorModeBox.SelectedIndex < 0) return;
        step.ErrorMode = (ErrorMode)ErrorModeBox.SelectedIndex;
        UpdateErrorModeHint(step);
    }

    private void UpdateErrorModeHint(PackageStep step) => ErrorModeHint.Text = step.Type == StepType.Nested
        ? "Sans effet : chaque étape du paquet imbriqué garde son propre comportement en cas d'erreur."
        : step.ErrorMode switch
        {
            ErrorMode.Continue => "L'étape est notée en échec (le poste aussi), mais les étapes suivantes s'exécutent.",
            ErrorMode.StopSuccess => "Les étapes suivantes ne s'exécutent pas et le poste est compté réussi : " +
                                     "utile quand l'échec veut dire « rien à faire » sur ce poste.",
            _ => "Les étapes suivantes ne s'exécutent pas et le poste est en échec.",
        };

    private void StepEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep != null) StepName_TextChanged(this, null!);
    }

    private void SetEnabled(PackageStep step, bool enabled)
    {
        step.Enabled = enabled;
        foreach (TreeViewItem node in _stepsNode.Items)
            if (node.Tag == step) node.Header = StepHeader(step, Package.Steps.IndexOf(step));
        if (StepView.DataContext == step) StepEnabledBox.IsChecked = enabled;
        if (StepsOverview.Visibility == Visibility.Visible) ShowStepsOverview();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var dialog = new OpenFileDialog
        {
            Title = "Fichier d'installation",
            Filter = "Installeurs|*.exe;*.msi;*.msp;*.msu;*.ps1;*.bat;*.cmd|Tous les fichiers|*.*",
        };
        if (!string.IsNullOrEmpty(step.File) && Directory.Exists(Path.GetDirectoryName(step.File)))
            dialog.InitialDirectory = Path.GetDirectoryName(step.File);
        if (dialog.ShowDialog(this) != true) return;

        step.File = dialog.FileName;
        FileBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(step.Name))
        {
            step.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            StepView.DataContext = null;
            ShowStep(step);
            StepName_TextChanged(this, null!);
        }
    }

    // ---------- Propriétés ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Package.Name = NameBox.Text;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = string.IsNullOrWhiteSpace(Package.Name) ? "Nouveau paquet" : $"{Package.Name} — Paquet";

    /// <summary>Délai qu'auront les étapes sans délai propre.</summary>
    private int PackageTimeout => Package.TimeoutMinutes ?? _defaults.TimeoutMinutes;

    /// <summary>Options et Hors ligne : chaque liste commence par « Par défaut (valeur des Préférences) ».</summary>
    private void FillOptions()
    {
        _loadingOptions = true;
        RunAsBox.Items.Clear();
        RunAsBox.Items.Add(new ComboBoxItem { Content = $"Par défaut ({Ui.RunAsText(_defaults.RunAs)})", Tag = null, IsSelected = Package.RunAs == null });
        foreach (var mode in Enum.GetValues<RunAsMode>())
            RunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode, IsSelected = Package.RunAs == mode });
        CopyModeBox.Items.Clear();
        CopyModeBox.Items.Add(new ComboBoxItem { Content = $"Par défaut ({Ui.CopyModeText(_defaults.CopyMode)})", Tag = null, IsSelected = Package.CopyMode == null });
        foreach (var mode in Enum.GetValues<CopyMode>())
            CopyModeBox.Items.Add(new ComboBoxItem { Content = Ui.CopyModeText(mode), Tag = mode, IsSelected = Package.CopyMode == mode });
        TimeoutBox.Text = Package.TimeoutMinutes?.ToString() ?? "";
        TimeoutHint.Text = $"Pour les étapes sans délai propre. Vide : valeur des Préférences ({_defaults.TimeoutMinutes} min).";
        UpdateRunAsHint();

        OfflineDefaultBox.Content = "Utiliser les réglages des Préférences (Options › Préférences › Hors ligne)";
        OfflineDefaultBox.IsChecked = Package.Offline == null;
        PackageOffline.Load(Package.Offline ?? Clone(_defaults.Offline));
        PackageOffline.IsEnabled = Package.Offline != null;
        _loadingOptions = false;
    }

    private void UpdateRunAsHint() =>
        RunAsHint.Text = Ui.RunAsHint(Package.RunAs ?? _defaults.RunAs);

    private void RunAsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingOptions || RunAsBox.SelectedItem is not ComboBoxItem item) return;
        Package.RunAs = item.Tag as RunAsMode?;
        UpdateRunAsHint();
    }

    private void CopyModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingOptions && CopyModeBox.SelectedItem is ComboBoxItem item) Package.CopyMode = item.Tag as CopyMode?;
    }

    private void TimeoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingOptions) return;
        var text = TimeoutBox.Text.Trim();
        if (text.Length == 0) Package.TimeoutMinutes = null;
        else if (int.TryParse(text, out var minutes)) Package.TimeoutMinutes = minutes;
    }

    /// <summary>Case cochée : le paquet suit les Préférences ; décochée : il garde ses propres réglages, partis de ceux des Préférences.</summary>
    private void OfflineDefault_Click(object sender, RoutedEventArgs e)
    {
        Package.Offline = OfflineDefaultBox.IsChecked == true ? null : Clone(_defaults.Offline);
        PackageOffline.Load(Package.Offline ?? Clone(_defaults.Offline));
        PackageOffline.IsEnabled = Package.Offline != null;
    }

    // ---------- Barre d'outils ----------

    private void NewStep_Click(object sender, RoutedEventArgs e)
    {
        NewStepButton.ContextMenu.PlacementTarget = NewStepButton;
        NewStepButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        NewStepButton.ContextMenu.IsOpen = true;
    }

    private void AddInstall_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Install));
    private void AddCommand_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Command));
    private void AddPowerShell_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.PowerShell));
    private void AddPause_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Pause));
    private void AddNested_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Nested));

    private Package? FindPackage(long id) => _library.FirstOrDefault(p => p.Id == id);

    private void Nested_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStep || SelectedStep is not { Type: StepType.Nested } step || NestedBox.SelectedItem is not Package nested) return;
        step.NestedPackageId = nested.Id;
        StepName_TextChanged(this, null!);
    }

    private void AddStep(PackageStep step)
    {
        // Insérée après l'étape sélectionnée, sinon à la fin.
        var index = SelectedStep is { } current ? Package.Steps.IndexOf(current) + 1 : Package.Steps.Count;
        Package.Steps.Insert(index, step);
        RebuildSteps(step);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private bool CanMove(PackageStep? step, int delta)
    {
        var j = step == null ? -1 : Package.Steps.IndexOf(step) + delta;
        return step != null && j >= 0 && j < Package.Steps.Count;
    }

    private void Move(int delta)
    {
        if (SelectedStep is not { } step || !CanMove(step, delta)) return;
        var i = Package.Steps.IndexOf(step);
        var j = i + delta;
        (Package.Steps[i], Package.Steps[j]) = (Package.Steps[j], Package.Steps[i]);
        RebuildSteps(step);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var copy = Clone(step);
        copy.Name = string.IsNullOrWhiteSpace(step.Name) ? "" : step.Name + " (copie)";
        AddStep(copy);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyStep();

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (CopyStep()) DeleteStep_Click(sender, e);
    }

    private bool CopyStep()
    {
        if (SelectedStep is not { } step) return false;
        try
        {
            Clipboard.SetDataObject(new DataObject(StepFormat, JsonSerializer.Serialize(step, Api.Json)), copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageDialog.Show(this, "Presse-papiers occupé par une autre application : réessayez.", "Paquet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        UpdateButtons();
        return true;
    }

    private static bool CanPaste
    {
        get
        {
            try { return Clipboard.ContainsData(StepFormat); }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.GetData(StepFormat) is not string json) return;
            if (JsonSerializer.Deserialize<PackageStep>(json, Api.Json) is { } step) AddStep(step);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or JsonException)
        {
            MessageDialog.Show(this, "Impossible de coller l'étape : " + ex.Message, "Paquet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var i = Package.Steps.IndexOf(step);
        Package.Steps.Remove(step);
        RebuildSteps(Package.Steps.Count > 0 ? Package.Steps[Math.Min(i, Package.Steps.Count - 1)] : null);
        if (Package.Steps.Count == 0) _propertiesNode.IsSelected = true;
    }

    // ---------- Enregistrement ----------

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Champs lus à l'enregistrement (pas de liaison) : nom, version, description.</summary>
    private void ReadTextFields()
    {
        Package.Name = NameBox.Text.Trim();
        Package.Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? null : VersionBox.Text.Trim();
        Package.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
    }

    private async Task<bool> SaveAsync()
    {
        ReadTextFields();
        try
        {
            var selected = SelectedStep == null ? -1 : Package.Steps.IndexOf(SelectedStep);
            Package = await Api.SavePackage(Package);
            Saved = true;
            _savedSnapshot = Snapshot();
            PackageConditions.Load(Package.Conditions, forPackage: true);
            FillOptions();
            RebuildSteps(selected >= 0 && selected < Package.Steps.Count ? Package.Steps[selected] : null);
            if (selected < 0) _propertiesNode.IsSelected = true;
            UpdateTitle();
            return true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            return false;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        var name = Package.Name;
        ReadTextFields();
        Package.Name = name; // le nom n'est rogné qu'à l'enregistrement
        if (e.Cancel || Snapshot() == _savedSnapshot) return;

        var answer = MessageDialog.Show(this, "Enregistrer les modifications du paquet ?", "Paquet",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No) return;

        e.Cancel = true; // on rouvre la question après l'enregistrement asynchrone
        if (answer == MessageBoxResult.Yes && await SaveAsync())
        {
            _savedSnapshot = Snapshot();
            Close();
        }
    }

    private string Snapshot() => JsonSerializer.Serialize(Package, Api.Json);

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Api.Json), Api.Json)!;
}

/// <summary>Ligne de la vue d'ensemble des étapes.</summary>
public record PackageStepRow(int Number, string Name, string Type, string Conditions, string Details);
