using System.Net;
using DeployCraft.Server.Security;

namespace DeployCraft.Server.Endpoints;

/// <summary>Background service: status, restart and account.</summary>
internal static class ServiceEndpoints
{
    public static void MapServiceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/service", (HttpContext context) => new
        {
            info = ServiceAccount.Info(),
            canEdit = ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User)),
            // The password does not travel over the network: the account is changed from the server's console.
            local = context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip),
        });

        api.MapPost("/service/restart", (HttpContext context, ServiceAccount service) =>
        {
            if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return AccessEndpoints.OnlyAdmins();
            return service.Restart() is { } error ? Results.BadRequest(new { error }) : Results.NoContent();
        });

        api.MapPut("/service/account", (HttpContext context, ServiceAccountInput input, ServiceAccount service) =>
        {
            if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return AccessEndpoints.OnlyAdmins();
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
                return Results.BadRequest(new { error = "Le compte du service se change depuis la console du serveur (le mot de passe ne passe pas par le réseau)." });
            return service.Change(input.Account, input.Password) is { } error ? Results.BadRequest(new { error }) : Results.NoContent();
        });
    }
}

record ServiceAccountInput(string? Account, string? Password);
