using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeployCraft.Console;

/// <summary>
/// Réglages propres à la session Windows de la personne (thème, largeur de l'arbre…), dans son profil : chaque réglage
/// est lu et écrit seul, sans effacer les autres. Fichier absent ou illisible : valeurs d'usine.
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
        catch (IOException) { } // réglage non enregistré : il reste appliqué pour cette session
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
