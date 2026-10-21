using System.IO;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Texts and layout rules of the step editor that do not depend on the window.</summary>
public static class StepTexts
{
    /// <summary>Title of a step in the package tree.</summary>
    public static string Header(PackageStep step, int index) =>
        (string.IsNullOrWhiteSpace(step.Name)
            ? L.Format("StepTexts_HeaderUnnamed", index + 1, StepSummary.TypeText(step.Type))
            : $"{index + 1}. {step.Name}") + (step.Enabled ? "" : L.Get("StepSummary_DisabledSuffix"));

    /// <summary>An .exe with no silent parameter waits for a click that will never come: we recall the usual parameters.</summary>
    public static string SilentHint(string? file) => Path.GetExtension(file ?? "").ToLowerInvariant() switch
    {
        ".exe" => L.Get("StepTexts_SilentExe"),
        ".msi" or ".msp" or ".msu" => L.Get("StepTexts_SilentMsi"),
        _ => "",
    };

    public static string ErrorModeHint(PackageStep step) => step.Type == StepType.Nested
        ? L.Get("StepTexts_ErrorNested")
        : step.ErrorMode switch
        {
            ErrorMode.Continue => L.Get("StepTexts_ErrorContinue"),
            ErrorMode.StopSuccess => L.Get("StepTexts_ErrorStopSuccess"),
            _ => L.Get("StepTexts_ErrorStopFail"),
        };

    public static string TimeoutHint(StepType type, int packageTimeout) => type switch
    {
        StepType.Pause => L.Get("StepTexts_TimeoutPause"),
        StepType.Nested => L.Get("StepTexts_TimeoutNested"),
        StepType.Reboot => L.Format("StepTexts_TimeoutReboot", packageTimeout),
        StepType.Message => L.Format("StepTexts_TimeoutMessage", packageTimeout),
        _ => L.Format("StepTexts_TimeoutDefault", packageTimeout),
    };

    /// <summary>Windows Installer options shown for the file: restart / quiet, operation (not for .msp), repair (not for .msu).</summary>
    public static (bool Msi, bool Operation, bool Repair) InstallOptions(string? file)
    {
        var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
        var msi = InstallCommand.HasMsiOptions(file);
        return (msi, msi && ext != ".msp", ext != ".msu");
    }

    /// <summary>Fields of the step editor shown for this step type.</summary>
    public static StepFields Fields(StepType type) => new(
        Install: type == StepType.Install,
        AdditionalFiles: type is StepType.Install or StepType.Command or StepType.PowerShell,
        Command: type is StepType.Command or StepType.PowerShell,
        Copy: type == StepType.CopyFiles,
        Reboot: type == StepType.Reboot,
        Message: type == StepType.Message,
        Nested: type == StepType.Nested,
        Pause: type == StepType.Pause,
        SuccessCodes: type is StepType.Install or StepType.Command or StepType.PowerShell);
}

/// <summary>Which parts of the step editor a step type uses.</summary>
public record StepFields(bool Install, bool AdditionalFiles, bool Command, bool Copy, bool Reboot, bool Message, bool Nested, bool Pause,
    bool SuccessCodes);
