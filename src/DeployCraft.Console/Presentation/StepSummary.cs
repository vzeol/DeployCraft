using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>How a step is shown in the lists: title, type, one-line summary.</summary>
public static class StepSummary
{
    /// <summary>Title of the step, flagged if it is disabled.</summary>
    public static string Name(PackageStep s) => s.Enabled ? s.Name : $"{s.Name} (désactivée)";

    /// <summary>Summary of a step for the lists: file, first command line, pause duration, copy, restart, message, nested package.</summary>
    public static string Details(PackageStep s, Func<long, Package?> lookup) => s.Type switch
    {
        StepType.Install => $"{s.File} {s.Arguments}".Trim(),
        StepType.Pause => $"{s.PauseSeconds} s",
        StepType.CopyFiles => $"{s.CopySource} → {s.CopyDestination}",
        StepType.Reboot => $"{(s.RebootShutdownOnly ? "Arrêt" : "Redémarrage")} dans {s.RebootDelaySeconds} s",
        StepType.Message => (s.MessageText ?? "").Split('\n')[0].Trim(),
        StepType.Nested => s.NestedPackageId is { } id && lookup(id) is { } p
            ? $"Paquet : {p.Name} ({p.Steps.Count} étape{(p.Steps.Count > 1 ? "s" : "")})"
            : "Paquet : (introuvable)",
        _ => (s.Command ?? "").Split('\n')[0].Trim(),
    };

    public static string TypeText(StepType t) => t switch
    {
        StepType.Install => "Installer",
        StepType.Command => "Commande",
        StepType.Pause => "Pause",
        StepType.CopyFiles => "Copie de fichiers",
        StepType.Reboot => "Redémarrer",
        StepType.Message => "Message",
        StepType.Nested => "Paquet imbriqué",
        _ => "PowerShell",
    };
}
