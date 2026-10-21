using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>Schedules and their per-computer history.</summary>
public interface IScheduleRepository
{
    List<Schedule> List();
    Schedule? Get(long id);
    Schedule Save(Schedule s);
    void Delete(long id);
    DateTime? GetChecked(long id);
    void SetChecked(long id, DateTime local);
    void SetRun(long id, DateTime utc, string result);
    List<ScheduleTargetHistory> GetHistory(long scheduleId);
    void RecordResult(long scheduleId, string computer, long packageId, bool success);
    void ClearHistory(long scheduleId, IReadOnlyCollection<ScheduleHistoryKey>? keys);
    HashSet<string> ActiveComputers(long scheduleId);
}

/// <summary>SQLite storage of <see cref="IScheduleRepository"/>.</summary>
public sealed class ScheduleRepository(Database database) : IScheduleRepository
{
    public List<Schedule> List()
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT id, name, data, last_run_utc, last_result, modified_utc FROM schedules ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<Schedule>();
        while (r.Read()) list.Add(ReadSchedule(r));
        return list;
    }

    public Schedule? Get(long id) => List().FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Saves a schedule. Triggers are only counted from now on:
    /// editing a schedule does not catch up on past triggers.
    /// </summary>
    public Schedule Save(Schedule s)
    {
        s.ModifiedUtc = DateTime.UtcNow;
        var data = JsonSerializer.Serialize(s, Database.Json);
        using var connection = database.Open();
        var args = new (string, object?)[]
        {
            ("$id", s.Id), ("$name", s.Name), ("$data", data), ("$checked", DateTime.Now.ToString("o")), ("$mod", s.ModifiedUtc.ToString("o")),
        };
        if (s.Id == 0)
        {
            using var cmd = Database.Command(connection, """
                INSERT INTO schedules (name, data, last_checked, modified_utc) VALUES ($name, $data, $checked, $mod);
                SELECT last_insert_rowid();
                """, args);
            s.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Database.Command(connection, "UPDATE schedules SET name=$name, data=$data, last_checked=$checked, modified_utc=$mod WHERE id=$id", args);
            cmd.ExecuteNonQuery();
        }
        return s;
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            DELETE FROM schedules WHERE id = $id;
            DELETE FROM schedule_history WHERE schedule_id = $id;
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Last moment (local time) up to which the triggers were examined.</summary>
    public DateTime? GetChecked(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT last_checked FROM schedules WHERE id = $id", ("$id", id));
        return cmd.ExecuteScalar() is string s ? DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind) : null;
    }

    public void SetChecked(long id, DateTime local)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "UPDATE schedules SET last_checked = $c WHERE id = $id", ("$id", id), ("$c", local.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public void SetRun(long id, DateTime utc, string result)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "UPDATE schedules SET last_run_utc = $r, last_result = $t WHERE id = $id",
            ("$id", id), ("$r", utc.ToString("o")), ("$t", result));
        cmd.ExecuteNonQuery();
    }

    private static Schedule ReadSchedule(SqliteDataReader r)
    {
        var s = JsonSerializer.Deserialize<Schedule>(r.GetString(2), Database.Json) ?? new();
        s.Id = r.GetInt64(0);
        s.Name = r.GetString(1);
        s.LastRunUtc = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)).ToUniversalTime();
        s.LastResult = r.IsDBNull(4) ? null : r.GetString(4);
        s.ModifiedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime();
        return s;
    }

    public List<ScheduleTargetHistory> GetHistory(long scheduleId)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
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

    /// <summary>Counts the result of a computer: success (final) or one more failure.</summary>
    public void RecordResult(long scheduleId, string computer, long packageId, bool success)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
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

    /// <summary>Deletes history rows (all of them if <paramref name="keys"/> is empty): these computers will be served again.</summary>
    public void ClearHistory(long scheduleId, IReadOnlyCollection<ScheduleHistoryKey>? keys)
    {
        using var connection = database.Open();
        if (keys is not { Count: > 0 })
        {
            using var all = Database.Command(connection, "DELETE FROM schedule_history WHERE schedule_id = $s", ("$s", scheduleId));
            all.ExecuteNonQuery();
            return;
        }
        using var tx = connection.BeginTransaction();
        foreach (var k in keys)
        {
            using var cmd = Database.Command(connection, "DELETE FROM schedule_history WHERE schedule_id = $s AND computer = $c AND package_id = $p",
                ("$s", scheduleId), ("$c", k.Computer), ("$p", k.PackageId));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Computers that still have a deployment of this schedule pending or in progress.</summary>
    public HashSet<string> ActiveComputers(long scheduleId)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            SELECT DISTINCT t.computer FROM deployment_targets t JOIN deployments d ON d.id = t.deployment_id
            WHERE d.schedule_id = $s AND t.status IN ('Queued', 'Running')
            """, ("$s", scheduleId));
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }
}
