namespace DeployCraft.Server.Remote;

/// <summary>
/// Rate limit shared by all file copies from the server to the computers: token bucket with one
/// second of reserve. Each copied chunk reserves its share and waits if the bucket is overdrawn; the total of
/// running copies therefore never exceeds the limit, whatever the number of computers.
/// </summary>
public sealed class BandwidthLimiter(Func<DateTime>? clock = null, Action<TimeSpan, CancellationToken>? sleep = null)
{
    /// <summary>Limit of the copies to the computers, set in Preferences.</summary>
    public static BandwidthLimiter Copies { get; } = new();

    private const int BufferSize = 256 * 1024;

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    /// <summary>Wait interrupted as soon as cancelled.</summary>
    private readonly Action<TimeSpan, CancellationToken> _sleep = sleep ?? ((wait, ct) => ct.WaitHandle.WaitOne(wait));
    private readonly object _lock = new();
    private double _tokens;
    private DateTime? _last;
    private long _rate;

    /// <summary>Maximum rate in bytes per second (0 = unlimited).</summary>
    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _rate);
        set => Interlocked.Exchange(ref _rate, Math.Max(0, value));
    }

    public static long FromMbps(int mbps) => mbps * 125_000L;

    /// <summary>Reserves <paramref name="bytes"/> bytes; waits as long as needed if the limit is reached.</summary>
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

    /// <param name="copied">Called after each chunk written, with its size (progress tracking).</param>
    /// <param name="ct">Cancellation checked between chunks and during waits: the copy stops midway.</param>
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
    /// Copies a file (overwrites the destination). With no limit and no progress tracking, uses the system copy, which is
    /// faster; otherwise copies by chunks.
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
        // Same date as the source, like the system copy (the file copy on the computer relies on it).
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
    }
}
