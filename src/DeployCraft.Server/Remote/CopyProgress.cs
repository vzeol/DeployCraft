using System.Globalization;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Avancement de la copie des fichiers vers un poste : octets copiés sur le total, et débit mesuré sur les dernières
/// secondes. Les octets sont ajoutés par la copie ; le texte est lu une fois par seconde pour le suivi.
/// </summary>
public sealed class CopyProgress(Func<DateTime>? clock = null)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly Queue<(DateTime Time, long Done)> _samples = new();
    private long _done;
    private long _total = -1;

    /// <summary>Total à copier, connu une fois la liste des fichiers établie (avant, rien n'est affiché).</summary>
    public long Total
    {
        get => Interlocked.Read(ref _total);
        set => Interlocked.Exchange(ref _total, value);
    }

    public long Done => Interlocked.Read(ref _done);

    public void Add(int bytes) => Interlocked.Add(ref _done, bytes);

    /// <summary>« Copie des fichiers : 45 % (12,3 Mo/s) », ou null tant que le total n'est pas connu.</summary>
    public string? Text()
    {
        var total = Total;
        if (total < 0) return null;
        var now = _clock();
        var done = Done;
        _samples.Enqueue((now, done));
        while (_samples.Count > 1 && now - _samples.Peek().Time > Window) _samples.Dequeue();
        var (since, from) = _samples.Peek();
        var seconds = (now - since).TotalSeconds;
        var percent = total == 0 ? 100 : (int)Math.Min(100, done * 100 / total);
        return seconds > 0
            ? $"Copie des fichiers : {percent} % ({Rate((done - from) / seconds)})"
            : $"Copie des fichiers : {percent} %";
    }

    /// <summary>Débit lisible : « 12,3 Mo/s » ou « 850 Ko/s » (1 Mo = 1 024 Ko).</summary>
    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024
            ? (bytesPerSecond / (1024 * 1024)).ToString("0.0", French) + " Mo/s"
            : (bytesPerSecond / 1024).ToString("0", French) + " Ko/s";
}

/// <summary>Fichiers à copier vers un poste, rassemblés pour connaître le total avant de commencer.</summary>
internal sealed class CopyBatch
{
    private readonly List<(string Source, string Destination)> _files = [];

    public void Add(string source, string destination) => _files.Add((source, destination));

    public long TotalBytes => _files.Sum(f => new FileInfo(f.Source).Length);

    /// <param name="ct">Annulation : la copie s'arrête en route ; le fichier à moitié copié part avec le slot.</param>
    public void Run(BandwidthLimiter limiter, CopyProgress? progress, CancellationToken ct = default)
    {
        foreach (var (source, destination) in _files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            limiter.CopyFile(source, destination, progress == null ? null : progress.Add, ct);
        }
    }
}
