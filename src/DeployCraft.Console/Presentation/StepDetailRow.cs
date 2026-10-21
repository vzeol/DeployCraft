using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Line of the step details of a computer.</summary>
public record StepDetailRow(int Number, string Name, string Status, string ReturnCode, string Duration, string Runs, string Note,
    string CommandLine, StepOutcome? Outcome)
{
    /// <summary>Every step of the package, run or not, to see where the deployment stopped.</summary>
    public static List<StepDetailRow> For(Deployment deployment, DeploymentTarget target)
    {
        // Toutes les étapes du paquet, exécutées ou non, pour voir où le déploiement s'est arrêté.
        // Les étapes avant l'étape de départ n'ont pas été exécutées (« redéployer depuis l'étape en échec »).
        var offset = target.StartStep - 1;
        return deployment.Snapshot.Steps.Select((step, i) =>
        {
            var j = i - offset;
            var outcome = j >= 0 && j < target.Steps.Count ? target.Steps[j] : null;
            return new StepDetailRow(
                i + 1,
                outcome?.Name ?? (string.IsNullOrWhiteSpace(step.Name) ? StepSummary.TypeText(step.Type) : step.Name),
                outcome?.State ?? (j < 0 || target.Status == TargetStatus.Failed ? "skipped" : "Queued"),
                outcome?.ExitCode?.ToString() ?? "",
                outcome?.State == "skipped" ? "" : Ui.Duration(outcome?.StartedUtc, outcome?.EndedUtc),
                // Déploiements antérieurs au compteur : une étape passée compte pour une exécution.
                outcome == null ? "" : Math.Max(outcome.Runs, outcome.Ran ? 1 : 0).ToString(),
                outcome?.Note ?? "",
                outcome?.CommandLine ?? outcome?.Error ?? "",
                outcome);
        }).ToList();
    }
}
