using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Deployment refresh, filtering and selection, independent of window controls.</summary>
public sealed class DeploymentList(Func<DateTime?, string> localTime, Func<DateTime?, DateTime?, string> duration)
{
    private List<Deployment> _all = [];
    private long? _pendingSelection;

    public void SetDeployments(IEnumerable<Deployment> deployments) => _all = deployments.ToList();
    public void Select(long id) => _pendingSelection = id;

    public DeploymentRows Rows(string filter, long? selectedId)
    {
        filter = filter.Trim();
        var rows = _all.Select(d => new DeploymentRow(d, d.Id, DeploymentSummary.StatusOf(d), d.PackageName,
                d.CreatedUtc, localTime(d.CreatedUtc), duration(d.CreatedUtc, d.EndedUtc), DeploymentSummary.Count(d)))
            .Where(r => filter.Length == 0 || Matches(filter, r.Id.ToString(), Ui.StatusText(r.Status), r.PackageName, r.Created)).ToList();
        selectedId = _pendingSelection ?? selectedId;
        var selected = rows.FirstOrDefault(r => r.Id == selectedId) ?? (selectedId == null ? rows.FirstOrDefault() : null);
        if (selected != null && _pendingSelection == selected.Id) _pendingSelection = null;
        return new(rows, selected, rows.Count switch
        {
            0 => L.Get("DeploymentsPanel_NoDeployment"),
            1 => L.Get("DeploymentsPanel_OneDeployment"),
            _ => L.Format("DeploymentsPanel_DeploymentCount", rows.Count),
        });
    }

    public TargetRows Targets(Deployment d, string filter, IEnumerable<long> selectedIds)
    {
        filter = filter.Trim();
        var rows = d.Targets.Select(t => new TargetRow(t, d.Id, t.Computer, DeploymentSummary.TargetStatusOf(t),
                L.Format("DeploymentsPanel_StepsOf", t.Steps.Count(s => s.State is "success" or "stopped" || s.State == "skipped" && t.Status == TargetStatus.Successful), d.Snapshot.Steps.Count - t.StartStep + 1),
                t.Status == TargetStatus.Running ? t.Stage ?? "" : t.Error ?? "", localTime(t.StartedUtc), duration(t.StartedUtc, t.EndedUtc)))
            .Where(r => filter.Length == 0 || Matches(filter, r.Computer, Ui.StatusText(r.Status), r.Detail)).ToList();
        var ids = selectedIds.ToHashSet();
        var selected = rows.Where(r => ids.Contains(r.Target.Id)).ToList();
        if (selected.Count == 0 && rows.Count > 0) selected.Add(rows[0]);
        return new(rows, selected, rows.Count == 1 ? L.Get("DeploymentsPanel_OneComputer") : L.Format("DeploymentsPanel_ComputerCount", rows.Count));
    }

    private static bool Matches(string filter, params string?[] values) =>
        values.Any(v => v?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
}

public record DeploymentRow(Deployment Deployment, long Id, string Status, string PackageName, DateTime CreatedUtc,
    string Created, string Duration, DeploymentSummary.Counts Counts)
{
    public string Prioritized => Deployment.Targets.Any(t => t.Prioritized) ? L.Get("DeploymentsPanel_Yes") : "";
}

public record TargetRow(DeploymentTarget Target, long DeploymentId, string Computer, string Status, string Steps,
    string Detail, string Started, string Duration);

public record DeploymentRows(List<DeploymentRow> Rows, DeploymentRow? Selected, string Title);
public record TargetRows(List<TargetRow> Rows, List<TargetRow> Selected, string Title);
