using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Validation;

namespace DeployCraft.Server.Endpoints;

/// <summary>Deployment preferences (defaults applied to every package).</summary>
internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/defaults", (ISettingsRepository settingsRepository) => settingsRepository.GetDefaults());

        api.MapPut("/settings/defaults", (DeploymentDefaults defaults, ISettingsRepository settingsRepository, DeploymentEngine engine) =>
        {
            defaults.Offline ??= new();
            defaults.History ??= new();
            defaults.Performance ??= new();
            if (defaults.TimeoutMinutes < 1) return Results.BadRequest(new { error = Msg.Get("SettingsEndpoints_TimeoutMin") });
            if (PackageValidation.ValidateOffline(defaults.Offline) is { } error) return Results.BadRequest(new { error = Msg.Format("SettingsEndpoints_Offline", error) });
            if (HistorySettings.Validate(defaults.History.KeepDays) is { } historyError) return Results.BadRequest(new { error = Msg.Format("SettingsEndpoints_History", historyError) });
            if (PerformanceSettings.Validate(defaults.Performance) is { } performanceError) return Results.BadRequest(new { error = Msg.Format("SettingsEndpoints_Performance", performanceError) });
            settingsRepository.SaveDefaults(defaults);
            engine.ApplyPerformance(defaults.Performance);
            return Results.Ok(defaults);
        });
    }
}
