using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;
using DeployCraft.Server.Validation;

namespace DeployCraft.Server.Endpoints;

/// <summary>Schedules and their target history.</summary>
internal static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/schedules", (IScheduleRepository scheduleRepository) => scheduleRepository.List().Select(WithNextRun));

        api.MapGet("/schedules/{id:long}", (long id, IScheduleRepository scheduleRepository) =>
            scheduleRepository.Get(id) is { } s ? Results.Ok(WithNextRun(s)) : Results.NotFound());

        api.MapPost("/schedules", (Schedule s, IScheduleRepository scheduleRepository, DeployCredentials cred) =>
        {
            if (ScheduleValidation.Validate(s, credentialId => cred.Get(credentialId) != null) is { } error) return Results.BadRequest(new { error });
            s.Id = 0;
            return Results.Ok(WithNextRun(scheduleRepository.Save(s)));
        });

        api.MapPut("/schedules/{id:long}", (long id, Schedule s, IScheduleRepository scheduleRepository, DeployCredentials cred) =>
        {
            if (scheduleRepository.Get(id) == null) return Results.NotFound();
            if (ScheduleValidation.Validate(s, credentialId => cred.Get(credentialId) != null) is { } error) return Results.BadRequest(new { error });
            s.Id = id;
            return Results.Ok(WithNextRun(scheduleRepository.Save(s)));
        });

        api.MapDelete("/schedules/{id:long}", (long id, IScheduleRepository scheduleRepository) =>
        {
            scheduleRepository.Delete(id);
            return Results.NoContent();
        });

        // « Démarrer maintenant » : même règles qu'un déclenchement (postes réussis ou en cours ignorés).
        api.MapPost("/schedules/{id:long}/run", (HttpContext context, long id, IScheduleRepository scheduleRepository, Scheduler scheduler) =>
        {
            if (scheduleRepository.Get(id) is not { } s) return Results.NotFound();
            var deployments = scheduler.Run(s, context.User.Identity?.Name);
            return Results.Ok(new { deployments, result = scheduleRepository.Get(id)?.LastResult });
        });

        api.MapGet("/schedules/{id:long}/history", (long id, IScheduleRepository scheduleRepository) => scheduleRepository.GetHistory(id));

        // Efface des lignes de l'historique des cibles (toutes si la liste est vide) : ces postes seront redéployés.
        api.MapPost("/schedules/{id:long}/history/clear", (long id, List<ScheduleHistoryKey>? keys, IScheduleRepository scheduleRepository) =>
        {
            scheduleRepository.ClearHistory(id, keys);
            return Results.NoContent();
        });
    }

    private static Schedule WithNextRun(Schedule s)
    {
        s.NextRunUtc = s.Enabled ? TriggerCalendar.Next(s, DateTime.Now)?.ToUniversalTime() : null;
        return s;
    }
}
