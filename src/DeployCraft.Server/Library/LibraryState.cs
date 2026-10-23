using DeployCraft.Server.Data;

namespace DeployCraft.Server.Library;

/// <summary>State of the library saved in the settings (JSON): folder, catalog reading, sheets read, created packages, errors.</summary>
internal sealed class LibraryState
{
    public string? Folder { get; set; }
    public DateTime? LastRefreshUtc { get; set; }
    public string? RefreshError { get; set; }
    public LibraryAutoUpdate AutoUpdate { get; set; }
    public int AutoUpdateDays { get; set; } = 3;
    public Dictionary<string, LibraryEntry> Entries { get; set; } = [];
    public Dictionary<string, LibraryLink> Links { get; set; } = [];
    public Dictionary<string, string> Errors { get; set; } = [];
}

internal sealed class LibraryEntry
{
    public string? Version { get; set; }
    public LibraryInstaller? Installer { get; set; }
    public string? Problem { get; set; }
    /// <summary>First time this version was read (starting point of the delay for automatic updates).</summary>
    public DateTime? SeenUtc { get; set; }
}

internal sealed class LibraryLink
{
    public long PackageId { get; set; }
    public string Version { get; set; } = "";
    public string File { get; set; } = "";
}
