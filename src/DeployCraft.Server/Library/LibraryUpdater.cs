namespace DeployCraft.Server.Library;

/// <summary>
/// Au démarrage puis chaque heure : relit le catalogue de la bibliothèque s'il a plus d'un jour, même sans console
/// ouverte. Les nouvelles versions sont ainsi signalées, et mises à jour d'elles-mêmes si le réglage le demande.
/// </summary>
public class LibraryUpdater(PackageLibrary library, ILogger<LibraryUpdater> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await library.RefreshIfStaleAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Erreur de la lecture quotidienne de la bibliothèque");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
