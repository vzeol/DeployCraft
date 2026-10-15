namespace DeployCraft.Server.Library;

/// <summary>Logiciel proposé : son identifiant dans le catalogue public winget, son nom et sa catégorie.</summary>
public record LibraryApp(string Id, string Name, string Category);

/// <summary>
/// Sélection de logiciels courants dans un parc (bureautique, Internet, multimédia, enseignement). Chacun a été vérifié :
/// le catalogue propose pour lui un installeur pour tout le poste, lançable sans fenêtre.
/// </summary>
public static class LibraryCatalog
{
    public static readonly IReadOnlyList<LibraryApp> Apps =
    [
        new("TheDocumentFoundation.LibreOffice", "LibreOffice", "Bureautique"),
        new("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Bureautique"),
        new("geeksoftwareGmbH.PDF24Creator", "PDF24 Creator", "Bureautique"),
        new("SumatraPDF.SumatraPDF", "SumatraPDF", "Bureautique"),
        new("Notepad++.Notepad++", "Notepad++", "Bureautique"),
        new("Google.Chrome", "Google Chrome", "Internet"),
        new("Mozilla.Firefox", "Mozilla Firefox", "Internet"),
        new("Mozilla.Firefox.ESR", "Mozilla Firefox ESR", "Internet"),
        new("Zoom.Zoom", "Zoom", "Internet"),
        new("VideoLAN.VLC", "VLC", "Multimédia"),
        new("Audacity.Audacity", "Audacity", "Multimédia"),
        new("GIMP.GIMP.3", "GIMP", "Multimédia"),
        new("Inkscape.Inkscape", "Inkscape", "Multimédia"),
        new("IrfanSkiljan.IrfanView", "IrfanView", "Multimédia"),
        new("Greenshot.Greenshot", "Greenshot", "Multimédia"),
        new("OBSProject.OBSStudio", "OBS Studio", "Multimédia"),
        new("7zip.7zip", "7-Zip", "Utilitaires"),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", "Utilitaires"),
        new("EclipseAdoptium.Temurin.21.JRE", "Java (Temurin 21)", "Utilitaires"),
        new("GeoGebra.Classic", "GeoGebra Classique", "Enseignement"),
        new("AivarAnnamaa.Thonny", "Thonny", "Enseignement"),
        new("ArduinoSA.IDE.stable", "Arduino IDE", "Enseignement"),
        new("Python.Python.3.13", "Python 3.13", "Enseignement"),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Enseignement"),
    ];

    /// <summary>Dossier de la fiche dans le dépôt du catalogue : manifests/v/VideoLAN/VLC.</summary>
    public static string ManifestFolder(string id)
    {
        var parts = id.Split('.');
        return $"manifests/{char.ToLowerInvariant(id[0])}/{string.Join('/', parts)}";
    }
}
