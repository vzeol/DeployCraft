using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeployCraft.Runner.Steps
{
    /// <summary>PowerShell: the script is written to exec\ and run by Windows PowerShell.</summary>
    internal sealed class PowerShellStepLauncher : IStepLauncher
    {
        public ProcessStartInfo Build(IStepHost host, int index, JobStep step)
        {
            var script = Path.Combine(host.ExecDir, $"step{index + 1}.ps1");
            // UTF-8 BOM: otherwise Windows PowerShell 5.1 reads the script as ANSI.
            File.WriteAllText(script, step.Command ?? "", new UTF8Encoding(true));
            return new ProcessStartInfo(
                Path.Combine(StepLaunchers.System32, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" {step.Arguments ?? ""}".Trim());
        }
    }
}
