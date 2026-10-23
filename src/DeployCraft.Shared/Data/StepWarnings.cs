namespace DeployCraft.Server.Data;

/// <summary>
/// Warnings of a step, shown in the package window and recalled on save: nothing blocking,
/// but a deployment may fail or not go as planned.
/// </summary>
public static class StepWarnings
{
    /// <summary>Files and folders the server (or the computer, in pull) will have to read for this step.</summary>
    public static List<string> Paths(PackageStep step)
    {
        var paths = new List<string>();
        if (!step.Enabled) return paths;
        if (step.Type == StepType.Install && !string.IsNullOrWhiteSpace(step.File)) paths.Add(step.File.Trim());
        if (step.Type == StepType.CopyFiles && !string.IsNullOrWhiteSpace(step.CopySource)) paths.Add(step.CopySource.Trim());
        if (step.Type is StepType.Install or StepType.Command or StepType.PowerShell)
            paths.AddRange(PackageStep.SplitLines(step.AdditionalFiles));
        return paths;
    }

    /// <param name="copyMode">Effective copy mode of the package.</param>
    /// <param name="missing">Paths not found from the server (checked by the service).</param>
    public static List<string> For(PackageStep step, CopyMode copyMode, IReadOnlyCollection<string> missing)
    {
        var warnings = new List<string>();
        if (!step.Enabled) return warnings;

        if (step.Type == StepType.Install && string.IsNullOrWhiteSpace(step.CustomCommandLine)
            && System.IO.Path.GetExtension(step.File ?? "").Equals(".exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(step.Arguments))
            warnings.Add(Msg.Get("StepWarnings_NoSilentArgs"));

        foreach (var path in Paths(step))
        {
            if (missing.Contains(path, StringComparer.OrdinalIgnoreCase))
                warnings.Add(Msg.Format("StepWarnings_NotFound", path));
            else if (copyMode == CopyMode.Pull && !path.StartsWith(@"\\", StringComparison.Ordinal))
                warnings.Add(Msg.Format("StepWarnings_PullNotShare", path));
        }
        return warnings;
    }
}
