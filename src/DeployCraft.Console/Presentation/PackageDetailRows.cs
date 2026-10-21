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
            own is { } v ? text(v) : L.Format("PackageDetailRows_FromPreferences", text(fallback));
        var enabled = p.Steps.Count(s => s.Enabled);
        return
        [
            (L.Get("PackageDetailRows_Name"), p.Name),
            (L.Get("PackageDetailRows_Version"), string.IsNullOrWhiteSpace(p.Version) ? "—" : p.Version),
            (L.Get("PackageDetailRows_Description"), string.IsNullOrWhiteSpace(p.Description) ? "—" : p.Description),
            (L.Get("PackageDetailRows_Folder"), PackageDetails.FolderPath(p.FolderId, folders)),
            (L.Get("PackageDetailRows_Steps"), enabled == p.Steps.Count ? $"{p.Steps.Count}" : L.Format("PackageDetailRows_StepsSomeDisabled", p.Steps.Count, p.Steps.Count - enabled)),
            (L.Get("PackageDetailRows_Modified"), Ui.LocalTime(p.ModifiedUtc)),
            (L.Get("PackageDetailRows_RunAs"), Setting(p.RunAs, d.RunAs, Ui.RunAsText)),
            (L.Get("PackageDetailRows_CopyMode"), Setting(p.CopyMode, d.CopyMode, Ui.CopyModeText)),
            (L.Get("PackageDetailRows_StepTimeout"), Setting(p.TimeoutMinutes, d.TimeoutMinutes, m => L.Format("PackageDetailRows_Minutes", m))),
            (L.Get("PackageDetailRows_Conditions"), PackageDetails.ConditionsText(p.Conditions)),
            (L.Get("PackageDetailRows_Offline"), PackageDetails.OfflineText(p.Offline ?? d.Offline, p.Offline == null)),
            (L.Get("PackageDetailRows_UsedIn"), PackageDetails.UsedByText(p.Id, packages)),
        ];
    }
}
