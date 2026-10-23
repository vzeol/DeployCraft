using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Deployment action decisions and API calls, with confirmation supplied by the caller.</summary>
public sealed class DeploymentActions(Func<long, IEnumerable<long>?, Task> abort, Func<long, Task> delete)
{
    public static bool IsActive(DeploymentTarget t) => t.Status is TargetStatus.Queued or TargetStatus.Running;
    public static bool CanAbort(Deployment? d) => d?.Status is DeploymentStatus.Queued or DeploymentStatus.Running;
    public static bool CanRedeploy(Deployment? d) => d != null;
    public static bool CanRedeployFailed(Deployment? d) => d?.Targets.Any(t => t.Status == TargetStatus.Failed) == true;
    public static bool CanDelete(Deployment? d) => d != null && !CanAbort(d);

    public static List<TargetRequest> Fresh(IEnumerable<string> computers) => computers.Select(c => new TargetRequest(c)).ToList();
    public static List<DeploymentTarget> Failed(IEnumerable<DeploymentTarget> targets) => targets.Where(t => t.Status == TargetStatus.Failed).ToList();

    public static TargetActions Targets(List<DeploymentTarget> targets) => new(
        targets.Where(IsActive).ToList(), Failed(targets), Fresh(targets.Select(t => t.Computer)),
        targets.Count == 1, string.Join(Environment.NewLine, targets.Select(t => t.Computer)),
        string.Join(Environment.NewLine, targets.Select(t => t.Error).Where(e => !string.IsNullOrEmpty(e))),
        targets.Any(t => !string.IsNullOrEmpty(t.Error)));

    public static List<TargetRequest> Resume(IEnumerable<DeploymentTarget> targets) => targets.Select(t =>
    {
        var index = t.Steps.FindIndex(s => s.State == "failed");
        return new TargetRequest(t.Computer, index < 0 ? t.StartStep : t.StartStep + index);
    }).ToList();

    public async Task<bool> AbortAsync(Deployment d, List<DeploymentTarget>? targets, Func<string, bool> confirm)
    {
        var what = targets == null ? L.Format("DeploymentsPanel_AbortWhatAll", d.Id, d.PackageName)
            : targets.Count == 1 ? L.Format("DeploymentsPanel_AbortWhatOne", targets[0].Computer) : L.Format("DeploymentsPanel_AbortWhatMany", targets.Count);
        if (!confirm(L.Format("DeploymentsPanel_AbortConfirm", what))) return false;
        await abort(d.Id, targets?.Select(t => t.Id));
        return true;
    }

    public async Task<bool> DeleteAsync(Deployment? d, Func<string, bool> confirm)
    {
        if (d == null || !CanDelete(d) || !confirm(L.Format("DeploymentsPanel_DeleteConfirm", d.Id, d.PackageName))) return false;
        await delete(d.Id);
        return true;
    }
}

public record TargetActions(List<DeploymentTarget> Active, List<DeploymentTarget> Failed, List<TargetRequest> Fresh,
    bool CanOpenSteps, string Names, string Errors, bool CanCopyError);
