using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;

namespace DeployCraft.Server.Endpoints;

/// <summary>Deployments: start, follow, abort, delete.</summary>
internal static class DeploymentEndpoints
{
    public static void MapDeploymentEndpoints(this RouteGroupBuilder api)
    {
        // Un déploiement par paquet, dans l'ordre : la réponse est la liste des déploiements créés.
        api.MapPost("/deployments", (HttpContext context, DeployInput input, Store store, DeploymentEngine engine) =>
        {
            var packages = new List<Package>();
            foreach (var id in input.PackageIds)
            {
                if (store.GetPackage(id) is not { } package) return Results.NotFound(new { error = $"Paquet introuvable (n° {id})." });
                packages.Add(package);
            }
            try
            {
                return Results.Ok(engine.Start(packages, input.Targets, context.User.Identity?.Name, input.Options ?? new DeployOptions()));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/deployments", (Store store) => store.ListDeployments());

        api.MapGet("/deployments/{id:long}", (long id, Store store) =>
            store.GetDeployment(id) is { } d ? Results.Ok(d) : Results.NotFound());

        api.MapPost("/deployments/{id:long}/abort", (HttpContext context, long id, AbortInput input, DeploymentEngine engine) =>
            Results.Ok(new { aborted = engine.Abort(id, input.TargetIds, context.User.Identity?.Name) }));

        api.MapDelete("/deployments/{id:long}", (long id, Store store) =>
        {
            if (store.GetDeployment(id) is not { } d) return Results.NoContent();
            if (d.Status is DeploymentStatus.Running or DeploymentStatus.Queued)
                return Results.BadRequest(new { error = "Ce déploiement est en cours : attendez qu'il se termine." });
            store.DeleteDeployment(id);
            return Results.NoContent();
        });
    }
}

record DeployInput(List<long> PackageIds, List<TargetRequest> Targets, DeployOptions? Options);
record AbortInput(List<long>? TargetIds);
