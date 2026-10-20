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
            if (string.IsNullOrWhiteSpace(step.CopyDestination)) throw new ArgumentException("Copie de fichiers : aucun dossier de destination.");
            string args;
            if (string.IsNullOrEmpty(step.SourcePath))
                args = FileCopy.Arguments(Path.Combine(host.ExecDir, step.Command ?? ""), step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
            else
            {
                // Pull : le poste lit lui-même le partage.
                host.Log($"Copie depuis {step.SourcePath}");
                var isFile = File.Exists(step.SourcePath);
                if (!isFile && !Directory.Exists(step.SourcePath))
                    throw new FileNotFoundException("Copie de fichiers : fichier ou dossier introuvable sur le partage (ou illisible avec l'identité de l'étape) : " + step.SourcePath);
                args = FileCopy.PullArguments(step.SourcePath, isFile, step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
            }
            // Par cmd : les %variables% de la destination prennent les valeurs de l'identité de l'étape.
            return new ProcessStartInfo(Path.Combine(StepLaunchers.System32, "cmd.exe"), "/c robocopy.exe " + args);
        }
    }
}
