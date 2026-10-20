using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Validation;

namespace DeployCraft.Server.Endpoints;

/// <summary>Deployment preferences (defaults applied to every package).</summary>
internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/defaults", (Store store) => store.GetDefaults());

        api.MapPut("/settings/defaults", (DeploymentDefaults defaults, Store store, DeploymentEngine engine) =>
        {
            defaults.Offline ??= new();
            defaults.History ??= new();
            defaults.Performance ??= new();
            if (defaults.TimeoutMinutes < 1) return Results.BadRequest(new { error = "Le délai max doit être d'au moins 1 minute." });
            if (PackageValidation.ValidateOffline(defaults.Offline) is { } error) return Results.BadRequest(new { error = $"Hors ligne : {error}" });
            if (HistorySettings.Validate(defaults.History.KeepDays) is { } historyError) return Results.BadRequest(new { error = $"Historique : {historyError}" });
            if (PerformanceSettings.Validate(defaults.Performance) is { } performanceError) return Results.BadRequest(new { error = $"Performance : {performanceError}" });
            store.SaveDefaults(defaults);
            engine.ApplyPerformance(defaults.Performance);
            return Results.Ok(defaults);
        });
    }
}
