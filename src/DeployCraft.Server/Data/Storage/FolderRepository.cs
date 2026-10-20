namespace DeployCraft.Server.Data;

/// <summary>Folders of the package tree.</summary>
public interface IFolderRepository
{
    List<PackageFolder> List();
    PackageFolder Save(PackageFolder f);
    void Delete(long id);
}

/// <summary>SQLite storage of <see cref="IFolderRepository"/>.</summary>
public sealed class FolderRepository(Database database) : IFolderRepository
{
    public List<PackageFolder> List()
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "SELECT id, name, parent_id FROM package_folders ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        var list = new List<PackageFolder>();
        while (r.Read()) list.Add(new() { Id = r.GetInt64(0), Name = r.GetString(1), ParentId = r.IsDBNull(2) ? null : r.GetInt64(2) });
        return list;
    }

    public PackageFolder Save(PackageFolder f)
    {
        using var connection = database.Open();
        var args = new (string, object?)[] { ("$id", f.Id), ("$name", f.Name.Trim()), ("$parent", f.ParentId) };
        if (f.Id == 0)
        {
            using var cmd = Database.Command(connection, "INSERT INTO package_folders (name, parent_id) VALUES ($name, $parent); SELECT last_insert_rowid();", args);
            f.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = Database.Command(connection, "UPDATE package_folders SET name = $name, parent_id = $parent WHERE id = $id", args);
            cmd.ExecuteNonQuery();
        }
        f.Name = f.Name.Trim();
        return f;
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        using var cmd = Database.Command(connection, "DELETE FROM package_folders WHERE id = $id", ("$id", id));
        cmd.ExecuteNonQuery();
    }
}
