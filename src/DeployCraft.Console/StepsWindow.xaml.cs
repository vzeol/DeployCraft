using System.Text;
using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record StepDetailRow(int Number, string Name, string Status, string ReturnCode, string Duration, string CommandLine, StepOutcome? Outcome);

/// <summary>Détail des étapes exécutées sur un poste : statut, code retour, durée, sortie.</summary>
public partial class StepsWindow : Window
{
    private readonly int _startStep;

    public StepsWindow(Deployment deployment, DeploymentTarget target)
    {
        InitializeComponent();
        _startStep = target.StartStep;
        Title = $"{target.Computer} — Étapes";
        Heading.Text = $"{target.Computer} · {Ui.StatusText(target.Status.ToString())}";
        SubHeading.Text = $"{deployment.PackageName} · déploiement {deployment.Id} · {Ui.LocalTime(target.StartedUtc)}";

        // Toutes les étapes du paquet, exécutées ou non, pour voir où le déploiement s'est arrêté.
        // Les étapes avant l'étape de départ n'ont pas été exécutées (« redéployer depuis l'étape en échec »).
        var offset = target.StartStep - 1;
        var rows = deployment.Snapshot.Steps.Select((step, i) =>
        {
            var j = i - offset;
            var outcome = j >= 0 && j < target.Steps.Count ? target.Steps[j] : null;
            return new StepDetailRow(
                i + 1,
                outcome?.Name ?? (string.IsNullOrWhiteSpace(step.Name) ? MainWindow.StepTypeText(step.Type) : step.Name),
                outcome?.State ?? (j < 0 || target.Status == TargetStatus.Failed ? "skipped" : "Queued"),
                outcome?.ExitCode?.ToString() ?? "",
                outcome?.State == "skipped" ? "" : Ui.Duration(outcome?.StartedUtc, outcome?.EndedUtc),
                outcome?.CommandLine ?? outcome?.Error ?? "",
                outcome);
        }).ToList();

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
                ? $"Étape non exécutée : ce poste a démarré à l'étape {_startStep}."
                : "Cette étape n'a pas été exécutée.");
        else
        {
            if (!string.IsNullOrEmpty(row.Outcome.CommandLine)) sb.AppendLine("> " + row.Outcome.CommandLine).AppendLine();
            // Étape ignorée ou arrêtée en succès : c'est une raison, pas une erreur.
            if (!string.IsNullOrEmpty(row.Outcome.Error))
                sb.AppendLine((row.Outcome.State == "failed" ? "Erreur : " : "Raison : ") + row.Outcome.Error).AppendLine();
            sb.Append(string.IsNullOrWhiteSpace(row.Outcome.Output) ? "(aucune sortie)" : row.Outcome.Output.TrimEnd());
        }
        Output.Text = sb.ToString();
    }
}
