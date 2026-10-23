using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Localized deployment and computer cards, without window controls.</summary>
public sealed class DeploymentDetails(Func<DateTime?, string> localTime, Func<DateTime?, DateTime?, string> duration)
{
    public DeploymentCard Deployment(Deployment d)
    {
        var status = DeploymentSummary.StatusOf(d);
        var title = L.Format("DeploymentsPanel_DeploymentTitle", d.Id);
        var statusText = Ui.StatusText(status);
        var package = d.StartStep > 1 ? L.Format("DeploymentsPanel_FromStep", d.PackageName, d.StartStep) : d.PackageName;
        var created = localTime(d.CreatedUtc);
        var durationText = duration(d.CreatedUtc, d.EndedUtc);
        var launchedBy = d.LaunchedBy ?? "—";
        var runAs = d.RunAsMode switch
        {
            RunAsMode.DeployUser => d.RunAs ?? "—",
            RunAsMode.LocalSystem => L.Get("Ui_RunAsLocalSystem"),
            _ => L.Get("Ui_RunAsLoggedOn"),
        }
        // The credential is also used to copy files and create the service, even without running under this account.
        + (d.RunAsMode != RunAsMode.DeployUser && d.CredentialId != null ? " " + L.Format("DeploymentsPanel_RunAsAccess", d.RunAs) : "")
        + (d.CopyMode == CopyMode.Pull ? " · pull" : "");
        var failedCount = d.Targets.Count(t => t.Status == TargetStatus.Failed);
        var total = $"{d.Targets.Count} ";
        var failed = $"{failedCount} ";
        var running = DeploymentActions.CanAbort(d);
        var counts = running
            ? L.Format("DeploymentsPanel_RunningWaiting", d.Targets.Count(t => t.Status == TargetStatus.Running), d.Targets.Count(t => t.Status == TargetStatus.Queued))
            : "";
        return new(status, title, statusText, package, created, durationText, launchedBy, runAs, total, failed, counts, failedCount > 0, running);
    }

    public TargetCard Target(TargetRow row)
    {
        var t = row.Target;
        var name = t.Computer;
        var statusText = Ui.StatusText(row.Status);
        var steps = row.Steps + " " + L.Get("DeploymentsPanel_StepsWord");
        var current = t.Status == TargetStatus.Running ? t.Stage ?? "" : "";
        var started = string.IsNullOrEmpty(row.Started) ? "—" : row.Started;
        var ended = t.EndedUtc == null ? "—" : localTime(t.EndedUtc);
        var duration = string.IsNullOrEmpty(row.Duration) ? "—" : row.Duration;
        var error = t.Error ?? "";
        // Reason on a succeeded computer (skipped step, stop on success...): a note, not an error.
        var failed = t.Status == TargetStatus.Failed;
        var errorTitle = failed ? L.Get("DeploymentsPanel_ErrorTitle") : L.Get("DeploymentsPanel_NoteTitle");
        return new(row.Status, name, statusText, steps, current, started, ended, duration, error, errorTitle, t.Status == TargetStatus.Running, failed, !string.IsNullOrEmpty(t.Error));
    }
}

public record DeploymentCard(string Status, string Title, string StatusText, string Package, string Created, string Duration, string LaunchedBy, string RunAs, string Total, string Failed, string Counts, bool CanRetry, bool Running);

public record TargetCard(string Status, string Name, string StatusText, string Steps, string Current, string Started, string Ended, string Duration, string Error, string ErrorTitle, bool Running, bool Failed, bool HasError);
