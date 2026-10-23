using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Presentation of the selected package, without a window.</summary>
public sealed class PackagePage(PackageList list, Func<Task<List<Schedule>>> loadSchedules, Func<DateTime?, string> localTime)
{
    public async Task<PackageActivity> RefreshAsync(long packageId, IEnumerable<Deployment> deployments,
        Action<IEnumerable<Deployment>> showDeployments, Func<long?> selectedScheduleId)
    {
        showDeployments(deployments.Where(d => d.PackageId == packageId));
        var schedules = (await loadSchedules()).Where(s => s.PackageIds.Contains(packageId)).Select(s => new ScheduleRow(s)).ToList();
        var selected = selectedScheduleId();
        return new(schedules, schedules.FirstOrDefault(s => s.Id == selected),
            schedules.Count == 0 ? L.Get("MainWindow_PackageNoSchedule") : L.Format("MainWindow_PackageScheduleCount", schedules.Count));
    }

    public PackageOverview Overview(Package p)
    {
        var steps = p.Steps.Count == 1 ? L.Get("MainWindow_OneStep") : L.Format("MainWindow_StepCount", p.Steps.Count);
        return new(string.IsNullOrWhiteSpace(p.Version) ? p.Name : $"{p.Name} {p.Version}",
            string.IsNullOrWhiteSpace(p.Description)
                ? L.Format("MainWindow_PackageSubtitle", steps, localTime(p.ModifiedUtc))
                : L.Format("MainWindow_PackageSubtitleDesc", p.Description, steps, localTime(p.ModifiedUtc)),
            StepRow.For(p.Steps, list.Find), PackageDetailRows.For(p, list.Defaults, list.Folders, list.Packages));
    }
}

public record PackageOverview(string Title, string Subtitle, List<StepRow> Steps,
    IReadOnlyList<(string Label, string Value)> Details);

public record PackageActivity(List<ScheduleRow> Schedules, ScheduleRow? SelectedSchedule, string ScheduleCount);
