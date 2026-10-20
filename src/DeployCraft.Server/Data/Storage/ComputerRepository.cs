using System.Text.Json;

namespace DeployCraft.Server.Data;

/// <summary>Computers seen during deployments (network cards, for wake-on-LAN).</summary>
public interface IComputerRepository
{
    void SaveNetwork(string computer, IReadOnlyCollection<NetworkCard> cards);
    List<NetworkCard> GetNetwork(string computer);
}

/// <summary>SQLite storage of <see cref="IComputerRepository"/>.</summary>
public sealed class ComputerRepository(Database database) : IComputerRepository
{
    /// <summary>Retient les cartes réseau d'un poste (adresse MAC et diffusion de son sous-réseau).</summary>
    public void SaveNetwork(string computer, IReadOnlyCollection<NetworkCard> cards)
    {
        if (cards.Count == 0) return;
        using var connection = database.Open();
        using var cmd = Database.Command(connection, """
            INSERT INTO computers (name, network, seen_utc) VALUES ($n, $j, $s)
            ON CONFLICT(name) DO UPDATE SET network = excluded.network, seen_utc = excluded.seen_utc
            """, ("$n", computer), ("$j", JsonSerializer.Serialize(cards, Database.Json)), ("$s", DateTime.UtcNow.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public List<NetworkCard> GetNetwork(string computer)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT network FROM computers WHERE name = $n", ("$n", computer));
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<List<NetworkCard>>(json, Database.Json) ?? [] : [];
    }
}
