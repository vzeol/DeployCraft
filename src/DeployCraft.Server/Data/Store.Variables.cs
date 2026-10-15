using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Data;

/// <summary>Variables personnalisées (Préférences › Variables) : table à part, modifiée d'un bloc.</summary>
public partial class Store
{
    private static void MigrateVariables(SqliteConnection db) => Exec(db, """
        CREATE TABLE IF NOT EXISTS variables (
            name TEXT PRIMARY KEY COLLATE NOCASE,
            value TEXT NOT NULL,
            description TEXT
        );
        """);

    public List<Variable> ListVariables()
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT name, value, description FROM variables ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        var list = new List<Variable>();
        while (r.Read()) list.Add(new Variable { Name = r.GetString(0), Value = r.GetString(1), Description = r.IsDBNull(2) ? null : r.GetString(2) });
        return list;
    }

    /// <summary>Remplace toutes les variables par <paramref name="variables"/> (la page les enregistre d'un bloc).</summary>
    public void SaveVariables(IEnumerable<Variable> variables)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using (var clear = Command(db, "DELETE FROM variables"))
        {
            clear.Transaction = tx;
            clear.ExecuteNonQuery();
        }
        foreach (var v in variables)
        {
            using var cmd = Command(db, "INSERT INTO variables (name, value, description) VALUES ($n, $v, $d)",
                ("$n", v.Name.Trim()), ("$v", v.Value ?? ""), ("$d", string.IsNullOrWhiteSpace(v.Description) ? null : v.Description.Trim()));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
