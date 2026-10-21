using DeployCraft.Server.Data;

namespace DeployCraft.Server.Deployment;

/// <summary>
/// History cleanup (Preferences › Déploiement): at startup then once a day, deletes deployments
/// finished more than N days ago, if the setting is enabled.
/// </summary>
public class HistoryCleaner(IDeploymentRepository deploymentRepository, ISettingsRepository settingsRepository, ILogger<HistoryCleaner> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                Run(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Erreur du nettoyage de l'historique");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>One pass: returns the number of deployments deleted (0 if the setting is disabled).</summary>
    internal int Run(DateTime nowUtc)
    {
        var history = settingsRepository.GetDefaults().History ?? new();
        if (!history.Cleanup) return 0;

        // An out-of-range value (database edited by hand) must never delete more than intended.
        var days = Math.Clamp(history.KeepDays, HistorySettings.MinDays, HistorySettings.MaxDays);
        var deleted = deploymentRepository.PurgeFinished(nowUtc.AddDays(-days));
        log.LogInformation("Nettoyage de l'historique : {Count} déploiement(s) terminé(s) depuis plus de {Days} jour(s) supprimé(s)", deleted, days);
        return deleted;
    }
}
