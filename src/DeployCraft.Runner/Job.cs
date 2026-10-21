#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DeployCraft.Runner
{
    // Exchange contract with the server: job.json (instructions) and status.json (result).
    // The JSON names must stay aligned with DeployCraft.Server/Remote/RunnerContract.cs.

    [DataContract]
    public class Job
    {
        /// <summary>Conditions of the package: if not met, no step is run.</summary>
        [DataMember(Name = "conditions")] public JobConditions Conditions { get; set; }
        [DataMember(Name = "steps")] public List<JobStep> Steps { get; set; } = new List<JobStep>();
        /// <summary>Number (in the package) of the job's first step, for messages (0 = 1).</summary>
        [DataMember(Name = "firstNumber")] public int FirstNumber { get; set; }
        /// <summary>Language of the messages ("fr" or "en"; missing = French).</summary>
        [DataMember(Name = "language")] public string Language { get; set; }
    }

    [DataContract]
    public class JobConditions
    {
        /// <summary>« 10 », « 11 », « server », « older » (vide = toutes).</summary>
        [DataMember(Name = "windows")] public List<string> Windows { get; set; } = new List<string>();
        /// <summary>"x64", "x86" or null.</summary>
        [DataMember(Name = "architecture")] public string Architecture { get; set; }
        [DataMember(Name = "minPowerShell")] public int? MinPowerShell { get; set; }
        /// <summary>true = someone must be logged on, false = nobody, null = does not matter.</summary>
        [DataMember(Name = "loggedOn")] public bool? LoggedOn { get; set; }
        [DataMember(Name = "filePath")] public string FilePath { get; set; }
        [DataMember(Name = "fileExists")] public bool? FileExists { get; set; }
        [DataMember(Name = "registryKey")] public string RegistryKey { get; set; }
        [DataMember(Name = "registryValue")] public string RegistryValue { get; set; }
        [DataMember(Name = "registryExists")] public bool? RegistryExists { get; set; }
    }

    /// <summary>Inherited conditions (nested package), with their origin to explain a skipped step.</summary>
    [DataContract]
    public class JobConditionGroup
    {
        [DataMember(Name = "label")] public string Label { get; set; }
        [DataMember(Name = "conditions")] public JobConditions Conditions { get; set; }
    }

    [DataContract]
    public class NetworkCard
    {
        [DataMember(Name = "mac")] public string Mac { get; set; }
        [DataMember(Name = "broadcast")] public string Broadcast { get; set; }
    }

    [DataContract]
    public class JobStep
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        /// <summary>install | command | powershell | pause | copyfiles | reboot | message</summary>
        [DataMember(Name = "type")] public string Type { get; set; }
        /// <summary>install: file name in exec\. command: cmd line. powershell: script. copyfiles: folder in exec\ (push). reboot and message: message text.</summary>
        [DataMember(Name = "command")] public string Command { get; set; }
        /// <summary>message: window title.</summary>
        [DataMember(Name = "arguments")] public string Arguments { get; set; }
        /// <summary>pause: wait time in seconds. reboot: delay before the restart. message: display duration (0 = until click).</summary>
        [DataMember(Name = "seconds")] public int Seconds { get; set; }
        [DataMember(Name = "successCodes")] public List<int> SuccessCodes { get; set; } = new List<int> { 0 };
        [DataMember(Name = "timeoutSeconds")] public int TimeoutSeconds { get; set; } = 3600;
        [DataMember(Name = "continueOnError")] public bool ContinueOnError { get; set; }
        /// <summary>On failure: stop the package and count the computer as succeeded (takes precedence over continueOnError).</summary>
        [DataMember(Name = "stopSuccessOnError")] public bool StopSuccessOnError { get; set; }
        /// <summary>Disabled step: never run, recorded as "skipped".</summary>
        [DataMember(Name = "disabled")] public bool Disabled { get; set; }
        /// <summary>Run the step in the logged-on user's session (the runner runs as System).</summary>
        [DataMember(Name = "runAsLoggedOnUser")] public bool RunAsLoggedOnUser { get; set; }
        /// <summary>Pull mode: file (UNC) to copy into exec\ before the step, with the runner's identity.</summary>
        [DataMember(Name = "sourcePath")] public string SourcePath { get; set; }
        [DataMember(Name = "includeDirectory")] public bool IncludeDirectory { get; set; }
        /// <summary>copyfiles: folder on the computer to copy to (environment variables accepted).</summary>
        [DataMember(Name = "copyDestination")] public string CopyDestination { get; set; }
        /// <summary>copyfiles: patterns of the files to copy ("*" = all).</summary>
        [DataMember(Name = "copyPatterns")] public List<string> CopyPatterns { get; set; }
        [DataMember(Name = "copySubfolders")] public bool CopySubfolders { get; set; }
        /// <summary>copyfiles: replace files already present on the computer.</summary>
        [DataMember(Name = "copyOverwrite")] public bool CopyOverwrite { get; set; }
        /// <summary>reboot: shut down instead of restarting.</summary>
        [DataMember(Name = "shutdownOnly")] public bool ShutdownOnly { get; set; }
        /// <summary>message: wait for the user's click (or for the display to end).</summary>
        [DataMember(Name = "waitForUser")] public bool WaitForUser { get; set; }
        /// <summary>install: full command line, launched by cmd in exec\.</summary>
        [DataMember(Name = "commandLine")] public string CommandLine { get; set; }
        /// <summary>Pull mode: additional files (UNC) to copy into exec\ before the step.</summary>
        [DataMember(Name = "pullFiles")] public List<string> PullFiles { get; set; }
        /// <summary>install: folder where to keep the installation file after the step (environment variables accepted).</summary>
        [DataMember(Name = "keepFolder")] public string KeepFolder { get; set; }
        /// <summary>install: names (in exec\) of the additional files, kept with the installation file.</summary>
        [DataMember(Name = "additionalFiles")] public List<string> AdditionalFiles { get; set; }
        /// <summary>Conditions of the step: if not met, the step is skipped.</summary>
        [DataMember(Name = "conditions")] public JobConditions Conditions { get; set; }
        /// <summary>Additional conditions (nested package), checked before the step's own (null = none).</summary>
        [DataMember(Name = "extraConditions")] public List<JobConditionGroup> ExtraConditions { get; set; }
    }

    [DataContract]
    public class RunStatus
    {
        /// <summary>running | done</summary>
        [DataMember(Name = "state")] public string State { get; set; } = "running";
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "steps")] public List<StepResult> Steps { get; set; } = new List<StepResult>();
        /// <summary>Network cards of the computer, remembered by the server to wake it up later (Wake-on-LAN).</summary>
        [DataMember(Name = "network")] public List<NetworkCard> Network { get; set; }
        /// <summary>The package stops here: the following steps (other identity) must not be launched.</summary>
        [DataMember(Name = "halt")] public bool Halt { get; set; }
        /// <summary>Reason given to the steps not launched (null: none, as after a failure).</summary>
        [DataMember(Name = "haltReason")] public string HaltReason { get; set; }
    }

    [DataContract]
    public class StepResult
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        /// <summary>running | success | failed | skipped | stopped (failure with stop counted as success)</summary>
        [DataMember(Name = "state")] public string State { get; set; }
        [DataMember(Name = "exitCode")] public int? ExitCode { get; set; }
        [DataMember(Name = "commandLine")] public string CommandLine { get; set; }
        [DataMember(Name = "output")] public string Output { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "startedUtc")] public string StartedUtc { get; set; }
        [DataMember(Name = "endedUtc")] public string EndedUtc { get; set; }
    }

    internal static class Json
    {
        public static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        /// <summary>
        /// Atomic write: the server never reads a half-written file. The replacement fails while
        /// the server reads the old file through the share: we retry a few times.
        /// </summary>
        public static void Write<T>(string path, T value)
        {
            var tmp = path + ".tmp";
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                File.WriteAllText(tmp, Encoding.UTF8.GetString(stream.ToArray()), new UTF8Encoding(false));
            }
            Retry(() =>
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }, 10, System.Threading.Thread.Sleep);
        }

        /// <summary>Re-runs <paramref name="action"/> after an IOException (locked file), at most <paramref name="attempts"/> times.</summary>
        public static void Retry(Action action, int attempts, Action<int> sleep)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (attempt < attempts)
                {
                    sleep(150);
                }
            }
        }

        public static string Now() => DateTime.UtcNow.ToString("o");
    }
}
