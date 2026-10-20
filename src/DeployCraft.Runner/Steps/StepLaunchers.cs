using System;
using System.Collections.Generic;

namespace DeployCraft.Runner.Steps
{
    /// <summary>The launcher of each step type that starts a process, by the type name sent in the job.</summary>
    internal static class StepLaunchers
    {
        private static readonly Dictionary<string, IStepLauncher> ByType = new Dictionary<string, IStepLauncher>(StringComparer.OrdinalIgnoreCase)
        {
            ["install"] = new InstallStepLauncher(),
            ["command"] = new CommandStepLauncher(),
            ["powershell"] = new PowerShellStepLauncher(),
            ["copyfiles"] = new CopyFilesStepLauncher(),
            ["reboot"] = new RebootStepLauncher(),
        };

        public static IStepLauncher For(string type) =>
            ByType.TryGetValue(type ?? "", out var launcher) ? launcher : throw new NotSupportedException("Type d'étape inconnu : " + type);

        public static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);
    }
}
