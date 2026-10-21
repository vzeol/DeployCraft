using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Looks for a new version published on GitHub, and downloads its installer (hash checked).
/// The installer updates the service and the console together, as administrator, on the server.
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

    /// <summary>Installed version, pre-release included ("1.0.0-rc.1"), without the build number.</summary>
    public static string? CurrentVersion =>
        typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(UpdateCheck).Assembly.GetName().Version?.ToString(3);

    /// <summary>The new version, or null (already up to date, no Internet access...).</summary>
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
    /// Downloads the installer into the temporary folder and checks its hash; returns its path.
    /// A file that does not match is deleted (<see cref="InvalidDataException"/>).
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
            throw new InvalidDataException(L.Get("UpdateCheck_BadHash"));
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
