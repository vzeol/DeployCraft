namespace DeployCraft.Server.Data;

/// <summary>Contenu de la fenêtre « À propos » de la console (partagé pour être testé).</summary>
public static class AboutInfo
{
    public const string ProjectUrl = "https://github.com/vzeol/DeployCraft";
    public const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    public const string Notice =
        "Logiciel libre sous licence GNU GPL version 3 : vous pouvez l'utiliser, l'étudier, le modifier et le " +
        "redistribuer selon ses termes. Il est fourni tel quel, sans aucune garantie.";

    /// <summary>Texte de l'état du service affiché dans la fenêtre.</summary>
    public static string ServiceText(string? version, string? machine, string? error) =>
        error != null ? error
        : string.IsNullOrWhiteSpace(machine) ? Version(version)
        : $"{Version(version)} sur {machine}";

    /// <summary>Résumé à coller dans un signalement de problème (bouton « Copier les informations »).</summary>
    public static string Summary(string? consoleVersion, string serviceText, string? connectedAs, string windows, string runtime)
    {
        var lines = new List<string>
        {
            "DeployCraft",
            $"Console : {Version(consoleVersion)}",
            $"Service : {serviceText}",
        };
        if (!string.IsNullOrWhiteSpace(connectedAs)) lines.Add($"Connecté en tant que : {connectedAs}");
        lines.Add($"Windows : {windows}");
        lines.Add($".NET : {runtime}");
        return string.Join(Environment.NewLine, lines);
    }

    public static string Version(string? version) => string.IsNullOrWhiteSpace(version) ? "version inconnue" : version;
}
