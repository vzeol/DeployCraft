using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>
/// Stockage SQLite. Les structures imbriquées (étapes, résultats) sont rangées en JSON :
/// le schéma reste minuscule et facile à faire évoluer.
/// </summary>
public class Store
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;

    public Store(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var db = Open();
        Exec(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS packages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                description TEXT,
                steps TEXT NOT NULL,
                modified_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS deployments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                package_id INTEGER NOT NULL,
                package_name TEXT NOT NULL,
                snapshot TEXT NOT NULL,
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                ended_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS deployment_targets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                deployment_id INTEGER NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
                computer TEXT NOT NULL,
                status TEXT NOT NULL,
                stage TEXT,
                error TEXT,
                steps TEXT NOT NULL DEFAULT '[]',
                started_utc TEXT,
                ended_utc TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_targets_deployment ON deployment_targets(deployment_id);
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
            """);

        // Colonnes ajoutées en 0.3 : les bases existantes sont complétées sans perte.
        AddColumnIfMissing(db, "deployments", "launched_by", "TEXT");
        AddColumnIfMissing(db, "deployments", "run_as", "TEXT");
        AddColumnIfMissing(db, "deployments", "start_step", "INTEGER NOT NULL DEFAULT 1");

        // 0.6 : exécuter en tant que, mode de copie, étape de départ et priorité par poste.
        AddColumnIfMissing(db, "packages", "run_as", "TEXT NOT NULL DEFAULT 'DeployUser'");
        AddColumnIfMissing(db, "packages", "copy_mode", "TEXT NOT NULL DEFAULT 'Push'");
        // Avant 0.6, le service temporaire tournait toujours en Système local.
        AddColumnIfMissing(db, "deployments", "run_as_mode", "TEXT NOT NULL DEFAULT 'LocalSystem'");
        AddColumnIfMissing(db, "deployments", "copy_mode", "TEXT NOT NULL DEFAULT 'Push'");
        if (AddColumnIfMissing(db, "deployment_targets", "start_step", "INTEGER NOT NULL DEFAULT 1"))
            Exec(db, "UPDATE deployment_targets SET start_step = (SELECT start_step FROM deployments WHERE deployments.id = deployment_id)");
        AddColumnIfMissing(db, "deployment_targets", "prioritized", "INTEGER NOT NULL DEFAULT 0");

        // 0.8 : planifications. Le réglage complet est rangé en JSON ; l'historique est par poste et par paquet.
        Exec(db, """
            CREATE TABLE IF NOT EXISTS schedules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                data TEXT NOT NULL,
                last_checked TEXT,
                last_run_utc TEXT,
                last_result TEXT,
                modified_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS schedule_history (
                schedule_id INTEGER NOT NULL,
                computer TEXT NOT NULL COLLATE NOCASE,
                package_id INTEGER NOT NULL,
                succeeded INTEGER NOT NULL DEFAULT 0,
                failures INTEGER NOT NULL DEFAULT 0,
                last_utc TEXT,
                PRIMARY KEY (schedule_id, computer, package_id)
            );
            """);
        AddColumnIfMissing(db, "deployments", "schedule_id", "INTEGER");
        AddColumnIfMissing(db, "deployments", "schedule_name", "TEXT");
        AddColumnIfMissing(db, "deployments", "queue_timeout", "INTEGER");

        // 0.9 : plusieurs identifiants ; le mot de passe est rangé chiffré (voir DeployCredentials).
        Exec(db, """
            CREATE TABLE IF NOT EXISTS credentials (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain TEXT,
                user TEXT NOT NULL,
                description TEXT,
                password TEXT,
                is_default INTEGER NOT NULL DEFAULT 0
            );
            """);
        AddColumnIfMissing(db, "deployments", "credential_id", "INTEGER");

        // 0.10 : version, délai, conditions et réglages hors ligne du paquet ; run_as / copy_mode « Default » = Préférences.
        AddColumnIfMissing(db, "packages", "version", "TEXT");
        AddColumnIfMissing(db, "packages", "timeout", "INTEGER");
        AddColumnIfMissing(db, "packages", "conditions", "TEXT");
        AddColumnIfMissing(db, "packages", "offline", "TEXT");
        // Adresses MAC apprises lors des déploiements, pour le Wake-on-LAN.
        Exec(db, """
            CREATE TABLE IF NOT EXISTS computers (
                name TEXT PRIMARY KEY COLLATE NOCASE,
                network TEXT NOT NULL,
                seen_utc TEXT NOT NULL
            );
            """);
    }

    /// <summary>Renvoie true si la colonne vient d'être ajoutée.</summary>
    private static bool AddColumnIfMissing(SqliteConnection db, string table, string column, string type)
    {
        using (var cmd = Command(db, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $c", ("$c", column)))
        {
            if ((long)cmd.ExecuteScalar()! > 0) return false;
        }
        Exec(db, $"ALTER TABLE {table} ADD COLUMN {column} {type}");
        return true;
    }

    // ---------- Réglages ----------

    public string? GetSetting(string key)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT value FROM settings WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string? value)
    {
        using var db = Open();
        using var cmd = Command(db, """
            INSERT INTO settings (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """, ("$k", key), ("$v", value));
        cmd.ExecuteNonQuery();
    }

    // ---------- Paquets ----------

    private const string PackageColumns = "id, name, description, steps, modified_utc, run_as, copy_mode, version, timeout, conditions, offline";

    public List<Package> ListPackages()
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {PackageColumns} FROM packages ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        var list = new List<Package>();
        while (r.Read()) list.Add(ReadPackage(r));
        return list;
    }

    public Package? GetPackage(long id)
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {PackageColumns} FROM packages WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadPackage(r) : null;
    }

    public Package SavePackage(Package p)
    {
        p.ModifiedUtc = DateTime.UtcNow;
        using var db = Open();
        var args = new (string, object?)[]
        {
            ("$id", p.Id), ("$name", p.Name), ("$desc", p.Description),
            ("$steps", JsonSerializer.Serialize(p.Steps, Json)), ("$mod", p.ModifiedUtc.ToString("o")),
            ("$runas", p.RunAs?.ToString() ?? DefaultValue), ("$copy", p.CopyMode?.ToString() ?? DefaultValue),
            ("$version", string.IsNullOrWhiteSpace(p.Version) ? null : p.Version.Trim()), ("$timeout", p.TimeoutMinutes),
            ("$conditions", JsonSerializer.Serialize(p.Conditions, Json)),
            ("$offline", p.Offline == null ? null : JsonSerializer.Serialize(p.Offline, Json)),
        };
        if (p.Id == 0)
        {
            using var cmd = Command(db, """
                INSERT INTO packages (name, description, steps, modified_utc, run_as, copy_mode, version, timeout, conditions, offline)
                VALUES ($name, $desc, $steps, $mod, $runas, $copy, $version, $timeout, $conditions, $offline);
                SELECT last_insert_rowid();
                """, args);
            p.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Command(db, """
                UPDATE packages SET name=$name, description=$desc, steps=$steps, modified_utc=$mod, run_as=$runas, copy_mode=$copy,
                    version=$version, timeout=$timeout, conditions=$conditions, offline=$offline
                WHERE id=$id
                """, args);
            cmd.ExecuteNonQuery();
        }
        return p;
    }

    public void DeletePackage(long id)
    {
        using var db = Open();
        using var cmd = Command(db, "DELETE FROM packages WHERE id = $id", ("$id", id));
        cmd.ExecuteNonQuery();
        SetSetting($"package.{id}.targets", null);
    }

    private static Package ReadPackage(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        Steps = JsonSerializer.Deserialize<List<PackageStep>>(r.GetString(3), Json) ?? [],
        ModifiedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
        RunAs = Enum.TryParse<RunAsMode>(r.GetString(5), out var runAs) ? runAs : null,
        CopyMode = Enum.TryParse<CopyMode>(r.GetString(6), out var copy) ? copy : null,
        Version = r.IsDBNull(7) ? null : r.GetString(7),
        TimeoutMinutes = r.IsDBNull(8) ? null : r.GetInt32(8),
        Conditions = r.IsDBNull(9) ? new() : JsonSerializer.Deserialize<Conditions>(r.GetString(9), Json) ?? new(),
        Offline = r.IsDBNull(10) ? null : JsonSerializer.Deserialize<OfflineSettings>(r.GetString(10), Json),
    };

    /// <summary>Valeur rangée dans run_as / copy_mode quand le paquet suit les Préférences.</summary>
    private const string DefaultValue = "Default";

    // ---------- Préférences de déploiement ----------

    public DeploymentDefaults GetDefaults() =>
        GetSetting("defaults") is { } json ? JsonSerializer.Deserialize<DeploymentDefaults>(json, Json) ?? new() : new();

    public void SaveDefaults(DeploymentDefaults defaults) => SetSetting("defaults", JsonSerializer.Serialize(defaults, Json));

    // ---------- Postes connus (Wake-on-LAN) ----------

    /// <summary>Retient les cartes réseau d'un poste (adresse MAC et diffusion de son sous-réseau).</summary>
    public void SaveComputerNetwork(string computer, IReadOnlyCollection<NetworkCard> cards)
    {
        if (cards.Count == 0) return;
        using var db = Open();
        using var cmd = Command(db, """
            INSERT INTO computers (name, network, seen_utc) VALUES ($n, $j, $s)
            ON CONFLICT(name) DO UPDATE SET network = excluded.network, seen_utc = excluded.seen_utc
            """, ("$n", computer), ("$j", JsonSerializer.Serialize(cards, Json)), ("$s", DateTime.UtcNow.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public List<NetworkCard> GetComputerNetwork(string computer)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT network FROM computers WHERE name = $n", ("$n", computer));
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<List<NetworkCard>>(json, Json) ?? [] : [];
    }

    /// <summary>Cibles mémorisées pour un paquet (« Mémoriser ces cibles » de la fenêtre Déployer une fois).</summary>
    public List<string> GetPackageTargets(long packageId) =>
        GetSetting($"package.{packageId}.targets") is { } json
            ? JsonSerializer.Deserialize<List<string>>(json, Json) ?? []
            : [];

    public void SetPackageTargets(long packageId, IReadOnlyCollection<string> computers) =>
        SetSetting($"package.{packageId}.targets", computers.Count == 0 ? null : JsonSerializer.Serialize(computers, Json));

    // ---------- Déploiements ----------

    public Deployment CreateDeployment(Package package, IReadOnlyList<TargetRequest> targets, string? launchedBy, string? runAs, long? credentialId,
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

        using var db = Open();
        using var tx = db.BeginTransaction();
        using (var cmd = Command(db, """
            INSERT INTO deployments (package_id, package_name, snapshot, status, created_utc, launched_by, run_as, start_step, run_as_mode, copy_mode,
                                     schedule_id, schedule_name, queue_timeout, credential_id)
            VALUES ($pkg, $name, $snap, $status, $created, $by, $runas, $start, $mode, $copy, $sid, $sname, $qt, $cred);
            SELECT last_insert_rowid();
            """,
            ("$pkg", package.Id), ("$name", package.Name), ("$snap", JsonSerializer.Serialize(package, Json)),
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
            using var cmd = Command(db, """
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

    public List<Deployment> ListDeployments(int limit = 100)
    {
        using var db = Open();
        var list = new List<Deployment>();
        using (var cmd = Command(db, $"SELECT {DeploymentColumns} FROM deployments ORDER BY id DESC LIMIT $limit", ("$limit", limit)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) list.Add(ReadDeployment(r));
        }
        foreach (var d in list) d.Targets = ListTargets(db, d.Id);
        return list;
    }

    public Deployment? GetDeployment(long id)
    {
        using var db = Open();
        Deployment? d;
        using (var cmd = Command(db, $"SELECT {DeploymentColumns} FROM deployments WHERE id = $id", ("$id", id)))
        using (var r = cmd.ExecuteReader())
        {
            d = r.Read() ? ReadDeployment(r) : null;
        }
        if (d != null) d.Targets = ListTargets(db, d.Id);
        return d;
    }

    /// <summary>Déploiements interrompus par un arrêt du serveur : on les clôt proprement au démarrage.</summary>
    public void FailInterrupted()
    {
        using var db = Open();
        Exec(db, $"""
            UPDATE deployment_targets SET status = 'Failed', error = 'Interrompu : le serveur DeployCraft a été arrêté.'
            WHERE status IN ('Queued', 'Running');
            UPDATE deployments SET status = 'Failed', ended_utc = '{DateTime.UtcNow:o}'
            WHERE status IN ('Queued', 'Running');
            """);
    }

    public void DeleteDeployment(long id)
    {
        using var db = Open();
        using var cmd = Command(db, """
            DELETE FROM deployment_targets WHERE deployment_id = $id;
            DELETE FROM deployments WHERE id = $id;
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    public void UpdateDeploymentStatus(long id, DeploymentStatus status, bool ended)
    {
        using var db = Open();
        using var cmd = Command(db,
            ended ? "UPDATE deployments SET status=$s, ended_utc=$e WHERE id=$id" : "UPDATE deployments SET status=$s WHERE id=$id",
            ("$id", id), ("$s", status.ToString()), ("$e", DateTime.UtcNow.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public void UpdateTarget(DeploymentTarget t)
    {
        using var db = Open();
        using var cmd = Command(db, """
            UPDATE deployment_targets
            SET status=$s, stage=$stage, error=$err, steps=$steps, started_utc=$start, ended_utc=$end
            WHERE id=$id
            """,
            ("$id", t.Id), ("$s", t.Status.ToString()), ("$stage", t.Stage), ("$err", t.Error),
            ("$steps", JsonSerializer.Serialize(t.Steps, Json)),
            ("$start", t.StartedUtc?.ToString("o")), ("$end", t.EndedUtc?.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    private static List<DeploymentTarget> ListTargets(SqliteConnection db, long deploymentId)
    {
        using var cmd = Command(db, """
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
                Steps = JsonSerializer.Deserialize<List<StepOutcome>>(r.GetString(6), Json) ?? [],
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
        Snapshot = JsonSerializer.Deserialize<Package>(r.GetString(3), Json) ?? new(),
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

    // ---------- Planifications ----------

    public List<Schedule> ListSchedules()
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT id, name, data, last_run_utc, last_result, modified_utc FROM schedules ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<Schedule>();
        while (r.Read()) list.Add(ReadSchedule(r));
        return list;
    }

    public Schedule? GetSchedule(long id) => ListSchedules().FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Enregistre une planification. Les déclenchements ne sont comptés qu'à partir de maintenant :
    /// modifier une planification ne rattrape pas les déclenchements passés.
    /// </summary>
    public Schedule SaveSchedule(Schedule s)
    {
        s.ModifiedUtc = DateTime.UtcNow;
        var data = JsonSerializer.Serialize(s, Json);
        using var db = Open();
        var args = new (string, object?)[]
        {
            ("$id", s.Id), ("$name", s.Name), ("$data", data), ("$checked", DateTime.Now.ToString("o")), ("$mod", s.ModifiedUtc.ToString("o")),
        };
        if (s.Id == 0)
        {
            using var cmd = Command(db, """
                INSERT INTO schedules (name, data, last_checked, modified_utc) VALUES ($name, $data, $checked, $mod);
                SELECT last_insert_rowid();
                """, args);
            s.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Command(db, "UPDATE schedules SET name=$name, data=$data, last_checked=$checked, modified_utc=$mod WHERE id=$id", args);
            cmd.ExecuteNonQuery();
        }
        return s;
    }

    public void DeleteSchedule(long id)
    {
        using var db = Open();
        using var cmd = Command(db, """
            DELETE FROM schedules WHERE id = $id;
            DELETE FROM schedule_history WHERE schedule_id = $id;
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Dernier instant (heure locale) jusqu'auquel les déclencheurs ont été examinés.</summary>
    public DateTime? GetScheduleChecked(long id)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT last_checked FROM schedules WHERE id = $id", ("$id", id));
        return cmd.ExecuteScalar() is string s ? DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind) : null;
    }

    public void SetScheduleChecked(long id, DateTime local)
    {
        using var db = Open();
        using var cmd = Command(db, "UPDATE schedules SET last_checked = $c WHERE id = $id", ("$id", id), ("$c", local.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public void SetScheduleRun(long id, DateTime utc, string result)
    {
        using var db = Open();
        using var cmd = Command(db, "UPDATE schedules SET last_run_utc = $r, last_result = $t WHERE id = $id",
            ("$id", id), ("$r", utc.ToString("o")), ("$t", result));
        cmd.ExecuteNonQuery();
    }

    private static Schedule ReadSchedule(SqliteDataReader r)
    {
        var s = JsonSerializer.Deserialize<Schedule>(r.GetString(2), Json) ?? new();
        s.Id = r.GetInt64(0);
        s.Name = r.GetString(1);
        s.LastRunUtc = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)).ToUniversalTime();
        s.LastResult = r.IsDBNull(4) ? null : r.GetString(4);
        s.ModifiedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime();
        return s;
    }

    public List<ScheduleTargetHistory> GetScheduleHistory(long scheduleId)
    {
        using var db = Open();
        using var cmd = Command(db, """
            SELECT h.computer, h.package_id, COALESCE(p.name, '(paquet supprimé)'), h.succeeded, h.failures, h.last_utc
            FROM schedule_history h LEFT JOIN packages p ON p.id = h.package_id
            WHERE h.schedule_id = $s ORDER BY h.computer COLLATE NOCASE, p.name COLLATE NOCASE
            """, ("$s", scheduleId));
        using var r = cmd.ExecuteReader();
        var list = new List<ScheduleTargetHistory>();
        while (r.Read())
            list.Add(new ScheduleTargetHistory
            {
                Computer = r.GetString(0),
                PackageId = r.GetInt64(1),
                PackageName = r.GetString(2),
                Succeeded = r.GetInt64(3) != 0,
                Failures = r.GetInt32(4),
                LastUtc = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)).ToUniversalTime(),
            });
        return list;
    }

    /// <summary>Compte le résultat d'un poste : réussite (définitive) ou un échec de plus.</summary>
    public void RecordScheduleResult(long scheduleId, string computer, long packageId, bool success)
    {
        using var db = Open();
        using var cmd = Command(db, """
            INSERT INTO schedule_history (schedule_id, computer, package_id, succeeded, failures, last_utc)
            VALUES ($s, $c, $p, $ok, $ko, $now)
            ON CONFLICT(schedule_id, computer, package_id) DO UPDATE SET
                succeeded = MAX(succeeded, excluded.succeeded),
                failures = failures + excluded.failures,
                last_utc = excluded.last_utc
            """, ("$s", scheduleId), ("$c", computer), ("$p", packageId), ("$ok", success ? 1 : 0), ("$ko", success ? 0 : 1),
            ("$now", DateTime.UtcNow.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Efface des lignes d'historique (toutes si <paramref name="keys"/> est vide) : ces postes seront de nouveau servis.</summary>
    public void ClearScheduleHistory(long scheduleId, IReadOnlyCollection<ScheduleHistoryKey>? keys)
    {
        using var db = Open();
        if (keys is not { Count: > 0 })
        {
            using var all = Command(db, "DELETE FROM schedule_history WHERE schedule_id = $s", ("$s", scheduleId));
            all.ExecuteNonQuery();
            return;
        }
        using var tx = db.BeginTransaction();
        foreach (var k in keys)
        {
            using var cmd = Command(db, "DELETE FROM schedule_history WHERE schedule_id = $s AND computer = $c AND package_id = $p",
                ("$s", scheduleId), ("$c", k.Computer), ("$p", k.PackageId));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Postes qui ont encore un déploiement de cette planification en attente ou en cours.</summary>
    public HashSet<string> ActiveScheduleComputers(long scheduleId)
    {
        using var db = Open();
        using var cmd = Command(db, """
            SELECT DISTINCT t.computer FROM deployment_targets t JOIN deployments d ON d.id = t.deployment_id
            WHERE d.schedule_id = $s AND t.status IN ('Queued', 'Running')
            """, ("$s", scheduleId));
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    // ---------- Identifiants ----------

    private const string CredentialColumns = "id, domain, user, description, is_default";

    public List<Credential> ListCredentials()
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {CredentialColumns} FROM credentials ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<Credential>();
        while (r.Read()) list.Add(ReadCredential(r));
        return list;
    }

    public Credential? GetCredential(long id)
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {CredentialColumns} FROM credentials WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadCredential(r) : null;
    }

    /// <summary>Mot de passe chiffré (base64) tel qu'il est rangé.</summary>
    public string? GetCredentialSecret(long id)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT password FROM credentials WHERE id = $id", ("$id", id));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Enregistre un identifiant. <paramref name="secret"/> null = garder le mot de passe actuel.
    /// Le premier identifiant devient celui par défaut.
    /// </summary>
    public Credential SaveCredential(Credential c, string? secret)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        var args = new (string, object?)[]
        {
            ("$id", c.Id), ("$domain", c.Domain), ("$user", c.User), ("$desc", c.Description), ("$pwd", secret),
        };
        if (c.Id == 0)
        {
            using var cmd = Command(db, """
                INSERT INTO credentials (domain, user, description, password, is_default)
                VALUES ($domain, $user, $desc, $pwd, NOT EXISTS (SELECT 1 FROM credentials));
                SELECT last_insert_rowid();
                """, args);
            cmd.Transaction = tx;
            c.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Command(db, """
                UPDATE credentials SET domain = $domain, user = $user, description = $desc, password = COALESCE($pwd, password)
                WHERE id = $id
                """, args);
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return GetCredential(c.Id)!;
    }

    /// <summary>Supprime un identifiant ; s'il était par défaut, le premier restant le devient.</summary>
    public void DeleteCredential(long id)
    {
        using var db = Open();
        using var cmd = Command(db, """
            DELETE FROM credentials WHERE id = $id;
            UPDATE credentials SET is_default = 1
            WHERE id = (SELECT MIN(id) FROM credentials) AND NOT EXISTS (SELECT 1 FROM credentials WHERE is_default = 1);
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    public void SetDefaultCredential(long id)
    {
        using var db = Open();
        using var cmd = Command(db, "UPDATE credentials SET is_default = (id = $id) WHERE EXISTS (SELECT 1 FROM credentials WHERE id = $id)", ("$id", id));
        cmd.ExecuteNonQuery();
    }

    private static Credential ReadCredential(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Domain = r.IsDBNull(1) ? null : r.GetString(1),
        User = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        IsDefault = r.GetInt64(4) != 0,
    };

    // ---------- Outils ----------

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = Command(db, sql);
        cmd.ExecuteNonQuery();
    }
}
