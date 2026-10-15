namespace DeployCraft.Server.Library;

/// <summary>Logiciels courants proposés tant que rien n'est cherché, et chemin des fiches dans le catalogue.</summary>
public static class LibraryCatalog
{
    /// <summary>
    /// Sélection de logiciels courants dans un parc (bureautique, Internet, multimédia, enseignement). Chacun a été vérifié :
    /// le catalogue propose pour lui un installeur pour tout le poste, lançable sans fenêtre.
    /// </summary>
    public static readonly IReadOnlyList<string> Suggested =
    [
        "TheDocumentFoundation.LibreOffice",
        "Adobe.Acrobat.Reader.64-bit",
        "geeksoftwareGmbH.PDF24Creator",
        "SumatraPDF.SumatraPDF",
        "Notepad++.Notepad++",
        "Google.Chrome",
        "Mozilla.Firefox",
        "Mozilla.Firefox.ESR",
        "Zoom.Zoom",
        "VideoLAN.VLC",
        "Audacity.Audacity",
        "GIMP.GIMP.3",
        "Inkscape.Inkscape",
        "IrfanSkiljan.IrfanView",
        "Greenshot.Greenshot",
        "OBSProject.OBSStudio",
        "7zip.7zip",
        "KeePassXCTeam.KeePassXC",
        "EclipseAdoptium.Temurin.21.JRE",
        "GeoGebra.Classic",
        "AivarAnnamaa.Thonny",
        "ArduinoSA.IDE.stable",
        "Python.Python.3.13",
        "Microsoft.VisualStudioCode",
    ];

    /// <summary>Dossier de la fiche dans le dépôt du catalogue : manifests/v/VideoLAN/VLC.</summary>
    public static string ManifestFolder(string id)
    {
        var parts = id.Split('.');
        return $"manifests/{char.ToLowerInvariant(id[0])}/{string.Join('/', parts)}";
    }
}
