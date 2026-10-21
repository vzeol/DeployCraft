using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;

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
        _warningCheck.Tick += WarningCheck_Tick;
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
        SetupStepDragDrop();
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
        var header = Ui.Header(Ui.StepIcon(step.Type), StepTexts.Header(step, index));
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
        StepsGrid.ItemsSource = StepRow.For(Package.Steps, FindPackage);
        ShowOnly(StepsOverview);
    }

    private void StepsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StepsGrid.SelectedItem is StepRow row) SelectStep(Package.Steps[row.Number - 1]);
    }

    private void SelectStep(PackageStep step)
    {
        foreach (TreeViewItem node in _stepsNode.Items)
            if (node.Tag == step) { node.IsSelected = true; node.BringIntoView(); }
    }

    // ---------- Détail d'une étape ----------

    private void ShowStep(PackageStep step)
    {
        _loadingStep = true;
        StepView.DataContext = step;
        ShowOnly(StepView);
        ShowStepFields(step);
        ShowStepOptions(step);
        UpdateErrorModeHint(step);
        UpdateSilentHint();
        UpdateStepWarnings();
        _warningCheck.Stop();
        _warningCheck.Start();
        _loadingStep = false;
    }

    /// <summary>Only the fields of this step type are shown.</summary>
    private void ShowStepFields(PackageStep step)
    {
        var f = StepTexts.Fields(step.Type);
        foreach (UIElement el in new UIElement[] { FileLabel, FileBox, BrowseButton, FileHint, ArgsLabel, ArgsBox, SilentHint, IncludeDirBox, InstallPanel })
            el.Visibility = Show(f.Install);
        FilesPanel.Visibility = Show(f.AdditionalFiles);
        if (f.Install) LoadInstallOptions(step);
        CopyPanel.Visibility = Show(f.Copy);
        RebootPanel.Visibility = Show(f.Reboot);
        MessagePanel.Visibility = Show(f.Message);
        CommandLabel.Visibility = CommandBox.Visibility = Show(f.Command);
        foreach (UIElement el in new UIElement[] { NestedLabel, NestedBox, NestedHint })
            el.Visibility = Show(f.Nested);
        if (f.Nested)
        {
            NestedBox.ItemsSource = _library.Where(p => p.Id != Package.Id).OrderBy(p => p.Name).ToList();
            NestedBox.SelectedValue = step.NestedPackageId;
        }
        foreach (UIElement el in new UIElement[] { PauseLabel, PauseBox, PauseHint })
            el.Visibility = Show(f.Pause);
        foreach (UIElement el in new UIElement[] { CodesLabel, CodesBox, CodesHint })
            el.Visibility = Show(f.SuccessCodes);
        CommandLabel.Text = step.Type == StepType.PowerShell ? "Script" : "Commande";
        CommandBox.MinHeight = step.Type == StepType.PowerShell ? 220 : 26;
        CommandBox.TextWrapping = step.Type == StepType.PowerShell ? TextWrapping.NoWrap : TextWrapping.Wrap;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Conditions and Options tabs of the step.</summary>
    private void ShowStepOptions(PackageStep step)
    {
        var nested = step.Type == StepType.Nested;
        StepConditions.Load(step.Conditions, forPackage: false);
        StepTimeoutHint.Text = StepTexts.TimeoutHint(step.Type, PackageTimeout);
        StepTimeoutBox.IsEnabled = ErrorModeBox.IsEnabled = !nested;
        ErrorModeBox.SelectedIndex = (int)step.ErrorMode;
        StepRunAsBox.Items.Clear();
        StepRunAsBox.Items.Add(new ComboBoxItem
        {
            Content = $"Paramètre du paquet ({Ui.RunAsText(Package.RunAs ?? _defaults.RunAs)})",
            Tag = null,
            IsSelected = step.RunAs == null,
        });
        foreach (var mode in Enum.GetValues<RunAsMode>())
            StepRunAsBox.Items.Add(new ComboBoxItem { Content = Ui.RunAsText(mode), Tag = mode, IsSelected = step.RunAs == mode });
    }

    private void StepName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Tree.SelectedItem is TreeViewItem { Tag: PackageStep step } node)
            node.Header = StepHeader(step, Package.Steps.IndexOf(step));
    }

    private void InstallFields_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateSilentHint();
        UpdateCommandLine();
        WarningFields_Changed(sender, e);
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
        if (await AllWarningsAsync() is { Count: > 0 } warnings
            && MessageDialog.Show(this, "Ce paquet a des avertissements :\n\n" + string.Join("\n", warnings.Select(w => "• " + w)) +
                    "\n\nEnregistrer quand même ?", "Paquet", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
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

    private static T Clone<T>(T value) => StepList.Copy(value);
}
