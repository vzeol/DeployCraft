using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Recherche d'une nouvelle version publiée sur GitHub, et téléchargement de son installeur (empreinte vérifiée).
/// L'installeur met à jour le service et la console ensemble, en administrateur, sur le serveur.
/// </summary>
public static class UpdateCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly HttpClient Download = new() { Timeout = TimeSpan.FromMinutes(15) };

    static UpdateCheck()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft-Console");
        Download.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft-Console");
    }

    /// <summary>Version installée, pré-version comprise (« 1.0.0-rc.1 »), sans le n° de compilation.</summary>
    public static string? CurrentVersion =>
        typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(UpdateCheck).Assembly.GetName().Version?.ToString(3);

    /// <summary>La nouvelle version, ou null (déjà à jour, pas d'accès à Internet…).</summary>
    public static async Task<UpdateInfo?> FindAsync()
    {
        try
        {
            var release = await Http.GetFromJsonAsync<Release>(UpdateInfo.LatestReleaseApi);
            if (release == null || !UpdateInfo.IsNewer(release.Tag, CurrentVersion)) return null;
            var version = UpdateInfo.Parse(release.Tag)!.ToString(3);
            var setup = UpdateInfo.FindSetup(release.Assets.Select(a => new UpdateInfo.Asset(a.Name ?? "", a.Url ?? "", a.Digest)), version);
            return new UpdateInfo(version, release.Url ?? "", setup?.Url, setup?.Sha256);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Télécharge l'installeur dans le dossier temporaire et vérifie son empreinte ; renvoie son chemin.
    /// Un fichier qui ne correspond pas est supprimé (<see cref="InvalidDataException"/>).
    /// </summary>
    public static async Task<string> DownloadSetupAsync(UpdateInfo update, IProgress<int> progress)
    {
        var file = Path.Combine(Path.GetTempPath(), $"DeployCraft-Setup-{update.Version}.exe");
        using (var response = await Download.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report((int)(done * 100 / total.Value));
            }
        }

        string hash;
        await using (var check = File.OpenRead(file))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(check));
        if (!hash.Equals(update.SetupSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidDataException("L'installeur téléchargé ne correspond pas à l'empreinte publiée : il a été supprimé. Réessayez plus tard.");
        }
        return file;
    }

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; set; }
        [JsonPropertyName("html_url")] public string? Url { get; set; }
        [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
