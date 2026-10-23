namespace DeployCraft.Server.Data;

/// <summary>A software offered by the package library, and what DeployCraft made of it.</summary>
public class LibraryItem
{
    /// <summary>Identifier of the software in the public winget catalog (e.g. VideoLAN.VLC).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Latest version known from the catalog (null until it has been read).</summary>
    public string? LatestVersion { get; set; }
    /// <summary>Chosen installer type (msi, nullsoft, inno...) and architecture.</summary>
    public string? Installer { get; set; }
    /// <summary>Reason why this software cannot be downloaded (no silent installer for the whole computer...).</summary>
    public string? Problem { get; set; }
    /// <summary>DeployCraft package created from this software (null if never downloaded or if deleted).</summary>
    public long? PackageId { get; set; }
    /// <summary>Version downloaded into this package.</summary>
    public string? PackageVersion { get; set; }
    /// <summary>Current step (download, verification, copy, package creation), null if there is none.</summary>
    public string? Stage { get; set; }
    /// <summary>Progress of the current step, from 0 to 100.</summary>
    public int? Progress { get; set; }
    /// <summary>Error of the last download of this software.</summary>
    public string? Error { get; set; }
    public bool UpdateAvailable { get; set; }
}

/// <summary>Update of the packages created from the library when the catalog publishes a new version.</summary>
public enum LibraryAutoUpdate
{
    /// <summary>Only reported: the user clicks "Mettre à jour".</summary>
    Manual,
    /// <summary>Done by the service as soon as the new version is seen.</summary>
    Immediate,
    /// <summary>Done by the service a few days after the version appeared in the catalog.</summary>
    Delayed,
}

/// <summary>Library state returned to the console.</summary>
public class LibraryInfo
{
    /// <summary>Folder where the installers are stored.</summary>
    public string Folder { get; set; } = "";
    /// <summary>The folder is the default one (on the server, readable in push mode only).</summary>
    public bool DefaultFolder { get; set; }
    public DateTime? LastRefreshUtc { get; set; }
    public bool Refreshing { get; set; }
    /// <summary>Error of the last catalog read.</summary>
    public string? RefreshError { get; set; }
    public LibraryAutoUpdate AutoUpdate { get; set; }
    /// <summary>Delay before an automatic update (<see cref="LibraryAutoUpdate.Delayed"/> mode).</summary>
    public int AutoUpdateDays { get; set; }
    /// <summary>Number of software items in the catalog.</summary>
    public int CatalogCount { get; set; }
    public List<LibraryItem> Items { get; set; } = [];
}
