using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;

namespace DeployCraft.Server.Endpoints;

/// <summary>Deployments: start, follow, abort, delete.</summary>
internal static class DeploymentEndpoints
{
    public static void MapDeploymentEndpoints(this RouteGroupBuilder api)
    {
        // One deployment per package, in order: the response is the list of deployments created.
        api.MapPost("/deployments", (HttpContext context, DeployInput input, IPackageRepository packageRepository, DeploymentEngine engine) =>
        {
            var packages = new List<Package>();
            foreach (var id in input.PackageIds)
            {
                if (packageRepository.Get(id) is not { } package) return Results.NotFound(new { error = $"Paquet introuvable (n° {id})." });
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

        api.MapGet("/deployments", (IDeploymentRepository deploymentRepository) => deploymentRepository.List());

        api.MapGet("/deployments/{id:long}", (long id, IDeploymentRepository deploymentRepository) =>
            deploymentRepository.Get(id) is { } d ? Results.Ok(d) : Results.NotFound());

        api.MapPost("/deployments/{id:long}/abort", (HttpContext context, long id, AbortInput input, DeploymentEngine engine) =>
            Results.Ok(new { aborted = engine.Abort(id, input.TargetIds, context.User.Identity?.Name) }));

        api.MapDelete("/deployments/{id:long}", (long id, IDeploymentRepository deploymentRepository) =>
        {
            if (deploymentRepository.Get(id) is not { } d) return Results.NoContent();
            if (d.Status is DeploymentStatus.Running or DeploymentStatus.Queued)
                return Results.BadRequest(new { error = "Ce déploiement est en cours : attendez qu'il se termine." });
            deploymentRepository.Delete(id);
            return Results.NoContent();
        });
    }
}

record DeployInput(List<long> PackageIds, List<TargetRequest> Targets, DeployOptions? Options);
record AbortInput(List<long>? TargetIds);
