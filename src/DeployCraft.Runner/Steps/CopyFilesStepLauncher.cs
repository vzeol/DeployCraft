using System;
using System.Diagnostics;
using System.IO;

namespace DeployCraft.Runner.Steps
{
    /// <summary>File copy: robocopy from exec\ (push) or straight from the share (pull) to the destination folder.</summary>
    internal sealed class CopyFilesStepLauncher : IStepLauncher
    {
        public ProcessStartInfo Build(IStepHost host, int index, JobStep step)
        {
            if (string.IsNullOrWhiteSpace(step.CopyDestination)) throw new ArgumentException(RunnerTexts.Get("Launcher_CopyNoDestination"));
            string args;
            if (string.IsNullOrEmpty(step.SourcePath))
                args = FileCopy.Arguments(Path.Combine(host.ExecDir, step.Command ?? ""), step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
            else
            {
                // Pull: the computer reads the share itself.
                host.Log(RunnerTexts.Format("Launcher_CopyingFrom", step.SourcePath));
                var isFile = File.Exists(step.SourcePath);
                if (!isFile && !Directory.Exists(step.SourcePath))
                    throw new FileNotFoundException(RunnerTexts.Format("Launcher_CopyNotFound", step.SourcePath));
                args = FileCopy.PullArguments(step.SourcePath, isFile, step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
            }
            // Through cmd: the %variables% of the destination take the values of the step identity.
            return new ProcessStartInfo(Path.Combine(StepLaunchers.System32, "cmd.exe"), "/c robocopy.exe " + args);
        }
    }
}
