using System.Diagnostics;
using System.IO;

namespace DeployCraft.Runner.Steps
{
    /// <summary>Restart: only schedules the restart; the server then waits for the computer to come back.</summary>
    internal sealed class RebootStepLauncher : IStepLauncher
    {
        public ProcessStartInfo Build(IStepHost host, int index, JobStep step) =>
            new ProcessStartInfo(Path.Combine(StepLaunchers.System32, "shutdown.exe"), Shutdown.Arguments(step.ShutdownOnly, step.Seconds, step.Command));
    }
}
