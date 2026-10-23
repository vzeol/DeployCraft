namespace DeployCraft.Server.Data;

/// <summary>
/// New DeployCraft version published in the repository's Releases (excluding pre-releases), with its installer and
/// the SHA-256 hash GitHub gives for it (null if the release has none: manual installation only).
/// </summary>
public record UpdateInfo(string Version, string Url, string? SetupUrl = null, string? SetupSha256 = null)
{
    /// <summary>Latest stable version published (pre-releases are not included).</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/vzeol/DeployCraft/releases/latest";

    /// <summary>
    /// True if the release tag ("v0.16.0") designates a newer version than <paramref name="current"/>
    /// ("0.15.0"). With equal numbers, the final version comes before a pre-release ("1.0.0-rc.1" → "v1.0.0").
    /// An unreadable tag is never an update.
    /// </summary>
    public static bool IsNewer(string? tag, string? current)
    {
        if (Parse(tag) is not { } latest || Parse(current) is not { } installed) return false;
        return latest > installed || latest == installed && IsPrerelease(current) && !IsPrerelease(tag);
    }

    /// <summary>"1.0.0-rc.1" (suffix after a dash); what follows a "+" (build number) does not count.</summary>
    private static bool IsPrerelease(string? text) => (text ?? "").Split('+')[0].Contains('-');

    /// <summary>File attached to the release: name, download address, hash ("sha256:...").</summary>
    public record Asset(string Name, string Url, string? Digest);

    /// <summary>Installer of the version ("DeployCraft-Setup-0.16.0.exe") with its SHA-256 hash; null if either is missing.</summary>
    public static (string Url, string Sha256)? FindSetup(IEnumerable<Asset> assets, string version)
    {
        var setup = assets.FirstOrDefault(a => a.Name.Equals($"DeployCraft-Setup-{version}.exe", StringComparison.OrdinalIgnoreCase));
        const string prefix = "sha256:";
        if (setup?.Digest is not { } digest || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var sha = digest[prefix.Length..];
        return sha.Length == 64 && sha.All(Uri.IsHexDigit) ? (setup.Url, sha.ToUpperInvariant()) : null;
    }

    /// <summary>"v0.15.0", "0.15.0-beta.1" → 0.15.0; null if unreadable.</summary>
    public static Version? Parse(string? text)
    {
        var core = (text ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return System.Version.TryParse(core, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }
}
