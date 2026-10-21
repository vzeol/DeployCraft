using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace DeployCraft.Runner
{
    internal static class Program
    {
        /// <summary>
        /// Started by the service manager: the working folder is the exe's.
        /// To test by hand: DeployCraft.Runner.exe --console [folder]
        /// </summary>
        private static int Main(string[] args)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;

            if (args.Length > 0 && args[0] == "--console")
            {
                var dir = args.Length > 1 ? Path.GetFullPath(args[1]) : exeDir;
                new StepExecutor(dir).Run();
                Console.WriteLine(File.ReadAllText(Path.Combine(dir, "status.json")));
                return 0;
            }

            ServiceBase.Run(new RunnerService(exeDir));
            return 0;
        }
    }

    internal class RunnerService : ServiceBase
    {
        private readonly StepExecutor _executor;
        private Thread _worker;
        private volatile bool _finished;

        public RunnerService(string slotDir)
        {
            _executor = new StepExecutor(slotDir);
            CanStop = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            // OnStart must return quickly: the work is done in a thread.
            _worker = new Thread(() =>
            {
                _executor.Run();
                _finished = true;
                // The service stops by itself; the server then takes care of removing it.
                Stop();
            })
            { IsBackground = true, Name = "DeployCraftWorker" };
            _worker.Start();
        }

        /// <summary>Stop requested by the server before the end = cancellation of the deployment on this computer.</summary>
        protected override void OnStop()
        {
            if (_finished) return;
            _executor.Abort();
            _worker?.Join(TimeSpan.FromSeconds(15));
        }
    }
}
