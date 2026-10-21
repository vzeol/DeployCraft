using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Detail of the steps run on a computer: status, return code, duration, output.</summary>
public partial class StepsWindow : Window
{
    private readonly int _startStep;
    private readonly string _computer;
    private readonly long _deploymentId;

    public StepsWindow(Deployment deployment, DeploymentTarget target)
    {
        InitializeComponent();
        _startStep = target.StartStep;
        _computer = target.Computer;
        _deploymentId = deployment.Id;
        Title = L.Format("StepsWindow_TitleFor", target.Computer);
        Heading.Text = $"{target.Computer} · {Ui.StatusText(target.Status.ToString())}";
        SubHeading.Text = L.Format("StepsWindow_SubHeading", deployment.PackageName, deployment.Id, Ui.LocalTime(target.StartedUtc));

        var rows = StepDetailRow.For(deployment, target);

        Grid.ItemsSource = rows;
        Grid.SelectedItem = rows.FirstOrDefault(r => r.Status == "failed") ?? rows.LastOrDefault(r => r.Outcome != null) ?? rows.FirstOrDefault();
        if (rows.Count == 0) Output.Text = target.Error ?? "";
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not StepDetailRow row) return;
        var sb = new StringBuilder();
        if (row.Outcome == null)
            sb.AppendLine(row.Number < _startStep
                ? L.Format("StepsWindow_NotRunBeforeStart", _startStep)
                : L.Get("StepsWindow_NotRun"));
        else
        {
            if (!string.IsNullOrEmpty(row.Outcome.CommandLine)) sb.AppendLine("> " + row.Outcome.CommandLine).AppendLine();
            // Skipped step or stop on success: it is a reason, not an error.
            if (!string.IsNullOrEmpty(row.Outcome.Error))
                sb.AppendLine(L.Get(row.Outcome.State == "failed" ? "StepsWindow_ErrorPrefix" : "StepsWindow_ReasonPrefix") + row.Outcome.Error).AppendLine();
            sb.Append(string.IsNullOrWhiteSpace(row.Outcome.Output) ? L.Get("StepsWindow_NoOutput") : row.Outcome.Output.TrimEnd());
        }
        Output.Text = sb.ToString();
    }

    // ---------- Full output: copy, open, save ----------

    private string CurrentFileName => DeploymentSummary.OutputFileName(_computer, _deploymentId, (Grid.SelectedItem as StepDetailRow)?.Number ?? 0);

    private void CopyOutput_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Output.Text); }
        catch (System.Runtime.InteropServices.COMException) { /* presse-papiers occupé : l'utilisateur réessaiera */ }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "DeployCraft")).FullName;
            var file = Path.Combine(folder, CurrentFileName);
            File.WriteAllText(file, Output.Text, new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Ui.Error(this, ex);
        }
    }

    private void SaveOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = CurrentFileName, Filter = L.Get("StepsWindow_FilterText") + "|*.txt|" + L.Get("PackageWindow_FilterAllFiles") + "|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, Output.Text, new UTF8Encoding(true)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(this, ex); }
    }
}
