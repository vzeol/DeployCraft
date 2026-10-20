using System.Diagnostics;
using System.IO;

namespace DeployCraft.Runner.Steps
{
    /// <summary>Command: one command line run by cmd.</summary>
    internal sealed class CommandStepLauncher : IStepLauncher
    {
        public ProcessStartInfo Build(IStepHost host, int index, JobStep step) =>
            new ProcessStartInfo(Path.Combine(StepLaunchers.System32, "cmd.exe"), "/c " + step.Command);
    }
}
