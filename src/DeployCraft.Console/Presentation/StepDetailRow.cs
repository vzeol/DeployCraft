using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Line of the step details of a computer.</summary>
public record StepDetailRow(int Number, string Name, string Status, string ReturnCode, string Duration, string Runs, string Note,
    string CommandLine, StepOutcome? Outcome)
{
    /// <summary>Every step of the package, run or not, to see where the deployment stopped.</summary>
    public static List<StepDetailRow> For(Deployment deployment, DeploymentTarget target)
    {
        // Every step of the package, run or not, to see where the deployment stopped.
        // Steps before the start step were not run ("redeploy from the failed step").
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
                // Deployments from before the counter: a past step counts as one run.
                outcome == null ? "" : Math.Max(outcome.Runs, outcome.Ran ? 1 : 0).ToString(),
                outcome?.Note ?? "",
                outcome?.CommandLine ?? outcome?.Error ?? "",
                outcome);
        }).ToList();
    }
}
