using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Ligne de la bibliothèque de paquets.</summary>
public record LibraryRow(LibraryItem Item)
{
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Latest => Item.LatestVersion ?? "—";
    public string Downloaded => Item.PackageVersion ?? "—";
    public string Installer => Item.Installer ?? "";
    /// <summary>Le logiciel peut être téléchargé (ou mis à jour) maintenant.</summary>
    public bool CanDownload => Item.Stage == null && Item.Problem == null && Item.LatestVersion != null && (Item.PackageId == null || Item.UpdateAvailable);

    public string State
    {
        get
        {
            if (Item.Stage is { } stage) return $"{stage}… {Item.Progress} %";
            if (Item.Problem != null) return $"Non disponible : {Item.Problem}";
            if (Item.Error != null) return $"Échec : {Item.Error}";
            if (Item.LatestVersion == null) return "Catalogue pas encore lu";
            if (Item.PackageId == null) return "Pas encore téléchargé";
            return Item.UpdateAvailable ? "Mise à jour disponible" : "À jour";
        }
    }
}
