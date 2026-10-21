using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Row of the package library.</summary>
public record LibraryRow(LibraryItem Item)
{
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Latest => Item.LatestVersion ?? "—";
    public string Downloaded => Item.PackageVersion ?? "—";
    public string Installer => Item.Installer ?? "";
    /// <summary>The software can be downloaded (or updated) now.</summary>
    public bool CanDownload => Item.Stage == null && Item.Problem == null && Item.LatestVersion != null && (Item.PackageId == null || Item.UpdateAvailable);

    public string State
    {
        get
        {
            if (Item.Stage is { } stage) return L.Format("LibraryRow_Progress", stage, Item.Progress);
            if (Item.Problem != null) return L.Format("LibraryRow_Unavailable", Item.Problem);
            if (Item.Error != null) return L.Format("LibraryRow_Failed", Item.Error);
            if (Item.LatestVersion == null) return L.Get("LibraryRow_CatalogNotRead");
            if (Item.PackageId == null) return L.Get("LibraryRow_NotDownloaded");
            return Item.UpdateAvailable ? L.Get("LibraryRow_UpdateAvailable") : L.Get("LibraryRow_UpToDate");
        }
    }
}
