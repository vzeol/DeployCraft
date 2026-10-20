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
        api.MapGet("/schedules", (Store store) => store.ListSchedules().Select(WithNextRun));

        api.MapGet("/schedules/{id:long}", (long id, Store store) =>
            store.GetSchedule(id) is { } s ? Results.Ok(WithNextRun(s)) : Results.NotFound());

        api.MapPost("/schedules", (Schedule s, Store store, DeployCredentials cred) =>
        {
            if (ScheduleValidation.Validate(s, credentialId => cred.Get(credentialId) != null) is { } error) return Results.BadRequest(new { error });
            s.Id = 0;
            return Results.Ok(WithNextRun(store.SaveSchedule(s)));
        });

        api.MapPut("/schedules/{id:long}", (long id, Schedule s, Store store, DeployCredentials cred) =>
        {
            if (store.GetSchedule(id) == null) return Results.NotFound();
            if (ScheduleValidation.Validate(s, credentialId => cred.Get(credentialId) != null) is { } error) return Results.BadRequest(new { error });
            s.Id = id;
            return Results.Ok(WithNextRun(store.SaveSchedule(s)));
        });

        api.MapDelete("/schedules/{id:long}", (long id, Store store) =>
        {
            store.DeleteSchedule(id);
            return Results.NoContent();
        });

        // « Démarrer maintenant » : même règles qu'un déclenchement (postes réussis ou en cours ignorés).
        api.MapPost("/schedules/{id:long}/run", (HttpContext context, long id, Store store, Scheduler scheduler) =>
        {
            if (store.GetSchedule(id) is not { } s) return Results.NotFound();
            var deployments = scheduler.Run(s, context.User.Identity?.Name);
            return Results.Ok(new { deployments, result = store.GetSchedule(id)?.LastResult });
        });

        api.MapGet("/schedules/{id:long}/history", (long id, Store store) => store.GetScheduleHistory(id));

        // Efface des lignes de l'historique des cibles (toutes si la liste est vide) : ces postes seront redéployés.
        api.MapPost("/schedules/{id:long}/history/clear", (long id, List<ScheduleHistoryKey>? keys, Store store) =>
        {
            store.ClearScheduleHistory(id, keys);
            return Results.NoContent();
        });
    }

    private static Schedule WithNextRun(Schedule s)
    {
        s.NextRunUtc = s.Enabled ? TriggerCalendar.Next(s, DateTime.Now)?.ToUniversalTime() : null;
        return s;
    }
}
