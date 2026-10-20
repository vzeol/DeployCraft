using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>Packages, their folder and the targets remembered for them.</summary>
public interface IPackageRepository
{
    List<Package> List();
    Package? Get(long id);
    Package Save(Package p);
    void Delete(long id);
    void Move(long id, long? folderId);
    List<string> GetTargets(long packageId);
    void SetTargets(long packageId, IReadOnlyCollection<string> computers);
}

/// <summary>SQLite storage of <see cref="IPackageRepository"/>.</summary>
public sealed class PackageRepository(Database database, ISettingsRepository settings) : IPackageRepository
{
    private const string PackageColumns = "id, name, description, steps, modified_utc, run_as, copy_mode, version, timeout, conditions, offline, folder_id";

    public List<Package> List()
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, $"SELECT {PackageColumns} FROM packages ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        var list = new List<Package>();
        while (r.Read()) list.Add(ReadPackage(r));
        return list;
    }

    public Package? Get(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, $"SELECT {PackageColumns} FROM packages WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadPackage(r) : null;
    }

    public Package Save(Package p)
    {
        p.ModifiedUtc = DateTime.UtcNow;
        using var connection = database.Open();
        var args = new (string, object?)[]
        {
            ("$id", p.Id), ("$name", p.Name), ("$desc", p.Description),
            ("$steps", JsonSerializer.Serialize(p.Steps, Database.Json)), ("$mod", p.ModifiedUtc.ToString("o")),
            ("$runas", p.RunAs?.ToString() ?? DefaultValue), ("$copy", p.CopyMode?.ToString() ?? DefaultValue),
            ("$version", string.IsNullOrWhiteSpace(p.Version) ? null : p.Version.Trim()), ("$timeout", p.TimeoutMinutes),
            ("$conditions", JsonSerializer.Serialize(p.Conditions, Database.Json)),
            ("$offline", p.Offline == null ? null : JsonSerializer.Serialize(p.Offline, Database.Json)),
            ("$folder", p.FolderId),
        };
        if (p.Id == 0)
        {
            using var cmd = Database.Command(connection, """
                INSERT INTO packages (name, description, steps, modified_utc, run_as, copy_mode, version, timeout, conditions, offline, folder_id)
                VALUES ($name, $desc, $steps, $mod, $runas, $copy, $version, $timeout, $conditions, $offline, $folder);
                SELECT last_insert_rowid();
                """, args);
            p.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            // Le dossier n'est pas modifié ici (seulement par Move) : enregistrer un paquet ouvert avant un
            // déplacement ne le remet pas à son ancienne place.
            using var cmd = Database.Command(connection, """
                UPDATE packages SET name=$name, description=$desc, steps=$steps, modified_utc=$mod, run_as=$runas, copy_mode=$copy,
                    version=$version, timeout=$timeout, conditions=$conditions, offline=$offline
                WHERE id=$id
                """, args);
            cmd.ExecuteNonQuery();
        }
        return p;
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "DELETE FROM packages WHERE id = $id", ("$id", id));
        cmd.ExecuteNonQuery();
        settings.Set($"package.{id}.targets", null);
    }

    private static Package ReadPackage(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        Steps = JsonSerializer.Deserialize<List<PackageStep>>(r.GetString(3), Database.Json) ?? [],
        ModifiedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
        RunAs = Enum.TryParse<RunAsMode>(r.GetString(5), out var runAs) ? runAs : null,
        CopyMode = Enum.TryParse<CopyMode>(r.GetString(6), out var copy) ? copy : null,
        Version = r.IsDBNull(7) ? null : r.GetString(7),
        TimeoutMinutes = r.IsDBNull(8) ? null : r.GetInt32(8),
        Conditions = r.IsDBNull(9) ? new() : JsonSerializer.Deserialize<Conditions>(r.GetString(9), Database.Json) ?? new(),
        Offline = r.IsDBNull(10) ? null : JsonSerializer.Deserialize<OfflineSettings>(r.GetString(10), Database.Json),
        FolderId = r.IsDBNull(11) ? null : r.GetInt64(11),
    };

    public void Move(long id, long? folderId)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "UPDATE packages SET folder_id = $f WHERE id = $id", ("$f", folderId), ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Valeur rangée dans run_as / copy_mode quand le paquet suit les Préférences.</summary>
    private const string DefaultValue = "Default";

    /// <summary>Cibles mémorisées pour un paquet (« Mémoriser ces cibles » de la fenêtre Déployer une fois).</summary>
    public List<string> GetTargets(long packageId) =>
        settings.Get($"package.{packageId}.targets") is { } json
            ? JsonSerializer.Deserialize<List<string>>(json, Database.Json) ?? []
            : [];

    public void SetTargets(long packageId, IReadOnlyCollection<string> computers) =>
        settings.Set($"package.{packageId}.targets", computers.Count == 0 ? null : JsonSerializer.Serialize(computers, Database.Json));
}
