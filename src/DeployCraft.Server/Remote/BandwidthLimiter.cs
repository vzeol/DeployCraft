namespace DeployCraft.Server.Remote;

/// <summary>
/// Limite de débit partagée par toutes les copies de fichiers du serveur vers les postes : seau à jetons avec une
/// seconde de réserve. Chaque morceau copié réserve sa part et attend si le seau est à découvert ; le total des copies
/// en cours ne dépasse donc pas la limite, quel que soit le nombre de postes.
/// </summary>
public sealed class BandwidthLimiter(Func<DateTime>? clock = null, Action<TimeSpan, CancellationToken>? sleep = null)
{
    /// <summary>Limite des copies vers les postes, réglée par les Préférences.</summary>
    public static BandwidthLimiter Copies { get; } = new();

    private const int BufferSize = 256 * 1024;

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    /// <summary>Attente interrompue dès l'annulation.</summary>
    private readonly Action<TimeSpan, CancellationToken> _sleep = sleep ?? ((wait, ct) => ct.WaitHandle.WaitOne(wait));
    private readonly object _lock = new();
    private double _tokens;
    private DateTime? _last;
    private long _rate;

    /// <summary>Débit maximal en octets par seconde (0 = sans limite).</summary>
    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _rate);
        set => Interlocked.Exchange(ref _rate, Math.Max(0, value));
    }

    public static long FromMbps(int mbps) => mbps * 125_000L;

    /// <summary>Réserve <paramref name="bytes"/> octets ; attend le temps nécessaire si la limite est atteinte.</summary>
    public void Take(int bytes, CancellationToken ct = default)
    {
        var rate = BytesPerSecond;
        if (rate <= 0 || bytes <= 0) return;
        double wait;
        lock (_lock)
        {
            var now = _clock();
            if (_last is { } last) _tokens = Math.Min(rate, _tokens + (now - last).TotalSeconds * rate);
            _last = now;
            _tokens -= bytes;
            wait = _tokens < 0 ? -_tokens / rate : 0;
        }
        if (wait > 0) _sleep(TimeSpan.FromSeconds(wait), ct);
        ct.ThrowIfCancellationRequested();
    }

    /// <param name="copied">Appelé après chaque morceau écrit, avec sa taille (suivi de l'avancement).</param>
    /// <param name="ct">Annulation vérifiée entre deux morceaux et pendant les attentes : la copie s'arrête en route.</param>
    public void Copy(Stream source, Stream destination, Action<int>? copied = null, CancellationToken ct = default)
    {
        var buffer = new byte[BufferSize];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            Take(read, ct);
            destination.Write(buffer, 0, read);
            copied?.Invoke(read);
        }
    }

    /// <summary>
    /// Copie un fichier (écrase la destination). Sans limite ni suivi de l'avancement, par la copie du système, plus
    /// rapide ; sinon par morceaux.
    /// </summary>
    public void CopyFile(string source, string destination, Action<int>? copied = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (BytesPerSecond <= 0 && copied == null)
        {
            File.Copy(source, destination, overwrite: true);
            return;
        }
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
        using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            Copy(input, output, copied, ct);
        // Même date que la source, comme la copie du système (la copie de fichiers sur le poste s'en sert).
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
    }
}
