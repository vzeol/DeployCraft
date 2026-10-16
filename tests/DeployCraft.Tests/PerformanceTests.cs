using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Préférences › Déploiement : postes en parallèle et limite de débit des copies vers les postes.</summary>
public sealed class PerformanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deploycraft-perf-").FullName;
    private DateTime _now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Limiteur sur une horloge simulée : chaque attente fait avancer l'horloge, sans attendre pour de vrai.</summary>
    private BandwidthLimiter Limiter(long bytesPerSecond) =>
        new(() => _now, wait => _now += wait) { BytesPerSecond = bytesPerSecond };

    [Fact]
    public void Copy_TakesAsLongAsTheLimitRequires()
    {
        var limiter = Limiter(1_000_000);
        var start = _now;
        var output = new MemoryStream();

        limiter.Copy(new MemoryStream(new byte[10_000_000]), output);

        Assert.Equal(10_000_000, output.Length);
        Assert.InRange((_now - start).TotalSeconds, 9.9, 10.1);
    }

    [Fact]
    public void Limit_IsSharedByAllCopiesInProgress()
    {
        var limiter = Limiter(1_000_000);
        var start = _now;

        // Deux postes copient en même temps, morceau par morceau : le total reste à 1 Mo/s.
        for (var i = 0; i < 20; i++)
        {
            limiter.Take(250_000);
            limiter.Take(250_000);
        }

        Assert.InRange((_now - start).TotalSeconds, 9.9, 10.1);
    }

    [Fact]
    public void IdleTime_GivesAtMostOneSecondOfReserve()
    {
        var limiter = Limiter(1_000_000);
        limiter.Take(1);
        _now += TimeSpan.FromMinutes(5);
        var start = _now;

        limiter.Take(1_000_000); // la réserve d'une seconde suffit
        Assert.Equal(start, _now);
        limiter.Take(500_000);   // ensuite, le débit redevient la limite
        Assert.InRange((_now - start).TotalSeconds, 0.49, 0.51);
    }

    [Fact]
    public void NoLimit_NeverWaits()
    {
        var limiter = Limiter(0);
        var start = _now;
        limiter.Copy(new MemoryStream(new byte[5_000_000]), new MemoryStream());
        Assert.Equal(start, _now);
    }

    [Fact]
    public void CopyFile_WithALimit_KeepsContentAndDate()
    {
        var source = Path.Combine(_dir, "source.bin");
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(source, bytes);
        File.SetLastWriteTimeUtc(source, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var destination = Path.Combine(_dir, "copie.bin");

        Limiter(1_000_000).CopyFile(source, destination);

        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public void Mbps_AreBitsPerSecond()
    {
        Assert.Equal(12_500_000, BandwidthLimiter.FromMbps(100));
        Assert.Equal(0, BandwidthLimiter.FromMbps(0));
    }

    [Theory]
    [InlineData(8, 0, null)]
    [InlineData(1, 10000, null)]
    [InlineData(100, 50, null)]
    [InlineData(0, 0, "postes en parallèle")]
    [InlineData(101, 0, "postes en parallèle")]
    [InlineData(8, -1, "limite de copie")]
    [InlineData(8, 10001, "limite de copie")]
    public void Validate_ChecksTheLimits(int parallel, int limit, string? error)
    {
        var message = PerformanceSettings.Validate(new PerformanceSettings { ParallelTargets = parallel, CopyLimitMbps = limit });
        if (error == null) Assert.Null(message);
        else Assert.Contains(error, message);
    }

    [Fact]
    public void Settings_DefaultTo8Targets_AndNoLimit_AndAreSavedWithTheDefaults()
    {
        var store = new Store(Path.Combine(_dir, "test.db"));
        Assert.Equal(8, store.GetDefaults().Performance.ParallelTargets);
        Assert.Equal(0, store.GetDefaults().Performance.CopyLimitMbps);

        var defaults = store.GetDefaults();
        defaults.Performance = new PerformanceSettings { ParallelTargets = 20, CopyLimitMbps = 100 };
        store.SaveDefaults(defaults);

        Assert.Equal(20, store.GetDefaults().Performance.ParallelTargets);
        Assert.Equal(100, store.GetDefaults().Performance.CopyLimitMbps);
    }
}
