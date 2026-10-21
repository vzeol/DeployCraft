using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Endpoints;

/// <summary>Finding target computers: Active Directory and DNS checks.</summary>
internal static class TargetEndpoints
{
    public static void MapTargetEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/ad/computers", (string? search, ActiveDirectory ad) =>
        {
            try
            {
                return Results.Ok(ad.ListComputers(search));
            }
            catch (DeployException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // "Déployer une fois": each added computer is looked up in the server DNS (check or cross in the list).
        api.MapPost("/names/check", (List<string> names, NameCheck check, CancellationToken ct) => check.CheckAsync(names.Take(1000), ct));
    }
}
