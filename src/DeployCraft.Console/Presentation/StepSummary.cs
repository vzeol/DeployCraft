using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>How a step is shown in the lists: title, type, one-line summary.</summary>
public static class StepSummary
{
    /// <summary>Title of the step, flagged if it is disabled.</summary>
    public static string Name(PackageStep s) => s.Enabled ? s.Name : s.Name + L.Get("StepSummary_DisabledSuffix");

    /// <summary>Summary of a step for the lists: file, first command line, pause duration, copy, restart, message, nested package.</summary>
    public static string Details(PackageStep s, Func<long, Package?> lookup) => s.Type switch
    {
        StepType.Install => $"{s.File} {s.Arguments}".Trim(),
        StepType.Pause => L.Format("StepSummary_PauseSeconds", s.PauseSeconds),
        StepType.CopyFiles => $"{s.CopySource} → {s.CopyDestination}",
        StepType.Reboot => L.Format(s.RebootShutdownOnly ? "StepSummary_ShutdownIn" : "StepSummary_RestartIn", s.RebootDelaySeconds),
        StepType.Message => (s.MessageText ?? "").Split('\n')[0].Trim(),
        StepType.Nested => s.NestedPackageId is { } id && lookup(id) is { } p
            ? L.Format(p.Steps.Count > 1 ? "StepSummary_NestedMany" : "StepSummary_NestedOne", p.Name, p.Steps.Count)
            : L.Get("StepSummary_NestedMissing"),
        _ => (s.Command ?? "").Split('\n')[0].Trim(),
    };

    public static string TypeText(StepType t) => t switch
    {
        StepType.Install => L.Get("StepSummary_TypeInstall"),
        StepType.Command => L.Get("StepSummary_TypeCommand"),
        StepType.Pause => L.Get("StepSummary_TypePause"),
        StepType.CopyFiles => L.Get("StepSummary_TypeCopyFiles"),
        StepType.Reboot => L.Get("StepSummary_TypeReboot"),
        StepType.Message => L.Get("StepSummary_TypeMessage"),
        StepType.Nested => L.Get("StepSummary_TypeNested"),
        _ => "PowerShell",
    };
}
