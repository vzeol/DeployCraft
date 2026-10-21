namespace DeployCraft.Server.Library;

/// <summary>
/// At startup then every hour: re-reads the library catalog if it is more than a day old, even with no console
/// open. New versions are thus reported, and updated by themselves if the setting asks for it.
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
