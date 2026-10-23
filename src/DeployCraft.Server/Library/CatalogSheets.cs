using System.Net;

namespace DeployCraft.Server.Library;

/// <summary>Reading of the public winget catalog over HTTP: client and install sheet of one software version.</summary>
internal static class CatalogSheets
{
    private const string CatalogRaw = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/";

    public static readonly HttpClient Http = CreateClient();

    /// <summary>Install sheet of the latest version: chosen installer, or the reason why there is none.</summary>
    public static async Task<LibraryEntry> ReadEntryAsync(IndexEntry e)
    {
        var url = $"{CatalogRaw}{Escape(LibraryCatalog.ManifestFolder(e.Id))}/{Escape(e.Version)}/{Escape(e.Id)}.installer.yaml";
        using var response = await Http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new LibraryEntry { Version = e.Version, Problem = Msg.Get("PackageLibrary_SheetNotFound"), SeenUtc = DateTime.UtcNow };
        response.EnsureSuccessStatusCode();
        var installer = CatalogManifest.Choose(await response.Content.ReadAsStringAsync(), out var problem);
        return new LibraryEntry { Version = e.Version, Installer = installer, Problem = problem, SeenUtc = DateTime.UtcNow };
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // The catalog rejects requests without an application name.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft");
        return client;
    }
}
