using System.IO;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Package editor: Install step options, command line and warnings.</summary>
public partial class PackageWindow
{
    // ---------- Avertissements (fichier introuvable, pas de paramètre silencieux, mode pull) ----------

    private readonly PathCheck _paths = new();
    private readonly System.Windows.Threading.DispatcherTimer _warningCheck = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private CopyMode EffectiveCopyMode => Package.CopyMode ?? _defaults.CopyMode;

    private void WarningFields_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingStep) return;
        UpdateStepWarnings();
        _warningCheck.Stop();
        _warningCheck.Start();
    }

    private void UpdateStepWarnings()
    {
        var warnings = SelectedStep is { } step ? StepWarnings.For(step, EffectiveCopyMode, _paths.Missing) : [];
        StepWarningsText.Text = string.Join(Environment.NewLine, warnings.Select(w => "⚠ " + w));
        StepWarningsText.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Demande au service quels chemins il ne trouve pas ; sans réponse, rien n'est signalé.</summary>
    private async Task CheckPathsAsync(IEnumerable<string> paths, bool again = false)
    {
        var toCheck = _paths.ToCheck(paths, again);
        if (toCheck.Count == 0) return;
        try
        {
            _paths.Record(toCheck, await Api.MissingFiles(toCheck));
        }
        catch (ApiException) { }
    }

    private async void WarningCheck_Tick(object? sender, EventArgs e)
    {
        _warningCheck.Stop();
        if (SelectedStep is not { } step) return;
        await CheckPathsAsync(StepWarnings.Paths(step));
        UpdateStepWarnings();
    }

    /// <summary>Avertissements de toutes les étapes, vérifiés à nouveau juste avant l'enregistrement.</summary>
    private async Task<List<string>> AllWarningsAsync()
    {
        await CheckPathsAsync(Package.Steps.SelectMany(StepWarnings.Paths), again: true);
        return [.. Package.Steps.SelectMany((s, i) => StepWarnings.For(s, EffectiveCopyMode, _paths.Missing).Select(w => $"Étape {i + 1} « {s.Name} » : {w}"))];
    }

    // ---------- Installer : options Windows Installer et ligne de commande ----------

    private bool _updatingCommandLine;

    private void LoadInstallOptions(PackageStep step)
    {
        (step.MsiOperation switch
        {
            MsiOperation.Repair => MsiRepairRadio,
            MsiOperation.Uninstall => MsiUninstallRadio,
            _ => MsiInstallRadio,
        }).IsChecked = true;
        (step.MsiRestart switch
        {
            MsiRestart.Always => RestartAlwaysRadio,
            MsiRestart.Prompt => RestartPromptRadio,
            MsiRestart.NotSet => RestartNotSetRadio,
            _ => RestartNeverRadio,
        }).IsChecked = true;
        CustomCommandBox.IsChecked = !string.IsNullOrWhiteSpace(step.CustomCommandLine);
        UpdateCommandLine();
    }

    /// <summary>Options Windows Installer selon le fichier ; ligne générée, ou ligne écrite à la main si « Personnalisée ».</summary>
    private void UpdateCommandLine()
    {
        if (SelectedStep is not { Type: StepType.Install } step) return;
        var (msi, operation, repair) = StepTexts.InstallOptions(step.File);
        foreach (UIElement el in new UIElement[] { MsiRestartLabel, MsiRestartPanel, MsiQuietBox })
            el.Visibility = msi ? Visibility.Visible : Visibility.Collapsed;
        // .msp : un correctif s'applique seulement ; .msu : pas de réparation.
        MsiOperationLabel.Visibility = MsiOperationPanel.Visibility = operation ? Visibility.Visible : Visibility.Collapsed;
        MsiRepairRadio.Visibility = repair ? Visibility.Visible : Visibility.Collapsed;

        var custom = CustomCommandBox.IsChecked == true;
        _updatingCommandLine = true;
        CommandLineBox.IsReadOnly = !custom;
        CommandLineBox.Text = custom ? step.CustomCommandLine ?? "" : InstallCommand.Generate(step);
        _updatingCommandLine = false;
    }

    private void MsiOption_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingStep || SelectedStep is not { Type: StepType.Install } step) return;
        step.MsiOperation = MsiRepairRadio.IsChecked == true ? MsiOperation.Repair
            : MsiUninstallRadio.IsChecked == true ? MsiOperation.Uninstall
            : MsiOperation.Install;
        step.MsiRestart = RestartAlwaysRadio.IsChecked == true ? MsiRestart.Always
            : RestartPromptRadio.IsChecked == true ? MsiRestart.Prompt
            : RestartNotSetRadio.IsChecked == true ? MsiRestart.NotSet
            : MsiRestart.Never;
        UpdateCommandLine();
    }

    /// <summary>Cochée : la ligne générée devient modifiable. Décochée : on revient à la ligne générée.</summary>
    private void CustomCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { Type: StepType.Install } step) return;
        step.CustomCommandLine = CustomCommandBox.IsChecked == true ? InstallCommand.Generate(step) : null;
        UpdateCommandLine();
    }

    private void CommandLine_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingCommandLine || _loadingStep || CustomCommandBox.IsChecked != true || SelectedStep is not { Type: StepType.Install } step) return;
        step.CustomCommandLine = CommandLineBox.Text;
    }

    private void BrowseAdditional_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var dialog = new OpenFileDialog { Title = "Fichiers additionnels", Filter = "Tous les fichiers|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        var files = PackageStep.SplitLines(step.AdditionalFiles);
        files.AddRange(dialog.FileNames.Where(f => !files.Contains(f, StringComparer.OrdinalIgnoreCase)));
        step.AdditionalFiles = string.Join(Environment.NewLine, files);
        AdditionalFilesBox.Text = step.AdditionalFiles;
    }

    /// <summary>Un .exe sans paramètre silencieux attend un clic qui n'arrivera jamais : on prévient.</summary>
    private void UpdateSilentHint()
    {
        if (SelectedStep is not { Type: StepType.Install } step) return;
        // L'absence de paramètre silencieux est signalée en haut de l'étape ; ici, les paramètres courants pour aider.
        SilentHint.Text = StepTexts.SilentHint(step.File);
    }

    private void ErrorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStep || SelectedStep is not { } step || ErrorModeBox.SelectedIndex < 0) return;
        step.ErrorMode = (ErrorMode)ErrorModeBox.SelectedIndex;
        UpdateErrorModeHint(step);
    }

    private void StepRunAs_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingStep && SelectedStep is { } step && StepRunAsBox.SelectedItem is ComboBoxItem item)
            step.RunAs = item.Tag as RunAsMode?;
    }

    private void UpdateErrorModeHint(PackageStep step) => ErrorModeHint.Text = StepTexts.ErrorModeHint(step);

    private void StepEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep != null) StepName_TextChanged(this, null!);
        UpdateStepWarnings();
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
            Filter = "Installeurs|" + string.Join(";", InstallCommand.Extensions.Select(e => "*" + e)) + "|Tous les fichiers|*.*",
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

    private void BrowseCopyFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Fichier à copier", Filter = "Tous les fichiers|*.*" };
        if (SelectedStep is { } step && Directory.Exists(Path.GetDirectoryName(step.CopySource ?? "")))
            dialog.InitialDirectory = Path.GetDirectoryName(step.CopySource);
        if (dialog.ShowDialog(this) == true) SetCopySource(dialog.FileName);
    }

    private void BrowseCopyFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier à copier" };
        if (SelectedStep is { } step && Directory.Exists(step.CopySource)) dialog.InitialDirectory = step.CopySource;
        if (dialog.ShowDialog(this) == true) SetCopySource(dialog.FolderName);
    }

    private void SetCopySource(string path)
    {
        if (SelectedStep is not { } step) return;
        step.CopySource = path;
        CopySourceBox.Text = path;
        if (!string.IsNullOrWhiteSpace(step.Name)) return;
        step.Name = "Copie de " + Path.GetFileName(path.TrimEnd('\\'));
        StepView.DataContext = null;
        ShowStep(step);
        StepName_TextChanged(this, null!);
    }
}
