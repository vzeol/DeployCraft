using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Suivi d'un poste : avancement de la copie, exécutions et note de chaque étape.</summary>
public sealed class CopyProgressTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deploycraft-progress-").FullName;
    private DateTime _now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Text_ShowsPercentAndRecentRate()
    {
        var progress = new CopyProgress(() => _now);
        Assert.Null(progress.Text()); // total pas encore connu

        progress.Total = 100L * 1024 * 1024;
        Assert.Equal("Copie des fichiers : 0 %", progress.Text());

        _now += TimeSpan.FromSeconds(1);
        progress.Add(12 * 1024 * 1024);
        Assert.Equal("Copie des fichiers : 12 % (12,0 Mo/s)", progress.Text());

        _now += TimeSpan.FromSeconds(1);
        progress.Add(33 * 1024 * 1024);
        Assert.Equal("Copie des fichiers : 45 % (22,5 Mo/s)", progress.Text());
    }

    [Fact]
    public void Rate_ForgetsOlderThanThreeSeconds()
    {
        var progress = new CopyProgress(() => _now) { Total = 1_000_000_000 };
        progress.Text();
        progress.Add(500 * 1024 * 1024); // rafale au début
        for (var i = 0; i < 5; i++)
        {
            _now += TimeSpan.FromSeconds(1);
            progress.Add(512 * 1024);
            progress.Text();
        }
        _now += TimeSpan.FromSeconds(1);
        progress.Add(512 * 1024);

        Assert.EndsWith("(512 Ko/s)", progress.Text());
    }

    [Theory]
    [InlineData(0, "0 Ko/s")]
    [InlineData(870_400, "850 Ko/s")]
    [InlineData(12_897_484.8, "12,3 Mo/s")]
    public void Rate_IsReadable(double bytesPerSecond, string expected) =>
        Assert.Equal(expected, CopyProgress.Rate(bytesPerSecond));

    [Fact]
    public void Batch_CountsEveryByteCopied_EvenWithoutLimit()
    {
        var a = Path.Combine(_dir, "a.bin");
        var b = Path.Combine(_dir, "sous", "b.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllBytes(a, new byte[700_000]);
        File.WriteAllBytes(b, new byte[300_000]);
        var batch = new CopyBatch();
        batch.Add(a, Path.Combine(_dir, "exec", "a.bin"));
        batch.Add(b, Path.Combine(_dir, "exec", "sous", "b.bin"));
        var progress = new CopyProgress(() => _now) { Total = batch.TotalBytes };

        batch.Run(new BandwidthLimiter(), progress);

        Assert.Equal(1_000_000, progress.Total);
        Assert.Equal(1_000_000, progress.Done);
        Assert.Equal(300_000, new FileInfo(Path.Combine(_dir, "exec", "sous", "b.bin")).Length);
        Assert.Equal("Copie des fichiers : 100 %", progress.Text());
    }

    [Fact]
    public void Runs_CountThisAttempt_PlusEarlierOnes()
    {
        var status = new RunnerStatus
        {
            Steps =
            [
                new() { Name = "a", State = "success", StartedUtc = _now, EndedUtc = _now },
                new() { Name = "b", State = "running", StartedUtc = _now },
                new() { Name = "c", State = "skipped", StartedUtc = _now, Error = "Étape désactivée." },
            ],
        };

        Assert.Equal([1, 1, 0], DeploymentEngine.ToOutcomes(status).Select(s => s.Runs));
        Assert.Equal([2, 1, 0], DeploymentEngine.ToOutcomes(status, [1, 0, 0]).Select(s => s.Runs));
    }

    [Theory]
    [InlineData("skipped", null, "Condition non remplie. Version de Windows", "Condition non remplie. Version de Windows")]
    [InlineData("skipped", null, null, "Ignorée.")]
    [InlineData("failed", 1603, null, "Code 1603 hors des codes de succès.")]
    [InlineData("failed", null, "Délai dépassé (60 min).\nDétails", "Délai dépassé (60 min).")]
    [InlineData("stopped", 1, null, "Paquet arrêté en succès.")]
    [InlineData("success", 3010, null, "Réussie avec le code 3010.")]
    [InlineData("success", 0, null, "")]
    [InlineData("running", null, null, "")]
    public void Note_SummarizesTheResult(string state, int? exitCode, string? error, string expected) =>
        Assert.Equal(expected, new StepOutcome { State = state, ExitCode = exitCode, Error = error }.Note);

    [Fact]
    public void Note_IsShortenedAndNotStored()
    {
        var outcome = new StepOutcome { State = "failed", Error = new string('x', 300) };
        Assert.Equal(160, outcome.Note.Length);
        Assert.DoesNotContain("Note", System.Text.Json.JsonSerializer.Serialize(outcome));
    }
}
