using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Details tab of a package: what it does and where each setting comes from (package or preferences).</summary>
public static class PackageDetailRows
{
    public static IReadOnlyList<(string Label, string Value)> For(Package p, DeploymentDefaults? defaults, IReadOnlyList<PackageFolder> folders,
        IReadOnlyList<Package> packages)
    {
        var d = defaults ?? new DeploymentDefaults();
        string Setting<T>(T? own, T fallback, Func<T, string> text) where T : struct =>
            own is { } v ? text(v) : $"Préférences ({text(fallback)})";
        var enabled = p.Steps.Count(s => s.Enabled);
        return
        [
            ("Nom", p.Name),
            ("Version", string.IsNullOrWhiteSpace(p.Version) ? "—" : p.Version),
            ("Description", string.IsNullOrWhiteSpace(p.Description) ? "—" : p.Description),
            ("Dossier", PackageDetails.FolderPath(p.FolderId, folders)),
            ("Étapes", enabled == p.Steps.Count ? $"{p.Steps.Count}" : $"{p.Steps.Count}, dont {p.Steps.Count - enabled} désactivée(s)"),
            ("Modifié le", Ui.LocalTime(p.ModifiedUtc)),
            ("Exécuter en tant que", Setting(p.RunAs, d.RunAs, Ui.RunAsText)),
            ("Mode de copie", Setting(p.CopyMode, d.CopyMode, Ui.CopyModeText)),
            ("Délai max par étape", Setting(p.TimeoutMinutes, d.TimeoutMinutes, m => $"{m} min")),
            ("Conditions", PackageDetails.ConditionsText(p.Conditions)),
            ("Postes hors ligne", PackageDetails.OfflineText(p.Offline ?? d.Offline, p.Offline == null)),
            ("Utilisé dans", PackageDetails.UsedByText(p.Id, packages)),
        ];
    }
}
