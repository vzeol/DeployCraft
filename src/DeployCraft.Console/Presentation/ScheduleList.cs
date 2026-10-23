using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Schedule loading, selection and actions without window controls.</summary>
public sealed class ScheduleList(Func<Task<List<Schedule>>> load, Func<long, Task<Schedule>> get,
    Func<Schedule, Task<Schedule>> save, Func<long, Task> delete, Func<long, Task<ScheduleRunResult>> run)
{
    public async Task<ScheduleRows> RefreshAsync(Func<long?> selectedId, long? select = null)
    {
        var rows = (await load()).Select(s => new ScheduleRow(s)).ToList();
        var selected = select ?? selectedId();
        var row = rows.FirstOrDefault(r => r.Id == selected);
        return new(rows, row, rows.Count == 0 ? L.Get("MainWindow_SchedulesEmpty")
            : row != null ? L.Format("MainWindow_SchedulesSubtitleOne", rows.Count, rows.Count(r => r.Schedule.Enabled), row.Name)
            : L.Format("MainWindow_SchedulesSubtitleAll", rows.Count, rows.Count(r => r.Schedule.Enabled)));
    }

    public static Schedule New(Package? package) => package == null ? new() : new() { PackageIds = [package.Id], Name = package.Name };
    public Task<Schedule> GetAsync(long id) => get(id);
    public async Task<List<Schedule>> CandidatesAsync(long packageId) => (await load()).Where(s => !s.PackageIds.Contains(packageId)).ToList();

    public async Task AttachAsync(long id, long packageId)
    {
        var schedule = await get(id);
        if (schedule.Attach(packageId)) await save(schedule);
    }

    public async Task<bool> DetachAsync(long id, Package package, Action<string> inform, Func<string, bool> confirm)
    {
        var schedule = await get(id);
        if (schedule.PackageIds.Count == 1 && schedule.PackageIds[0] == package.Id)
        {
            inform(L.Format("MainWindow_OnlyPackageInSchedule", package.Name, schedule.Name));
            return false;
        }
        if (!confirm(L.Format("MainWindow_DetachConfirm", package.Name, schedule.Name))) return false;
        if (schedule.Detach(package.Id)) await save(schedule);
        return true;
    }

    public async Task<string?> RunAsync(long id)
    {
        var result = await run(id);
        return result.Deployments.Count == 0 ? result.Result ?? L.Get("MainWindow_NothingToDeploy") : null;
    }

    public async Task ToggleAsync(long id)
    {
        var schedule = await get(id);
        schedule.Enabled = !schedule.Enabled;
        await save(schedule);
    }

    public async Task<Schedule> DuplicateAsync(long id)
    {
        var copy = await get(id);
        copy.Id = 0;
        copy.Name += " " + L.Get("MainWindow_CopySuffix");
        copy.Enabled = false; // An active copy would double deployments; it must be enabled by hand.
        return await save(copy);
    }

    public async Task<bool> DeleteAsync(Schedule schedule, Func<string, bool> confirm)
    {
        if (!confirm(L.Format("MainWindow_DeleteScheduleConfirm", schedule.Name))) return false;
        await delete(schedule.Id);
        return true;
    }

    public static IEnumerable<Deployment> Deployments(IEnumerable<Deployment> deployments, long? selectedId) =>
        deployments.Where(d => selectedId == null ? d.ScheduleId != null : d.ScheduleId == selectedId);
}

public record ScheduleRows(List<ScheduleRow> Rows, ScheduleRow? Selected, string Subtitle)
{
    public bool CanRun => Selected != null;
}
