using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DeployCraft.Runner
{
    /// <summary>Exécute les étapes d'un job dans le dossier exec\ et tient status.json à jour.</summary>
    internal class StepExecutor
    {
        private const int MaxOutputChars = 64 * 1024;

        private readonly string _slotDir;
        private readonly string _execDir;
        private readonly string _statusPath;
        private volatile bool _aborted;
        /// <summary>PID de l'étape en cours (0 = aucune).</summary>
        private volatile int _current;

        public StepExecutor(string slotDir)
        {
            _slotDir = slotDir;
            _execDir = Path.Combine(slotDir, "exec");
            _statusPath = Path.Combine(slotDir, "status.json");
        }

        /// <summary>Annulation demandée par le serveur (arrêt du service) : tue l'étape en cours et saute les suivantes.</summary>
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
                // Les installeurs lancent souvent des sous-processus (msiexec…) : on tue tout l'arbre.
                using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", $"/T /F /PID {pid}") { CreateNoWindow = true, UseShellExecute = false }))
                    kill?.WaitForExit(10000);
            }
            catch { /* déjà terminé */ }
        }

        public void Run()
        {
            var status = new RunStatus();
            try
            {
                var job = Json.Read<Job>(Path.Combine(_slotDir, "job.json"));
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
            // Les outils console écrivent dans la page de code OEM (850 en français).
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = oem;
            psi.StandardErrorEncoding = oem;
            psi.EnvironmentVariables["cd"] = _execDir;

            var output = new StringBuilder();
            void Append(string line)
            {
                if (line == null) return;
                lock (output)
                {
                    if (output.Length < MaxOutputChars) output.AppendLine(line);
                }
            }

            try
            {
                int exitCode;
                if (step.RunAsLoggedOnUser)
                {
                    using (var process = UserProcess.Start(psi.FileName, psi.Arguments, _execDir, oem, Append))
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
                        process.OutputDataReceived += (s, e) => Append(e.Data);
                        process.ErrorDataReceived += (s, e) => Append(e.Data);
                        process.Start();
                        _current = process.Id;
                        // Aucune saisie possible : un installeur qui attend une réponse échoue au lieu de bloquer.
                        process.StandardInput.Close();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        if (!process.WaitForExit(step.TimeoutSeconds * 1000))
                        {
                            TimedOut(process.Id, step, result);
                            return;
                        }
                        process.WaitForExit(); // vide les flux de sortie
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
                lock (output) result.Output = output.ToString();
                if (install)
                {
                    result.Output = InstallLog.Append(result.Output, _execDir, MaxOutputChars);
                    Keep(step);
                }
            }
        }

        /// <summary>
        /// Message aux utilisateurs connectés : une fenêtre par session, lancée dans la session (le runner tourne en
        /// Système). Sans clic attendu, l'étape réussit dès l'affichage.
        /// </summary>
        private void ShowMessage(JobStep step, StepResult result)
        {
            var windows = new List<UserProcess>();
            try
            {
                var sessions = UserProcess.ActiveSessions();
                var untilClick = step.Seconds <= 0;
                result.CommandLine = "Message pour " + (sessions.Count == 0 ? "personne" : string.Join(", ", sessions.Select(s => s.Value ?? "session " + s.Key)));
                // Jusqu'au clic : l'attente est bornée par le délai max de l'étape ; sans attente, la fenêtre reste ouverte.
                var seconds = untilClick ? (step.WaitForUser ? step.TimeoutSeconds : 0) : step.Seconds;
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                var args = MessageOutcome.Arguments(step.Arguments, step.Command, seconds);
                foreach (var session in sessions)
                    windows.Add(UserProcess.Start(powershell, args, _execDir, Encoding.Default, _ => { }, session.Key));

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

        /// <summary>Mode pull : un fichier additionnel copié dans exec\ depuis le partage, avec l'identité du runner.</summary>
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
        /// « Garder le fichier sur le poste » : le fichier d'installation et ses fichiers additionnels sont copiés dans
        /// le dossier demandé (avec tout exec\ si l'étape copie le dossier entier). Un échec est noté sans changer le résultat.
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

        /// <summary>Mode pull : le poste copie lui-même le fichier (ou son dossier) depuis le partage.</summary>
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
                // En Système local, c'est le compte ordinateur du poste (DOMAINE\POSTE$) qui lit le partage.
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
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (step.PullFiles != null)
                foreach (var file in step.PullFiles) PullFile(file);
            switch ((step.Type ?? "").ToLowerInvariant())
            {
                case "install":
                    {
                        if (!string.IsNullOrEmpty(step.SourcePath)) Pull(step);
                        var file = Path.Combine(_execDir, step.Command ?? "");
                        if (!File.Exists(file)) throw new FileNotFoundException("Fichier d'installation introuvable sur la cible : " + step.Command);
                        if (string.IsNullOrWhiteSpace(step.CommandLine)) throw new ArgumentException("Installer : aucune ligne de commande.");
                        // Journal de Windows Installer : celui d'une étape précédente ne doit pas être repris.
                        File.Delete(Path.Combine(_execDir, InstallLog.FileName));
                        // Par cmd : la ligne (générée ou écrite à la main) est lancée telle quelle, dans exec\.
                        return new ProcessStartInfo(Path.Combine(system32, "cmd.exe"), "/s /c \"" + step.CommandLine + "\"");
                    }
                case "command":
                    return new ProcessStartInfo(Path.Combine(system32, "cmd.exe"), "/c " + step.Command);
                case "powershell":
                    {
                        var script = Path.Combine(_execDir, $"step{index + 1}.ps1");
                        // BOM UTF-8 : Windows PowerShell 5.1 lit sinon le script en ANSI.
                        File.WriteAllText(script, step.Command ?? "", new UTF8Encoding(true));
                        return PowerShell(system32, script, step.Arguments ?? "");
                    }
                case "copyfiles":
                    {
                        if (string.IsNullOrWhiteSpace(step.CopyDestination)) throw new ArgumentException("Copie de fichiers : aucun dossier de destination.");
                        string args;
                        if (string.IsNullOrEmpty(step.SourcePath))
                            args = FileCopy.Arguments(Path.Combine(_execDir, step.Command ?? ""), step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
                        else
                        {
                            // Pull : le poste lit lui-même le partage.
                            Log($"Copie depuis {step.SourcePath}");
                            var isFile = File.Exists(step.SourcePath);
                            if (!isFile && !Directory.Exists(step.SourcePath))
                                throw new FileNotFoundException("Copie de fichiers : fichier ou dossier introuvable sur le partage (ou illisible avec l'identité de l'étape) : " + step.SourcePath);
                            args = FileCopy.PullArguments(step.SourcePath, isFile, step.CopyDestination, step.CopyPatterns, step.CopySubfolders, step.CopyOverwrite);
                        }
                        // Par cmd : les %variables% de la destination prennent les valeurs de l'identité de l'étape.
                        return new ProcessStartInfo(Path.Combine(system32, "cmd.exe"), "/c robocopy.exe " + args);
                    }
                case "reboot":
                    // Le redémarrage est seulement programmé : le serveur attend ensuite que le poste revienne.
                    return new ProcessStartInfo(Path.Combine(system32, "shutdown.exe"), Shutdown.Arguments(step.ShutdownOnly, step.Seconds, step.Command));
                default:
                    throw new NotSupportedException("Type d'étape inconnu : " + step.Type);
            }
        }

        private static ProcessStartInfo PowerShell(string system32, string script, string args) =>
            new ProcessStartInfo(
                Path.Combine(system32, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" {args}".Trim());

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(_slotDir, "runner.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{message}{Environment.NewLine}");
            }
            catch { /* le log ne doit jamais faire échouer un déploiement */ }
        }
    }
}
