using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Recherche d'une nouvelle version publiée sur GitHub. La console prévient seulement : l'installeur se lance à la
/// main, en administrateur, sur le serveur (il met à jour le service et la console).
/// </summary>
public static class UpdateCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateCheck() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft-Console");

    public static string? CurrentVersion => typeof(UpdateCheck).Assembly.GetName().Version?.ToString(3);

    /// <summary>La nouvelle version, ou null (déjà à jour, pas d'accès à Internet…).</summary>
    public static async Task<UpdateInfo?> FindAsync()
    {
        try
        {
            var release = await Http.GetFromJsonAsync<Release>(UpdateInfo.LatestReleaseApi);
            return release != null && UpdateInfo.IsNewer(release.Tag, CurrentVersion)
                ? new UpdateInfo(UpdateInfo.Parse(release.Tag)!.ToString(3), release.Url ?? "")
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; set; }
        [JsonPropertyName("html_url")] public string? Url { get; set; }
    }
}
