using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Library;

/// <summary>Software entry of the catalog index: identifier, name and latest version.</summary>
public record IndexEntry(string Id, string Name, string Version);

/// <summary>
/// Index of the public winget catalog: a small file (SQLite database in an archive) that lists all the software
/// with its name, publisher, keywords and latest version. Downloaded in one go, queried locally.
/// </summary>
public class CatalogIndex(string path)
{
    public const string Url = "https://cdn.winget.microsoft.com/cache/source2.msix";
    private const string Entry = "Public/index.db";

    public bool Exists => File.Exists(path);

    /// <summary>Downloads the index and replaces the old one once the new one is complete.</summary>
    public async Task DownloadAsync(HttpClient http)
    {
        var archive = path + ".msix";
        var next = path + ".new";
        try
        {
            await using (var input = await http.GetStreamAsync(Url))
            await using (var output = File.Create(archive))
                await input.CopyToAsync(output);
            using (var zip = ZipFile.OpenRead(archive))
            {
                var entry = zip.GetEntry(Entry) ?? throw new InvalidDataException(Msg.Get("CatalogIndex_Incomplete"));
                entry.ExtractToFile(next, overwrite: true);
            }
            File.Move(next, path, overwrite: true);
        }
        finally
        {
            File.Delete(archive);
            File.Delete(next);
        }
    }

    public int Count()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM packages";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>The whole catalog, in alphabetical order.</summary>
    public List<IndexEntry> All()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, name, latest_version FROM packages ORDER BY name COLLATE NOCASE";
        return Read(cmd);
    }

    /// <summary>
    /// Software whose name, identifier, publisher or a keyword contains each of the searched words;
    /// first the exact name, then names that start with the search, then those that contain all the words.
    /// </summary>
    public List<IndexEntry> Search(string text, int limit)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        using var db = Open();
        using var cmd = db.CreateCommand();
        var conditions = new List<string>();
        var inName = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            // Subqueries independent of the software: computed once, not for each of the thousands of software entries.
            conditions.Add($"""
                (p.name LIKE $w{i} OR p.id LIKE $w{i} OR p.moniker LIKE $w{i}
                 OR p.rowid IN (SELECT package FROM norm_publishers2 WHERE norm_publisher LIKE $w{i})
                 OR p.rowid IN (SELECT m.package FROM tags2_map m JOIN tags2 t ON t.rowid = m.tag WHERE t.tag LIKE $w{i}))
                """);
            inName.Add($"p.name LIKE $w{i}");
            cmd.Parameters.AddWithValue($"$w{i}", $"%{Escape(words[i])}%");
        }
        cmd.CommandText = $"""
            SELECT p.id, p.name, p.latest_version FROM packages p
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY CASE WHEN lower(p.name) = lower($all) OR lower(p.moniker) = lower($all) THEN 0
                          WHEN p.name LIKE $start THEN 1
                          WHEN {string.Join(" AND ", inName)} THEN 2 ELSE 3 END, length(p.name), p.name
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$all", text.Trim());
        cmd.Parameters.AddWithValue("$start", $"{Escape(text.Trim())}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
    }

    /// <summary>The requested software that is in the index, by identifier.</summary>
    public Dictionary<string, IndexEntry> Get(IEnumerable<string> ids)
    {
        var list = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return [];
        using var db = Open();
        using var cmd = db.CreateCommand();
        var names = list.Select((id, i) =>
        {
            cmd.Parameters.AddWithValue($"$i{i}", id);
            return $"$i{i}";
        });
        cmd.CommandText = $"SELECT id, name, latest_version FROM packages WHERE id COLLATE NOCASE IN ({string.Join(", ", names)})";
        return Read(cmd).ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static List<IndexEntry> Read(SqliteCommand cmd)
    {
        var result = new List<IndexEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(new IndexEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    /// <summary>Removes "%" and "_", which have a special meaning for LIKE.</summary>
    private static string Escape(string word) => word.Replace("%", "").Replace("_", "");

    // No connection pool: the file must be replaceable by the next download.
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        return db;
    }
}
