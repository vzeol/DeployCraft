namespace DeployCraft.Server.Data;

/// <summary>Nouvelle version de DeployCraft publiée dans les Releases du dépôt (hors pré-versions).</summary>
public record UpdateInfo(string Version, string Url)
{
    /// <summary>Dernière version stable publiée (les pré-versions n'y figurent pas).</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/vzeol/DeployCraft/releases/latest";

    /// <summary>
    /// Vrai si l'étiquette de la release (« v0.16.0 ») désigne une version plus récente que <paramref name="current"/>
    /// (« 0.15.0 »). Une étiquette illisible n'est jamais une mise à jour.
    /// </summary>
    public static bool IsNewer(string? tag, string? current) =>
        Parse(tag) is { } latest && Parse(current) is { } installed && latest > installed;

    /// <summary>« v0.15.0 », « 0.15.0-beta.1 » → 0.15.0 ; null si illisible.</summary>
    public static Version? Parse(string? text)
    {
        var core = (text ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return System.Version.TryParse(core, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }
}
