using System.Text.Json;

namespace DeployCraft.Server.Data;

/// <summary>Server settings stored as key / value pairs, including the deployment preferences.</summary>
public interface ISettingsRepository
{
    string? Get(string key);
    void Set(string key, string? value);
    DeploymentDefaults GetDefaults();
    void SaveDefaults(DeploymentDefaults defaults);
}

/// <summary>SQLite storage of <see cref="ISettingsRepository"/>.</summary>
public sealed class SettingsRepository(Database database) : ISettingsRepository
{
    public string? Get(string key)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT value FROM settings WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string? value)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            INSERT INTO settings (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """, ("$k", key), ("$v", value));
        cmd.ExecuteNonQuery();
    }

    public DeploymentDefaults GetDefaults() =>
        Get("defaults") is { } json ? JsonSerializer.Deserialize<DeploymentDefaults>(json, Database.Json) ?? new() : new();

    public void SaveDefaults(DeploymentDefaults defaults) => Set("defaults", JsonSerializer.Serialize(defaults, Database.Json));
}
