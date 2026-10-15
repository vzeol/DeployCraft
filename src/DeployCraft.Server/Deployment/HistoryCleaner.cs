using DeployCraft.Server.Data;

namespace DeployCraft.Server.Deployment;

/// <summary>
/// Nettoyage de l'historique (Préférences › Déploiement) : au démarrage puis une fois par jour, supprime les
/// déploiements terminés depuis plus de N jours, si le réglage est activé.
/// </summary>
public class HistoryCleaner(Store store, ILogger<HistoryCleaner> log) : BackgroundService
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

    /// <summary>Un passage : renvoie le nombre de déploiements supprimés (0 si le réglage est désactivé).</summary>
    internal int Run(DateTime nowUtc)
    {
        var history = store.GetDefaults().History ?? new();
        if (!history.Cleanup) return 0;

        // Une valeur hors limites (base modifiée à la main) ne doit jamais effacer plus que prévu.
        var days = Math.Clamp(history.KeepDays, HistorySettings.MinDays, HistorySettings.MaxDays);
        var deleted = store.PurgeFinishedDeployments(nowUtc.AddDays(-days));
        log.LogInformation("Nettoyage de l'historique : {Count} déploiement(s) terminé(s) depuis plus de {Days} jour(s) supprimé(s)", deleted, days);
        return deleted;
    }
}
