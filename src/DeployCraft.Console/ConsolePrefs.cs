using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeployCraft.Console;

/// <summary>
/// Settings specific to the person's Windows session (theme, tree width...), in their profile: each setting
/// is read and written on its own, without erasing the others. File missing or unreadable: factory values.
/// </summary>
public static class ConsolePrefs
{
    private static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeployCraft", "console.json");

    public static T? Get<T>(string key)
    {
        try
        {
            return Read()[key] is { } node ? node.GetValue<T>() : default;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return default; }
    }

    public static void Set<T>(string key, T value)
    {
        var all = Read();
        all[key] = JsonValue.Create(value);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, all.ToJsonString());
        }
        catch (IOException) { } // setting not saved: it stays applied for this session
        catch (UnauthorizedAccessException) { }
    }

    private static JsonObject Read()
    {
        try
        {
            return JsonNode.Parse(System.IO.File.ReadAllText(File)) as JsonObject ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
