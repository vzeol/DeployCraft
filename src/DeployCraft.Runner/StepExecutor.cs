using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeployCraft.Runner.Steps;

namespace DeployCraft.Runner
{
    /// <summary>Runs the steps of a job in the exec\ folder and keeps status.json up to date.</summary>
    internal class StepExecutor : IStepHost
    {
        private const int MaxOutputChars = 64 * 1024;

        private readonly string _slotDir;
        private readonly string _execDir;
        private readonly string _statusPath;
        private volatile bool _aborted;
        /// <summary>PID of the current step (0 = none).</summary>
        private volatile int _current;

        public StepExecutor(string slotDir)
        {
            _slotDir = slotDir;
            _execDir = Path.Combine(slotDir, "exec");
            _statusPath = Path.Combine(slotDir, "status.json");
        }

        /// <summary>Cancellation requested by the server (service stop): kills the current step and skips the following ones.</summary>
        public void Abort()
        {
            _aborted = true;
            KillTree(_current);
        }

        private static void KillTree(int pid)
        {
            if (pid == 0) return;
            try
            {
                // Installers often start child processes (msiexec...): the whole tree is killed.
                using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", $"/T /F /PID {pid}") { CreateNoWindow = true, UseShellExecute = false }))
                    kill?.WaitForExit(10000);
            }
            catch { /* already finished */ }
        }

        public void Run()
        {
            var status = new RunStatus();
            try
            {
                var job = Json.Read<Job>(Path.Combine(_slotDir, "job.json"));
                RunnerTexts.Language = job.Language == "en" ? "en" : "fr";
                Log($"Job chargé : {job.Steps.Count} étape(s)");
                status.Network = LocalFacts.NetworkCards();
                Json.Write(_statusPath, status);

                StepSequence.Run(job, new LocalFacts(), status, RunStep, () => Json.Write(_statusPath, status), () => _aborted, Log);
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
                Log("Erreur : " + ex);
            }

            status.State = "done";
            Json.Write(_statusPath, status);
            Log("Terminé.");
        }

        private void RunStep(int index, JobStep step, StepResult result)
        {
            if (step.Type == "pause")
            {
                result.CommandLine = $"Pause de {step.Seconds} s";
                if (StepSequence.Wait(step.Seconds, () => _aborted, Thread.Sleep)) result.State = "success";
                else
                {
                    result.State = "failed";
                    result.Error = "Annulé.";
                }
                return;
            }

            if (step.Type == "message")
            {
                ShowMessage(step, result);
                return;
            }

            ProcessStartInfo psi;
            try
            {
                psi = BuildStartInfo(index, step);
            }
            catch (Exception ex)
            {
                result.State = "failed";
                result.Error = ex.Message;
                return;
            }

            var install = step.Type == "install";
            result.CommandLine = install ? step.CommandLine : $"{psi.FileName} {psi.Arguments}".Trim();
            psi.WorkingDirectory = _execDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            // Most console tools write in the OEM code page (850 on French systems), others in UTF-16:
            // the output is kept as bytes and decoded at the end (ProcessOutput).
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.EnvironmentVariables["cd"] = _execDir;
            var output = new ProcessOutput(MaxOutputChars * 2);

            try
            {
                int exitCode;
                if (step.RunAsLoggedOnUser)
                {
                    using (var process = UserProcess.Start(psi.FileName, psi.Arguments, _execDir, output.Add))
                    {
                        _current = process.Id;
                        Log($"Étape {index + 1} lancée dans la session de {process.User ?? "l'utilisateur connecté"}");
                        if (!process.WaitForExit(step.TimeoutSeconds * 1000))
                        {
                            TimedOut(process.Id, step, result);
                            return;
                        }
                        process.WaitForOutput();
                        exitCode = process.ExitCode;
                    }
                }
                else
                {
                    using (var process = new Process { StartInfo = psi })
                    {
                        process.Start();
                        _current = process.Id;
                        // No input possible: an installer waiting for an answer fails instead of blocking.
                        process.StandardInput.Close();
                        var readers = new[] { process.StandardOutput.BaseStream, process.StandardError.BaseStream }
                            .Select(stream => new Thread(() => output.ReadAll(stream)) { IsBackground = true, Name = "ProcessOutput" })
                            .ToList();
                        readers.ForEach(r => r.Start());

                        if (!process.WaitForExit(step.TimeoutSeconds * 1000))
                        {
                            TimedOut(process.Id, step, result);
                            return;
                        }
                        // The streams close with the program (or with the last child that inherited them).
                        readers.ForEach(r => r.Join(10000));
                        exitCode = process.ExitCode;
                    }
                }

                if (_aborted)
                {
                    result.State = "failed";
                    result.Error = "Annulé.";
                    return;
                }
                result.ExitCode = exitCode;
                result.State = step.SuccessCodes.Contains(exitCode) ? "success" : "failed";
                if (install && result.State == "failed") result.Error = InstallLog.ErrorMessage(step.Command, exitCode);
            }
            catch (Exception ex)
            {
                result.State = "failed";
                result.Error = ex.Message;
            }
            finally
            {
                _current = 0;
                result.Output = output.Text(oem);
                if (install)
                {
                    result.Output = InstallLog.Append(result.Output, _execDir, MaxOutputChars);
                    Keep(step);
                }
            }
        }

        /// <summary>
        /// Message to the logged-on users: one window per session, started in the session (the runner runs as
        /// System). With no click expected, the step succeeds as soon as the window is shown.
        /// </summary>
        private void ShowMessage(JobStep step, StepResult result)
        {
            var windows = new List<UserProcess>();
            try
            {
                var sessions = UserProcess.ActiveSessions();
                var untilClick = step.Seconds <= 0;
                result.CommandLine = "Message pour " + (sessions.Count == 0 ? "personne" : string.Join(", ", sessions.Select(s => s.Value ?? "session " + s.Key)));
                // Until the click: the wait is bounded by the step's maximum delay; with no wait, the window stays open.
                var seconds = untilClick ? (step.WaitForUser ? step.TimeoutSeconds : 0) : step.Seconds;
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                var args = MessageOutcome.Arguments(step.Arguments, step.Command, seconds);
                foreach (var session in sessions)
                    windows.Add(UserProcess.Start(powershell, args, _execDir, (_, __) => { }, session.Key));

                List<int> codes = null;
                if (step.WaitForUser)
                {
                    codes = new List<int>();
                    foreach (var window in windows)
                    {
                        while (!window.WaitForExit(500))
                        {
                            if (!_aborted) continue;
                            foreach (var w in windows) KillTree(w.Id);
                            result.State = "failed";
                            result.Error = "Annulé.";
                            return;
                        }
                        codes.Add(window.ExitCode);
                    }
                }
                var ok = MessageOutcome.Succeeded(sessions.Count, codes, untilClick, out var text);
                result.State = ok ? "success" : "failed";
                if (ok) result.Output = text;
                else result.Error = text;
            }
            catch (Exception ex)
            {
                result.State = "failed";
                result.Error = ex.Message;
            }
            finally
            {
                foreach (var window in windows) window.Dispose();
            }
        }

        private static void TimedOut(int pid, JobStep step, StepResult result)
        {
            KillTree(pid);
            result.State = "failed";
            result.Error = $"Délai dépassé ({step.TimeoutSeconds / 60} min). Paramètres silencieux manquants ?";
        }

        /// <summary>Pull mode: an additional file copied into exec\ from the share, with the runner identity.</summary>
        private void PullFile(string path)
        {
            Log($"Copie depuis {path}");
            try
            {
                File.Copy(path, Path.Combine(_execDir, Path.GetFileName(path)), true);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                throw new IOException($"Mode pull : fichier additionnel illisible ou introuvable : {path} ({ex.Message}).", ex);
            }
        }

        /// <summary>
        /// "Keep the file on the computer": the install file and its additional files are copied into
        /// the requested folder (with all of exec\ if the step copies the whole folder). A failure is noted without changing the result.
        /// </summary>
        private void Keep(JobStep step)
        {
            if (string.IsNullOrWhiteSpace(step.KeepFolder)) return;
            try
            {
                var folder = Environment.ExpandEnvironmentVariables(step.KeepFolder);
                Directory.CreateDirectory(folder);
                if (step.IncludeDirectory) CopyDirectory(_execDir, folder);
                else
                    foreach (var name in new[] { step.Command }.Concat(step.AdditionalFiles ?? Enumerable.Empty<string>()))
                    {
                        var file = Path.Combine(_execDir, name ?? "");
                        if (File.Exists(file)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
                    }
                Log($"Fichier d'installation gardé dans {folder}");
            }
            catch (Exception ex)
            {
                Log("Fichier d'installation non gardé : " + ex.Message);
            }
        }

        /// <summary>Pull mode: the computer itself copies the file (or its folder) from the share.</summary>
        private void Pull(JobStep step)
        {
            Log($"Copie depuis {step.SourcePath}");
            try
            {
                if (step.IncludeDirectory) CopyDirectory(Path.GetDirectoryName(step.SourcePath), _execDir);
                else File.Copy(step.SourcePath, Path.Combine(_execDir, Path.GetFileName(step.SourcePath)), true);
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException("Mode pull : fichier introuvable sur le partage : " + step.SourcePath);
            }
            catch (DirectoryNotFoundException)
            {
                throw new DirectoryNotFoundException("Mode pull : dossier introuvable sur le partage : " + Path.GetDirectoryName(step.SourcePath));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                // As Local System, it is the computer account of the machine (DOMAIN\COMPUTER$) that reads the share.
                string who;
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    who = identity.IsSystem ? "le compte ordinateur du poste (" + Environment.MachineName + "$)" : identity.Name;
                throw new IOException($"Mode pull : lecture de {step.SourcePath} impossible avec {who} ({ex.Message}).", ex);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, dir.Substring(source.Length).TrimStart('\\')));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(destination, file.Substring(source.Length).TrimStart('\\')), true);
        }

        private ProcessStartInfo BuildStartInfo(int index, JobStep step)
        {
            if (step.PullFiles != null)
                foreach (var file in step.PullFiles) PullFile(file);
            return StepLaunchers.For(step.Type).Build(this, index, step);
        }

        string IStepHost.ExecDir => _execDir;

        void IStepHost.Pull(JobStep step) => Pull(step);

        void IStepHost.Log(string message) => Log(message);

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(_slotDir, "runner.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{message}{Environment.NewLine}");
            }
            catch { /* the log must never make a deployment fail */ }
        }
    }
}
