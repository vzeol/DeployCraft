using DeployCraft.Server.Security;

namespace DeployCraft.Server.Endpoints;

/// <summary>Console users and active sessions (Options › Console users).</summary>
internal static class AccessEndpoints
{
    public static void MapAccessEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/access", (HttpContext context, ConsoleAccess access) => new
        {
            users = access.ListUsers(),
            sessions = access.ActiveSessions(),
            canEdit = ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User)),
        });

        api.MapPost("/access/users", (HttpContext context, ConsoleUserInput input, ConsoleAccess access) =>
        {
            if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return OnlyAdmins();
            var (user, error) = access.AddUser(input.Name);
            return user != null ? Results.Ok(user) : Results.BadRequest(new { error });
        });

        api.MapDelete("/access/users/{sid}", (HttpContext context, string sid, ConsoleAccess access) =>
        {
            if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return OnlyAdmins();
            access.RemoveUser(sid);
            return Results.NoContent();
        });
    }

    internal static IResult OnlyAdmins() => Results.Json(
        new { error = Msg.Get("AccessEndpoints_OnlyAdmins") },
        statusCode: StatusCodes.Status403Forbidden);
}

record ConsoleUserInput(string? Name);
