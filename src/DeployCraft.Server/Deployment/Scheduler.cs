using DeployCraft.Server.Data;

namespace DeployCraft.Server.Deployment;

/// <summary>
/// Fires the schedules. Every 15 s, each active schedule checks whether one of its
/// triggers has passed since the last check; if so, it starts one deployment per package
/// on the computers that still need it.
/// </summary>
public class Scheduler(IScheduleRepository scheduleRepository, IPackageRepository packageRepository, DeploymentEngine engine, ILogger<Scheduler> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A missed trigger (service stopped, server off) is only caught up if it is less than
    /// 5 minutes old: we do not start at 8 pm an installation planned for 8 am.
    /// </summary>
    private static readonly TimeSpan CatchUp = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Tick);
        do
        {
            try
            {
                CheckAll();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Erreur du planificateur");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private void CheckAll()
    {
        var now = DateTime.Now;
        foreach (var schedule in scheduleRepository.List())
        {
            var since = scheduleRepository.GetChecked(schedule.Id) ?? now;
            scheduleRepository.SetChecked(schedule.Id, now);
            if (!schedule.Enabled) continue;

            // Several triggers passed since the last check: a single launch.
            var due = schedule.Triggers.Select(t => TriggerCalendar.Next(t, since)).Where(d => d <= now).Min();
            if (due == null) continue;
            if (now - due.Value > CatchUp)
            {
                log.LogInformation("Planification {Name} : déclenchement du {Due} manqué, non rattrapé", schedule.Name, due);
                continue;
            }
            Run(schedule, launchedBy: null);
        }
    }

    /// <summary>
    /// Starts the schedule now. Returns the deployments created (none if all computers
    /// have already succeeded, are running or reached the maximum number of failures).
    /// </summary>
    public List<Data.Deployment> Run(Schedule schedule, string? launchedBy)
    {
        lock (_lock)
        {
            var (deployments, result) = Plan(schedule, launchedBy);
            scheduleRepository.SetRun(schedule.Id, DateTime.UtcNow, result);
            log.LogInformation("Planification {Name} : {Result}", schedule.Name, result);
            return deployments;
        }
    }

    private (List<Data.Deployment>, string) Plan(Schedule schedule, string? launchedBy)
    {
        var packages = schedule.PackageIds.Select(packageRepository.Get).OfType<Package>().Where(p => p.Steps.Count > 0).ToList();
        if (packages.Count == 0) return ([], "Aucun paquet à déployer (paquets supprimés ou sans étape).");
        var computers = schedule.Targets.Select(c => c.Trim().TrimStart('\\')).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (computers.Count == 0) return ([], "Aucune cible.");

        var history = scheduleRepository.GetHistory(schedule.Id)
            .ToDictionary(h => (h.Computer.ToUpperInvariant(), h.PackageId));
        // A computer still served by a previous launch is left alone until the next one.
        var busy = scheduleRepository.ActiveComputers(schedule.Id);

        var plan = new List<(Package, List<TargetRequest>)>();
        int done = 0, gaveUp = 0;
        foreach (var package in packages)
        {
            var targets = new List<TargetRequest>();
            foreach (var computer in computers.Where(c => !busy.Contains(c)))
            {
                history.TryGetValue((computer.ToUpperInvariant(), package.Id), out var h);
                if (schedule.StopOnSuccess && h?.Succeeded == true) { done++; continue; }
                if (schedule.StopAfterFailures is > 0 and var max && h?.Failures >= max) { gaveUp++; continue; }
                targets.Add(new TargetRequest(computer));
            }
            plan.Add((package, targets));
        }

        var count = plan.Sum(p => p.Item2.Count);
        var skipped = new List<string>();
        if (done > 0) skipped.Add($"{done} déjà réussi(s)");
        if (gaveUp > 0) skipped.Add($"{gaveUp} abandonné(s) après {schedule.StopAfterFailures} échec(s)");
        if (busy.Count > 0) skipped.Add($"{busy.Count} poste(s) encore en cours");
        var detail = skipped.Count > 0 ? $" ({string.Join(", ", skipped)})" : "";

        if (count == 0) return ([], $"Rien à déployer{detail}.");
        var options = new DeployOptions(schedule.RunAs, schedule.CopyMode, CredentialId: schedule.CredentialId);
        List<Data.Deployment> deployments;
        try
        {
            deployments = engine.StartPlan(plan, launchedBy ?? $"Planification « {schedule.Name} »", options, schedule);
        }
        catch (ArgumentException ex)
        {
            return ([], ex.Message);
        }
        var ids = string.Join(", ", deployments.Select(d => $"n° {d.Id}"));
        var hosts = plan.SelectMany(p => p.Item2).Select(t => t.Computer).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return (deployments, $"Lancé sur {hosts} poste(s){detail} : déploiement {ids}.");
    }
}
