using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployCraft.Tests;

/// <summary>Nettoyage automatique de l'historique, sur une base SQLite jetable.</summary>
public sealed class HistoryCleanupTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly string _db;
    private readonly Store _store;
    private readonly HistoryCleaner _cleaner;

    public HistoryCleanupTests()
    {
        _db = Path.Combine(_dir, "test.db");
        _store = new Store(_db);
        _cleaner = new HistoryCleaner(_store, NullLogger<HistoryCleaner>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private void Enable(int days, bool cleanup = true)
    {
        var defaults = _store.GetDefaults();
        defaults.History = new HistorySettings { Cleanup = cleanup, KeepDays = days };
        _store.SaveDefaults(defaults);
    }

    /// <summary>Un déploiement d'un poste, dans l'état voulu ; <paramref name="endedDaysAgo"/> null = jamais terminé.</summary>
    private long Deployment(DeploymentStatus status, double? endedDaysAgo, string computer = "PC01")
    {
        var d = _store.CreateDeployment(new Package { Id = 1, Name = "Essai" }, [new TargetRequest(computer)], "tester", "Compte du service",
            null, RunAsMode.LocalSystem, CopyMode.Push, prioritized: false);
        if (status != DeploymentStatus.Queued) _store.UpdateDeploymentStatus(d.Id, status, ended: false);
        if (endedDaysAgo is { } days) SetEnded(d.Id, status, Now.AddDays(-days));
        return d.Id;
    }

    /// <summary>La date de fin est écrite par le service à l'instant T : on la recule directement en base.</summary>
    private void SetEnded(long id, DeploymentStatus status, DateTime endedUtc)
    {
        using var db = new SqliteConnection($"Data Source={_db}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE deployments SET status = $s, ended_utc = $e WHERE id = $id";
        cmd.Parameters.AddWithValue("$s", status.ToString());
        cmd.Parameters.AddWithValue("$e", endedUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private long TargetRows(long deploymentId)
    {
        using var db = new SqliteConnection($"Data Source={_db}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM deployment_targets WHERE deployment_id = $id";
        cmd.Parameters.AddWithValue("$id", deploymentId);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Disabled_DeletesNothing()
    {
        var old = Deployment(DeploymentStatus.Successful, endedDaysAgo: 400);
        Enable(30, cleanup: false);

        Assert.Equal(0, _cleaner.Run(Now));
        Assert.NotNull(_store.GetDeployment(old));
    }

    [Fact]
    public void NeverConfigured_DeletesNothing()
    {
        var old = Deployment(DeploymentStatus.Failed, endedDaysAgo: 400);

        Assert.Equal(0, _cleaner.Run(Now));
        Assert.NotNull(_store.GetDeployment(old));
    }

    [Fact]
    public void Enabled_DeletesOnlyFinishedDeploymentsOlderThanTheLimit()
    {
        var oldSuccess = Deployment(DeploymentStatus.Successful, endedDaysAgo: 40);
        var oldFailure = Deployment(DeploymentStatus.Failed, endedDaysAgo: 31);
        var recentSuccess = Deployment(DeploymentStatus.Successful, endedDaysAgo: 29);
        var recentFailure = Deployment(DeploymentStatus.Failed, endedDaysAgo: 0);
        var oldRunning = Deployment(DeploymentStatus.Running, endedDaysAgo: null);
        var oldQueued = Deployment(DeploymentStatus.Queued, endedDaysAgo: null);
        Enable(30);

        Assert.Equal(2, _cleaner.Run(Now));

        Assert.Null(_store.GetDeployment(oldSuccess));
        Assert.Null(_store.GetDeployment(oldFailure));
        Assert.NotNull(_store.GetDeployment(recentSuccess));
        Assert.NotNull(_store.GetDeployment(recentFailure));
        Assert.NotNull(_store.GetDeployment(oldRunning));
        Assert.NotNull(_store.GetDeployment(oldQueued));
    }

    [Fact]
    public void RunningDeployment_IsKept_EvenIfItWasCreatedLongAgo()
    {
        // Un déploiement toujours en cours après des semaines n'a pas de date de fin : jamais supprimé.
        var running = Deployment(DeploymentStatus.Running, endedDaysAgo: null);
        SetEnded(running, DeploymentStatus.Running, Now.AddDays(-500));
        Enable(1);

        Assert.Equal(0, _cleaner.Run(Now));
        Assert.NotNull(_store.GetDeployment(running));
    }

    [Fact]
    public void DeletedDeployments_TakeTheirTargetsWithThem_AndOthersKeepTheirs()
    {
        var old = Deployment(DeploymentStatus.Successful, endedDaysAgo: 90, computer: "PC-ANCIEN");
        var recent = Deployment(DeploymentStatus.Successful, endedDaysAgo: 1, computer: "PC-RECENT");
        Enable(30);

        _cleaner.Run(Now);

        Assert.Equal(0, TargetRows(old));
        Assert.Equal(1, TargetRows(recent));
    }

    [Fact]
    public void LimitIsExact_OneMinuteEitherSide()
    {
        var justInside = Deployment(DeploymentStatus.Successful, endedDaysAgo: null);
        SetEnded(justInside, DeploymentStatus.Successful, Now.AddDays(-30).AddMinutes(1));
        var justOutside = Deployment(DeploymentStatus.Successful, endedDaysAgo: null);
        SetEnded(justOutside, DeploymentStatus.Successful, Now.AddDays(-30).AddMinutes(-1));
        Enable(30);

        Assert.Equal(1, _cleaner.Run(Now));
        Assert.NotNull(_store.GetDeployment(justInside));
        Assert.Null(_store.GetDeployment(justOutside));
    }

    [Fact]
    public void ScheduleHistory_IsNeverTouched()
    {
        _store.RecordScheduleResult(1, "PC01", 7, success: true);
        Deployment(DeploymentStatus.Successful, endedDaysAgo: 365);
        Enable(1);

        Assert.Equal(1, _cleaner.Run(Now));

        var line = Assert.Single(_store.GetScheduleHistory(1));
        Assert.True(line.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANumberOfDaysBelowTheMinimum_NeverDeletesMoreThanOneDayOfHistory(int days)
    {
        var lastHour = Deployment(DeploymentStatus.Successful, endedDaysAgo: null);
        SetEnded(lastHour, DeploymentStatus.Successful, Now.AddHours(-1));
        var twoDaysAgo = Deployment(DeploymentStatus.Successful, endedDaysAgo: 2);
        Enable(days);

        Assert.Equal(1, _cleaner.Run(Now));
        Assert.NotNull(_store.GetDeployment(lastHour));
        Assert.Null(_store.GetDeployment(twoDaysAgo));
    }

    [Fact]
    public void Defaults_StoredBeforeTheSettingExisted_MeanDisabled()
    {
        _store.SetSetting("defaults", """{"timeoutMinutes":45}""");

        var history = _store.GetDefaults().History;

        Assert.False(history.Cleanup);
        Assert.Equal(30, history.KeepDays);
        Assert.Equal(45, _store.GetDefaults().TimeoutMinutes);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(3650, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(3651, false)]
    public void Validate_AcceptsOneToTenYears(int days, bool valid) =>
        Assert.Equal(valid, HistorySettings.Validate(days) == null);
}
