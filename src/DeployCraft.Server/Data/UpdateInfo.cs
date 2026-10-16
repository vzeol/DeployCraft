namespace DeployCraft.Server.Data;

/// <summary>
/// Nouvelle version de DeployCraft publiée dans les Releases du dépôt (hors pré-versions), avec son installeur et
/// l'empreinte SHA-256 que GitHub en donne (null si la release n'en a pas : installation manuelle seulement).
/// </summary>
public record UpdateInfo(string Version, string Url, string? SetupUrl = null, string? SetupSha256 = null)
{
    /// <summary>Dernière version stable publiée (les pré-versions n'y figurent pas).</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/vzeol/DeployCraft/releases/latest";

    /// <summary>
    /// Vrai si l'étiquette de la release (« v0.16.0 ») désigne une version plus récente que <paramref name="current"/>
    /// (« 0.15.0 »). Une étiquette illisible n'est jamais une mise à jour.
    /// </summary>
    public static bool IsNewer(string? tag, string? current) =>
        Parse(tag) is { } latest && Parse(current) is { } installed && latest > installed;

    /// <summary>Fichier joint à la release : nom, adresse de téléchargement, empreinte (« sha256:… »).</summary>
    public record Asset(string Name, string Url, string? Digest);

    /// <summary>Installeur de la version (« DeployCraft-Setup-0.16.0.exe ») avec son empreinte SHA-256 ; null s'il manque l'un ou l'autre.</summary>
    public static (string Url, string Sha256)? FindSetup(IEnumerable<Asset> assets, string version)
    {
        var setup = assets.FirstOrDefault(a => a.Name.Equals($"DeployCraft-Setup-{version}.exe", StringComparison.OrdinalIgnoreCase));
        const string prefix = "sha256:";
        if (setup?.Digest is not { } digest || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var sha = digest[prefix.Length..];
        return sha.Length == 64 && sha.All(Uri.IsHexDigit) ? (setup.Url, sha.ToUpperInvariant()) : null;
    }

    /// <summary>« v0.15.0 », « 0.15.0-beta.1 » → 0.15.0 ; null si illisible.</summary>
    public static Version? Parse(string? text)
    {
        var core = (text ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return System.Version.TryParse(core, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }
}
