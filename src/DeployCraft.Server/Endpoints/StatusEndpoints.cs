using System.Security.Principal;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Endpoints;

/// <summary>State of the server, shown in the status bar of the console.</summary>
internal static class StatusEndpoints
{
    public static void MapStatusEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/status", (HttpContext context, DeployCredentials cred) =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var all = cred.List();
            return new
            {
                machine = Environment.MachineName,
                runningAs = identity.Name,
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
                defaultCredential = all.FirstOrDefault(c => c.IsDefault) ?? all.FirstOrDefault(),
                unreadableCredentials = all.Count(c => c.PasswordUnreadable),
                version = typeof(Program).Assembly.GetName().Version?.ToString(3),
                connectedAs = context.User.Identity?.Name,
            };
        });
    }
}
