using System.Diagnostics;

namespace DeployCraft.Runner.Steps
{
    /// <summary>What a step type needs from the executor to prepare its process.</summary>
    internal interface IStepHost
    {
        /// <summary>Working folder of the job on the computer (exec\), where files are copied.</summary>
        string ExecDir { get; }

        /// <summary>Pull mode: the computer copies the step file (or its folder) from the share itself.</summary>
        void Pull(JobStep step);

        void Log(string message);
    }

    /// <summary>Builds the process of one step type. Pause and Message do not start a process and are run by the executor.</summary>
    internal interface IStepLauncher
    {
        ProcessStartInfo Build(IStepHost host, int index, JobStep step);
    }
}
