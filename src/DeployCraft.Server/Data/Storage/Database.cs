using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;


namespace DeployCraft.Server.Data;

/// <summary>
/// SQLite database file shared by the repositories. Nested structures (steps, results) are stored as JSON:
/// the schema stays small and easy to evolve.
/// </summary>
public sealed class Database
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Migrate();
    }

    internal SqliteConnection Open()
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
        // Dossiers de l'arbre des paquets.
        Exec(db, """
            CREATE TABLE IF NOT EXISTS package_folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parent_id INTEGER
            );
            """);
        AddColumnIfMissing(db, "packages", "folder_id", "INTEGER");
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

    internal static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    internal static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = Command(db, sql);
        cmd.ExecuteNonQuery();
    }
}
