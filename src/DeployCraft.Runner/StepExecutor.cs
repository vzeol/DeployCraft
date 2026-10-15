using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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

                var facts = new LocalFacts();
                // Conditions du paquet non remplies : toutes les étapes sont sautées, sans échec.
                var packageSkip = Unmet(job.Conditions, facts, out var packageError);
                if (packageSkip != null) Log("Conditions du paquet non remplies : " + packageSkip);

                var stop = false;
                for (var i = 0; i < job.Steps.Count; i++)
                {
                    var step = job.Steps[i];
                    var result = new StepResult { Name = step.Name, State = "running", StartedUtc = Json.Now() };
                    status.Steps.Add(result);

                    if (stop || _aborted)
                    {
                        result.State = "skipped";
                        result.EndedUtc = Json.Now();
                        continue;
                    }

                    string stepSkip = null, stepError = null;
                    if (packageSkip != null)
                    {
                        result.State = "skipped";
                        result.Error = "Conditions du paquet non remplies. " + packageSkip;
                    }
                    else if (packageError != null)
                    {
                        result.State = "failed";
                        result.Error = packageError;
                        stop = true;
                    }
                    else if ((stepSkip = Unmet(step.Conditions, facts, out stepError)) != null)
                    {
                        result.State = "skipped";
                        result.Error = "Condition non remplie. " + stepSkip;
                    }
                    else if (stepError != null)
                    {
                        result.State = "failed";
                        result.Error = stepError;
                        if (!step.ContinueOnError) stop = true;
                    }
                    if (result.State != "running")
                    {
                        result.EndedUtc = Json.Now();
                        Log($"Étape {i + 1} « {step.Name} » : {result.State} ({result.Error})");
                        Json.Write(_statusPath, status);
                        continue;
                    }

                    Json.Write(_statusPath, status);
                    RunStep(i, step, result);
                    result.EndedUtc = Json.Now();
                    Log($"Étape {i + 1} « {step.Name} » : {result.State} (code {result.ExitCode})");

                    if (result.State == "failed" && !step.ContinueOnError) stop = true;
                    Json.Write(_statusPath, status);
                }
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

        /// <summary>
        /// Raison pour laquelle les conditions ne sont pas remplies (null = remplies).
        /// Une condition impossible à vérifier (clé de registre mal écrite…) est une erreur, rendue dans <paramref name="error"/>.
        /// </summary>
        private static string Unmet(JobConditions conditions, ITargetFacts facts, out string error)
        {
            error = null;
            try
            {
                return ConditionCheck.Failure(conditions, facts);
            }
            catch (Exception ex)
            {
                error = "Vérification des conditions impossible : " + ex.Message;
                return null;
            }
        }

        private void RunStep(int index, JobStep step, StepResult result)
        {
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

            result.CommandLine = $"{psi.FileName} {psi.Arguments}".Trim();
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
            }
        }

        private static void TimedOut(int pid, JobStep step, StepResult result)
        {
            KillTree(pid);
            result.State = "failed";
            result.Error = $"Délai dépassé ({step.TimeoutSeconds / 60} min). Paramètres silencieux manquants ?";
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
            switch ((step.Type ?? "").ToLowerInvariant())
            {
                case "install":
                    {
                        if (!string.IsNullOrEmpty(step.SourcePath)) Pull(step);
                        var file = Path.Combine(_execDir, step.Command ?? "");
                        if (!File.Exists(file)) throw new FileNotFoundException("Fichier d'installation introuvable sur la cible : " + step.Command);
                        var args = step.Arguments ?? "";
                        switch (Path.GetExtension(file).ToLowerInvariant())
                        {
                            case ".msi":
                                return new ProcessStartInfo(Path.Combine(system32, "msiexec.exe"), $"/i \"{file}\" /qn /norestart {args}".Trim());
                            case ".msp":
                                return new ProcessStartInfo(Path.Combine(system32, "msiexec.exe"), $"/p \"{file}\" /qn /norestart {args}".Trim());
                            case ".msu":
                                return new ProcessStartInfo(Path.Combine(system32, "wusa.exe"), $"\"{file}\" /quiet /norestart {args}".Trim());
                            case ".ps1":
                                return PowerShell(system32, file, args);
                            case ".bat":
                            case ".cmd":
                                return new ProcessStartInfo(Path.Combine(system32, "cmd.exe"), $"/c \"\"{file}\" {args}\"");
                            default:
                                return new ProcessStartInfo(file, args);
                        }
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
