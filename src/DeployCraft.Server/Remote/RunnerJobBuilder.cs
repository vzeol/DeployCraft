using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Turns a package into the runner's instructions (job.json) and lists the files to drop in the slot's exec\ folder.
/// Nothing is copied here: the copies are added to a <see cref="CopyBatch"/> run by the session.
/// </summary>
internal static class RunnerJobBuilder
{
    /// <summary>Instructions of the steps from <paramref name="startStep"/>, with the identity each one runs under.</summary>
    public static (RunnerJob Job, List<RunAsMode> Modes) Build(Package package, int startStep, RunAsMode runAs, bool hasAccount,
        CopyMode copyMode, bool isLocal, string exec, CopyBatch copies)
    {
        var job = new RunnerJob { Conditions = RunnerConditions.From(package.Conditions), FirstNumber = startStep };
        var modes = new List<RunAsMode>();
        foreach (var step in StepsFrom(package, startStep))
        {
            var mode = RunAsPlan.Effective(step, runAs, hasAccount);
            modes.Add(mode);
            var jobStep = ToJobStep(step, package, mode);

            // Disabled step: sent so it shows as "Ignoré" (skipped), but no file is copied or checked.
            if (step.Enabled && step.Type is StepType.Install or StepType.Command or StepType.PowerShell)
                StageAdditionalFiles(step, jobStep, copyMode, exec, copies);

            if (step.Type == StepType.Install && step.Enabled)
            {
                if (string.IsNullOrWhiteSpace(step.File)) throw new DeployException(Msg.Format("TargetSession_NoInstallFile", jobStep.Name));
                jobStep.Command = Path.GetFileName(step.File);
                jobStep.CommandLine = InstallCommand.For(step);
                if (step.KeepInstallFile) jobStep.KeepFolder = KeepFolder(package.Name);
                // Pull: the computer reads the share itself, with the runner's identity. A local path stays push.
                if (copyMode == CopyMode.Pull && step.File.StartsWith(@"\\"))
                {
                    jobStep.SourcePath = step.File;
                    jobStep.IncludeDirectory = step.IncludeDirectory;
                    job.Steps.Add(jobStep);
                    continue;
                }
                // Push: the file is read by the server (with the deployment account if it is on a share).
                if (!File.Exists(step.File)) throw new DeployException(Msg.Format("TargetSession_InstallFileMissing", step.File));
                if (step.IncludeDirectory) CopyDirectory(Path.GetDirectoryName(step.File)!, exec, copies);
                else copies.Add(step.File, Path.Combine(exec, Path.GetFileName(step.File)));
            }
            else if (step.Type == StepType.Reboot && step.Enabled && isLocal)
                throw new DeployException(Msg.Format("TargetSession_NoServerRestart", jobStep.Name));
            else if (step.Type == StepType.CopyFiles && step.Enabled)
            {
                if (string.IsNullOrWhiteSpace(step.CopySource)) throw new DeployException(Msg.Format("TargetSession_NothingToCopy", jobStep.Name));
                if (copyMode == CopyMode.Pull && step.CopySource.StartsWith(@"\\"))
                    jobStep.SourcePath = step.CopySource;
                else
                {
                    // Push: the server drops the selected files into exec\copie-N, which the computer then copies as they are.
                    jobStep.Command = $"copie-{startStep + job.Steps.Count}";
                    StageCopy(step.CopySource, step.CopyPatterns, step.CopySubfolders, Path.Combine(exec, jobStep.Command), copies);
                    jobStep.CopyPatterns = ["*"];
                    jobStep.CopySubfolders = true;
                }
            }
            job.Steps.Add(jobStep);
        }
        return (job, modes);
    }

    /// <summary>
    /// File copy in push mode: drops file <paramref name="source"/> into <paramref name="target"/>, or the
    /// files of the folder that match the patterns (with subfolders if requested), keeping their tree structure.
    /// </summary>
    /// <param name="batch">List to add the copies to (null = copy right away).</param>
    internal static void StageCopy(string source, string? patterns, bool subfolders, string target, CopyBatch? batch = null)
    {
        var copies = batch ?? new CopyBatch();
        Directory.CreateDirectory(target);
        if (File.Exists(source))
        {
            copies.Add(source, Path.Combine(target, Path.GetFileName(source)));
            if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
            return;
        }
        if (!Directory.Exists(source)) throw new DeployException(Msg.Format("TargetSession_CopySourceMissing", source));

        var option = subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        // As on the computer: subfolders are recreated even when empty.
        if (subfolders)
            foreach (var dir in Directory.EnumerateDirectories(source, "*", option))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var pattern in PackageStep.SplitPatterns(patterns))
            foreach (var file in Directory.EnumerateFiles(source, pattern, option))
                copies.Add(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
    }

    /// <summary>
    /// Additional files: dropped by the server next to the command (exec\), or, in pull mode, read by the computer
    /// itself from the share. A missing file stops the computer's preparation.
    /// </summary>
    /// <param name="batch">List to add the copies to (null = copy right away).</param>
    internal static void StageAdditionalFiles(PackageStep step, RunnerJobStep jobStep, CopyMode copyMode, string exec, CopyBatch? batch = null)
    {
        var copies = batch ?? new CopyBatch();
        var files = PackageStep.SplitLines(step.AdditionalFiles).Select(Environment.ExpandEnvironmentVariables).ToList();
        if (files.Count == 0) return;
        jobStep.AdditionalFiles = [.. files.Select(Path.GetFileName).OfType<string>()];
        foreach (var file in files)
        {
            if (copyMode == CopyMode.Pull && file.StartsWith(@"\\"))
            {
                (jobStep.PullFiles ??= []).Add(file);
                continue;
            }
            if (!File.Exists(file)) throw new DeployException(Msg.Format("TargetSession_ExtraFileMissing", jobStep.Name, file));
            copies.Add(file, Path.Combine(exec, Path.GetFileName(file)));
        }
        if (batch == null) copies.Run(BandwidthLimiter.Copies, null);
    }

    /// <summary>Folder on the computer where a package's installation files are kept (option of the "Installer" step).</summary>
    internal static string KeepFolder(string packageName)
    {
        var safe = string.Concat(packageName.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        return $@"%ProgramData%\DeployCraft\Installeurs\{(safe.Length > 0 ? safe : "Paquet")}";
    }

    /// <summary>Steps sent to the computer when (re)starting at step N.</summary>
    internal static IEnumerable<PackageStep> StepsFrom(Package package, int startStep) =>
        package.Steps.Skip(Math.Max(0, startStep - 1));

    /// <summary>Instructions of one step for the runner (apart from files to copy), run under identity <paramref name="runAs"/>.</summary>
    internal static RunnerJobStep ToJobStep(PackageStep step, Package package, RunAsMode runAs)
    {
        if (step.Type == StepType.Nested)
            throw new DeployException(Msg.Format("TargetSession_NestedNotExpanded", step.Name));
        return new()
        {
            Name = string.IsNullOrWhiteSpace(step.Name) ? step.Type.ToString() : step.Name,
            Type = step.Type.ToString().ToLowerInvariant(),
            Arguments = step.Type == StepType.Message ? step.MessageTitle : step.Arguments,
            Command = step.Type switch { StepType.Reboot => step.RebootMessage, StepType.Message => step.MessageText, _ => step.Command },
            Seconds = step.Type switch
            {
                StepType.Pause => step.PauseSeconds,
                StepType.Reboot => step.RebootDelaySeconds,
                StepType.Message => step.MessageSeconds,
                _ => 0,
            },
            // Copy: robocopy succeeds with 0 to 7; 8 to 15 = some files could not be copied; 16 = serious error.
            SuccessCodes = step.Type == StepType.CopyFiles ? [.. Enumerable.Range(0, step.CopyIgnoreErrors ? 16 : 8)] : ParseCodes(step.SuccessCodes),
            // A pause lasts as long as it lasts: its timeout (which counts in the server's wait) follows its duration, like that
            // of a message waited for longer than the step timeout.
            TimeoutSeconds = step.Type == StepType.Pause
                ? Math.Max(1, step.PauseSeconds) + 60
                : Math.Max(Math.Max(1, step.TimeoutMinutes ?? package.TimeoutMinutes ?? 60) * 60,
                    step.Type == StepType.Message && step.MessageWait ? step.MessageSeconds + 60 : 0),
            ContinueOnError = step.ErrorMode == ErrorMode.Continue,
            StopSuccessOnError = step.ErrorMode == ErrorMode.StopSuccess,
            Disabled = !step.Enabled,
            CopyDestination = step.Type == StepType.CopyFiles ? step.CopyDestination : null,
            CopyPatterns = step.Type == StepType.CopyFiles ? PackageStep.SplitPatterns(step.CopyPatterns) : null,
            CopySubfolders = step.Type == StepType.CopyFiles && step.CopySubfolders,
            CopyOverwrite = step.Type == StepType.CopyFiles && step.CopyOverwrite,
            // Reboot and message go through the service, never through the logged-on user (who often lacks the right).
            RunAsLoggedOnUser = runAs == RunAsMode.LoggedOnUser && step.Type is not (StepType.Reboot or StepType.Message),
            ShutdownOnly = step.Type == StepType.Reboot && step.RebootShutdownOnly,
            WaitForUser = step.Type == StepType.Message && step.MessageWait,
            Conditions = RunnerConditions.From(step.Conditions),
            ExtraConditions = step.ExtraConditions?
                .Select(g => new RunnerConditionGroup { Label = g.Label, Conditions = RunnerConditions.From(g.Conditions) })
                .Where(g => g.Conditions != null).ToList() is { Count: > 0 } extras ? extras : null,
        };
    }

    private static void CopyDirectory(string source, string destination, CopyBatch copies)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            copies.Add(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    public static List<int> ParseCodes(string? codes)
    {
        var list = (codes ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => int.TryParse(c, out var v) ? (int?)v : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list : [0];
    }
}
