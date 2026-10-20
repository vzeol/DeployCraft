using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>Deployments and their target computers.</summary>
public interface IDeploymentRepository
{
    Deployment Create(Package package, IReadOnlyList<TargetRequest> targets, string? launchedBy, string? runAs, long? credentialId, RunAsMode runAsMode, CopyMode copyMode, bool prioritized, Schedule? schedule = null);
    List<Deployment> List(int limit = 100);
    Deployment? Get(long id);
    void FailInterrupted();
    void Delete(long id);
    int PurgeFinished(DateTime endedBeforeUtc);
    void UpdateStatus(long id, DeploymentStatus status, bool ended);
    void UpdateTarget(DeploymentTarget t);
}

/// <summary>SQLite storage of <see cref="IDeploymentRepository"/>.</summary>
public sealed class DeploymentRepository(Database database) : IDeploymentRepository
{
    public Deployment Create(Package package, IReadOnlyList<TargetRequest> targets, string? launchedBy, string? runAs, long? credentialId,
        RunAsMode runAsMode, CopyMode copyMode, bool prioritized, Schedule? schedule = null)
    {
        var startStep = targets.Count > 0 ? targets.Min(t => t.StartStep) : 1;
        var d = new Deployment
        {
            PackageId = package.Id,
            PackageName = package.Name,
            Snapshot = package,
            Status = DeploymentStatus.Queued,
            LaunchedBy = launchedBy,
            RunAs = runAs,
            CredentialId = credentialId,
            StartStep = startStep,
            RunAsMode = runAsMode,
            CopyMode = copyMode,
            CreatedUtc = DateTime.UtcNow,
            ScheduleId = schedule?.Id,
            ScheduleName = schedule?.Name,
            QueueTimeoutMinutes = schedule?.QueueTimeoutMinutes,
        };

        using var connection = database.Open();
        using var tx = connection.BeginTransaction();
        using (var cmd = Database.Command(connection, """
            INSERT INTO deployments (package_id, package_name, snapshot, status, created_utc, launched_by, run_as, start_step, run_as_mode, copy_mode,
                                     schedule_id, schedule_name, queue_timeout, credential_id)
            VALUES ($pkg, $name, $snap, $status, $created, $by, $runas, $start, $mode, $copy, $sid, $sname, $qt, $cred);
            SELECT last_insert_rowid();
            """,
            ("$pkg", package.Id), ("$name", package.Name), ("$snap", JsonSerializer.Serialize(package, Database.Json)),
            ("$status", d.Status.ToString()), ("$created", d.CreatedUtc.ToString("o")),
            ("$by", launchedBy), ("$runas", runAs), ("$start", startStep),
            ("$mode", runAsMode.ToString()), ("$copy", copyMode.ToString()),
            ("$sid", d.ScheduleId), ("$sname", d.ScheduleName), ("$qt", d.QueueTimeoutMinutes), ("$cred", credentialId)))
        {
            cmd.Transaction = tx;
            d.Id = (long)cmd.ExecuteScalar()!;
        }

        foreach (var target in targets)
        {
            using var cmd = Database.Command(connection, """
                INSERT INTO deployment_targets (deployment_id, computer, status, start_step, prioritized) VALUES ($d, $c, $s, $start, $prio);
                SELECT last_insert_rowid();
                """, ("$d", d.Id), ("$c", target.Computer), ("$s", TargetStatus.Queued.ToString()),
                ("$start", target.StartStep), ("$prio", prioritized ? 1 : 0));
            cmd.Transaction = tx;
            d.Targets.Add(new DeploymentTarget
            {
                Id = (long)cmd.ExecuteScalar()!,
                DeploymentId = d.Id,
                Computer = target.Computer,
                Status = TargetStatus.Queued,
                StartStep = target.StartStep,
                Prioritized = prioritized,
            });
        }
        tx.Commit();
        return d;
    }

    private const string DeploymentColumns =
        "id, package_id, package_name, snapshot, status, created_utc, ended_utc, launched_by, run_as, start_step, run_as_mode, copy_mode, " +
        "schedule_id, schedule_name, queue_timeout, credential_id";

    public List<Deployment> List(int limit = 100)
    {
        using var connection = database.Open();
        var list = new List<Deployment>();
        using (var cmd = Database.Command(connection, $"SELECT {DeploymentColumns} FROM deployments ORDER BY id DESC LIMIT $limit", ("$limit", limit)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) list.Add(ReadDeployment(r));
        }
        foreach (var d in list) d.Targets = ListTargets(connection, d.Id);
        return list;
    }

    public Deployment? Get(long id)
    {
        using var connection = database.Open();
        Deployment? d;
        using (var cmd = Database.Command(connection, $"SELECT {DeploymentColumns} FROM deployments WHERE id = $id", ("$id", id)))
        using (var r = cmd.ExecuteReader())
        {
            d = r.Read() ? ReadDeployment(r) : null;
        }
        if (d != null) d.Targets = ListTargets(connection, d.Id);
        return d;
    }

    /// <summary>Déploiements interrompus par un arrêt du serveur : on les clôt proprement au démarrage.</summary>
    public void FailInterrupted()
    {
        using var connection = database.Open();
        Database.Exec(connection, $"""
            UPDATE deployment_targets SET status = 'Failed', error = 'Interrompu : le serveur DeployCraft a été arrêté.'
            WHERE status IN ('Queued', 'Running');
            UPDATE deployments SET status = 'Failed', ended_utc = '{DateTime.UtcNow:o}'
            WHERE status IN ('Queued', 'Running');
            """);
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            DELETE FROM deployment_targets WHERE deployment_id = $id;
            DELETE FROM deployments WHERE id = $id;
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Supprime les déploiements terminés (réussis ou en échec) dont la fin est antérieure à <paramref name="endedBeforeUtc"/>
    /// (UTC), avec leurs postes. Un déploiement en attente ou en cours n'est jamais touché, ni l'historique des
    /// planifications. Renvoie le nombre de déploiements supprimés.
    /// </summary>
    public int PurgeFinished(DateTime endedBeforeUtc)
    {
        using var connection = database.Open();
        using var tx = connection.BeginTransaction();
        var cutoff = endedBeforeUtc.ToString("o");
        const string finished = "status IN ('Successful', 'Failed') AND ended_utc IS NOT NULL AND ended_utc < $cutoff";

        using (var targets = Database.Command(connection, $"DELETE FROM deployment_targets WHERE deployment_id IN (SELECT id FROM deployments WHERE {finished})",
                   ("$cutoff", cutoff)))
        {
            targets.Transaction = tx;
            targets.ExecuteNonQuery();
        }
        int deleted;
        using (var deployments = Database.Command(connection, $"DELETE FROM deployments WHERE {finished}", ("$cutoff", cutoff)))
        {
            deployments.Transaction = tx;
            deleted = deployments.ExecuteNonQuery();
        }
        tx.Commit();
        return deleted;
    }

    public void UpdateStatus(long id, DeploymentStatus status, bool ended)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection,
            ended ? "UPDATE deployments SET status=$s, ended_utc=$e WHERE id=$id" : "UPDATE deployments SET status=$s WHERE id=$id",
            ("$id", id), ("$s", status.ToString()), ("$e", DateTime.UtcNow.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public void UpdateTarget(DeploymentTarget t)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            UPDATE deployment_targets
            SET status=$s, stage=$stage, error=$err, steps=$steps, started_utc=$start, ended_utc=$end
            WHERE id=$id
            """,
            ("$id", t.Id), ("$s", t.Status.ToString()), ("$stage", t.Stage), ("$err", t.Error),
            ("$steps", JsonSerializer.Serialize(t.Steps, Database.Json)),
            ("$start", t.StartedUtc?.ToString("o")), ("$end", t.EndedUtc?.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    private static List<DeploymentTarget> ListTargets(SqliteConnection connection, long deploymentId)
    {
        using var cmd = Database.Command(connection, """
            SELECT id, deployment_id, computer, status, stage, error, steps, started_utc, ended_utc, start_step, prioritized
            FROM deployment_targets WHERE deployment_id = $d ORDER BY computer COLLATE NOCASE
            """, ("$d", deploymentId));
        using var r = cmd.ExecuteReader();
        var list = new List<DeploymentTarget>();
        while (r.Read())
        {
            list.Add(new DeploymentTarget
            {
                Id = r.GetInt64(0),
                DeploymentId = r.GetInt64(1),
                Computer = r.GetString(2),
                Status = Enum.Parse<TargetStatus>(r.GetString(3)),
                Stage = r.IsDBNull(4) ? null : r.GetString(4),
                Error = r.IsDBNull(5) ? null : r.GetString(5),
                Steps = JsonSerializer.Deserialize<List<StepOutcome>>(r.GetString(6), Database.Json) ?? [],
                StartedUtc = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)).ToUniversalTime(),
                EndedUtc = r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8)).ToUniversalTime(),
                StartStep = r.GetInt32(9),
                Prioritized = r.GetInt64(10) != 0,
            });
        }
        return list;
    }

    private static Deployment ReadDeployment(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        PackageId = r.GetInt64(1),
        PackageName = r.GetString(2),
        Snapshot = JsonSerializer.Deserialize<Package>(r.GetString(3), Database.Json) ?? new(),
        Status = Enum.Parse<DeploymentStatus>(r.GetString(4)),
        CreatedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
        EndedUtc = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)).ToUniversalTime(),
        LaunchedBy = r.IsDBNull(7) ? null : r.GetString(7),
        RunAs = r.IsDBNull(8) ? null : r.GetString(8),
        StartStep = r.GetInt32(9),
        RunAsMode = Enum.TryParse<RunAsMode>(r.GetString(10), out var mode) ? mode : RunAsMode.LocalSystem,
        CopyMode = Enum.TryParse<CopyMode>(r.GetString(11), out var copy) ? copy : CopyMode.Push,
        ScheduleId = r.IsDBNull(12) ? null : r.GetInt64(12),
        ScheduleName = r.IsDBNull(13) ? null : r.GetString(13),
        QueueTimeoutMinutes = r.IsDBNull(14) ? null : r.GetInt32(14),
        CredentialId = r.IsDBNull(15) ? null : r.GetInt64(15),
    };
}
