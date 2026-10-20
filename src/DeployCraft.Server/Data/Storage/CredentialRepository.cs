using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>Deployment credentials (the password is stored encrypted, see DeployCredentials).</summary>
public interface ICredentialRepository
{
    List<Credential> List();
    Credential? Get(long id);
    string? GetSecret(long id);
    Credential Save(Credential c, string? secret);
    void Delete(long id);
    void SetDefault(long id);
}

/// <summary>SQLite storage of <see cref="ICredentialRepository"/>.</summary>
public sealed class CredentialRepository(Database database) : ICredentialRepository
{
    private const string CredentialColumns = "id, domain, user, description, is_default";

    public List<Credential> List()
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, $"SELECT {CredentialColumns} FROM credentials ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<Credential>();
        while (r.Read()) list.Add(ReadCredential(r));
        return list;
    }

    public Credential? Get(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, $"SELECT {CredentialColumns} FROM credentials WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadCredential(r) : null;
    }

    /// <summary>Mot de passe chiffré (base64) tel qu'il est rangé.</summary>
    public string? GetSecret(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT password FROM credentials WHERE id = $id", ("$id", id));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Enregistre un identifiant. <paramref name="secret"/> null = garder le mot de passe actuel.
    /// Le premier identifiant devient celui par défaut.
    /// </summary>
    public Credential Save(Credential c, string? secret)
    {
        using var connection = database.Open();
        using var tx = connection.BeginTransaction();
        var args = new (string, object?)[]
        {
            ("$id", c.Id), ("$domain", c.Domain), ("$user", c.User), ("$desc", c.Description), ("$pwd", secret),
        };
        if (c.Id == 0)
        {
            using var cmd = Database.Command(connection, """
                INSERT INTO credentials (domain, user, description, password, is_default)
                VALUES ($domain, $user, $desc, $pwd, NOT EXISTS (SELECT 1 FROM credentials));
                SELECT last_insert_rowid();
                """, args);
            cmd.Transaction = tx;
            c.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Database.Command(connection, """
                UPDATE credentials SET domain = $domain, user = $user, description = $desc, password = COALESCE($pwd, password)
                WHERE id = $id
                """, args);
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return Get(c.Id)!;
    }

    /// <summary>Supprime un identifiant ; s'il était par défaut, le premier restant le devient.</summary>
    public void Delete(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            DELETE FROM credentials WHERE id = $id;
            UPDATE credentials SET is_default = 1
            WHERE id = (SELECT MIN(id) FROM credentials) AND NOT EXISTS (SELECT 1 FROM credentials WHERE is_default = 1);
            """, ("$id", id));
        cmd.ExecuteNonQuery();
    }

    public void SetDefault(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "UPDATE credentials SET is_default = (id = $id) WHERE EXISTS (SELECT 1 FROM credentials WHERE id = $id)", ("$id", id));
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
}
