namespace DeployCraft.Server.Library;

/// <summary>Common software offered as long as nothing is searched, and path of the entries in the catalog.</summary>
public static class LibraryCatalog
{
    /// <summary>
    /// Selection of common software for a fleet (office, Internet, multimedia, teaching). Each was checked:
    /// the catalog offers an installer for the whole machine, launchable without a window.
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

    /// <summary>Entry folder in the catalog repository: manifests/v/VideoLAN/VLC.</summary>
    public static string ManifestFolder(string id)
    {
        var parts = id.Split('.');
        return $"manifests/{char.ToLowerInvariant(id[0])}/{string.Join('/', parts)}";
    }
}
