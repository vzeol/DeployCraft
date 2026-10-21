using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

// Mirror of DeployCraft.Runner/Job.cs: job.json and status.json exchanged with the runner.

public class RunnerJob
{
    /// <summary>Package conditions: if not met, no step is run.</summary>
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
    [JsonPropertyName("steps")] public List<RunnerJobStep> Steps { get; set; } = [];
    /// <summary>Number (in the package) of the job's first step, for messages.</summary>
    [JsonPropertyName("firstNumber")] public int FirstNumber { get; set; } = 1;
    /// <summary>Language of the runner's messages ("fr" or "en"): the one of the deployment's messages.</summary>
    [JsonPropertyName("language")] public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}

public class RunnerJobStep
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    /// <summary>pause: wait duration in seconds. reboot: delay before the reboot. message: display duration (0 = until clicked).</summary>
    [JsonPropertyName("seconds")] public int Seconds { get; set; }
    [JsonPropertyName("successCodes")] public List<int> SuccessCodes { get; set; } = [0];
    [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("continueOnError")] public bool ContinueOnError { get; set; }
    /// <summary>On failure: stop the package and count the computer as successful (takes precedence over continueOnError).</summary>
    [JsonPropertyName("stopSuccessOnError")] public bool StopSuccessOnError { get; set; }
    /// <summary>Disabled step: never run, marked "skipped".</summary>
    [JsonPropertyName("disabled")] public bool Disabled { get; set; }
    /// <summary>Run the step in the logged-on user's session (the runner runs as System).</summary>
    [JsonPropertyName("runAsLoggedOnUser")] public bool RunAsLoggedOnUser { get; set; }
    /// <summary>Pull mode: file (UNC) that the runner itself copies into exec before the step.</summary>
    [JsonPropertyName("sourcePath")] public string? SourcePath { get; set; }
    [JsonPropertyName("includeDirectory")] public bool IncludeDirectory { get; set; }
    /// <summary>copyfiles: folder on the computer to copy to (environment variables accepted).</summary>
    [JsonPropertyName("copyDestination")] public string? CopyDestination { get; set; }
    /// <summary>copyfiles: patterns of the files to copy ("*" = all).</summary>
    [JsonPropertyName("copyPatterns")] public List<string>? CopyPatterns { get; set; }
    [JsonPropertyName("copySubfolders")] public bool CopySubfolders { get; set; }
    /// <summary>copyfiles: replace files already present on the computer.</summary>
    [JsonPropertyName("copyOverwrite")] public bool CopyOverwrite { get; set; }
    /// <summary>reboot: shut down instead of restarting.</summary>
    [JsonPropertyName("shutdownOnly")] public bool ShutdownOnly { get; set; }
    /// <summary>message: wait for the user's click (or the end of the display).</summary>
    [JsonPropertyName("waitForUser")] public bool WaitForUser { get; set; }
    /// <summary>install: full command line, run by cmd in exec\ (null for other types).</summary>
    [JsonPropertyName("commandLine")] public string? CommandLine { get; set; }
    /// <summary>Pull mode: additional files (UNC) that the runner itself copies into exec\ before the step.</summary>
    [JsonPropertyName("pullFiles")] public List<string>? PullFiles { get; set; }
    /// <summary>install: folder on the computer where the installation file is kept after the step (null = do not keep it).</summary>
    [JsonPropertyName("keepFolder")] public string? KeepFolder { get; set; }
    /// <summary>install: names (in exec\) of the additional files, kept with the installation file.</summary>
    [JsonPropertyName("additionalFiles")] public List<string>? AdditionalFiles { get; set; }
    /// <summary>Step conditions: if not met, the step is skipped.</summary>
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
    /// <summary>Extra conditions (nested package), checked before those of the step (null = none).</summary>
    [JsonPropertyName("extraConditions")] public List<RunnerConditionGroup>? ExtraConditions { get; set; }
}

public class RunnerConditionGroup
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
}

/// <summary>Flat conditions, as strings: the runner (.NET Framework) reads them without depending on the server's enums.</summary>
public class RunnerConditions
{
    /// <summary>Accepted versions: "10", "11", "server", "older" (empty = all).</summary>
    [JsonPropertyName("windows")] public List<string> Windows { get; set; } = [];
    /// <summary>"x64", "x86" or null.</summary>
    [JsonPropertyName("architecture")] public string? Architecture { get; set; }
    [JsonPropertyName("minPowerShell")] public int? MinPowerShell { get; set; }
    /// <summary>true = someone must be logged on, false = nobody, null = don't care.</summary>
    [JsonPropertyName("loggedOn")] public bool? LoggedOn { get; set; }
    [JsonPropertyName("filePath")] public string? FilePath { get; set; }
    /// <summary>true = must exist, false = must not exist, null = no condition.</summary>
    [JsonPropertyName("fileExists")] public bool? FileExists { get; set; }
    [JsonPropertyName("registryKey")] public string? RegistryKey { get; set; }
    [JsonPropertyName("registryValue")] public string? RegistryValue { get; set; }
    [JsonPropertyName("registryExists")] public bool? RegistryExists { get; set; }

    /// <summary>null if no condition is set.</summary>
    public static RunnerConditions? From(Conditions c)
    {
        if (c.IsEmpty) return null;
        var windows = new List<string>();
        if (c.Windows.HasFlag(WindowsVersions.Windows10)) windows.Add("10");
        if (c.Windows.HasFlag(WindowsVersions.Windows11)) windows.Add("11");
        if (c.Windows.HasFlag(WindowsVersions.Server)) windows.Add("server");
        if (c.Windows.HasFlag(WindowsVersions.Older)) windows.Add("older");
        return new RunnerConditions
        {
            Windows = windows,
            Architecture = c.Architecture switch { ArchitectureCondition.X64 => "x64", ArchitectureCondition.X86 => "x86", _ => null },
            MinPowerShell = c.MinPowerShell,
            LoggedOn = c.LoggedOn switch { LoggedOnCondition.LoggedOn => true, LoggedOnCondition.NotLoggedOn => false, _ => null },
            FilePath = c.FilePath,
            FileExists = Presence(c.File),
            RegistryKey = c.RegistryKey,
            RegistryValue = string.IsNullOrWhiteSpace(c.RegistryValue) ? null : c.RegistryValue,
            RegistryExists = Presence(c.Registry),
        };
    }

    private static bool? Presence(PresenceCondition p) => p switch
    {
        PresenceCondition.Exists => true,
        PresenceCondition.NotExists => false,
        _ => null,
    };
}

public class RunnerStatus
{
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("steps")] public List<RunnerStepResult> Steps { get; set; } = [];
    /// <summary>Network adapters of the computer, kept to wake it up later (Wake-on-LAN).</summary>
    [JsonPropertyName("network")] public List<RunnerNetworkCard>? Network { get; set; }
    /// <summary>The package stops here: the following steps (other identity) must not be started.</summary>
    [JsonPropertyName("halt")] public bool Halt { get; set; }
    /// <summary>Reason given to the steps not started (null: none, as after a failure).</summary>
    [JsonPropertyName("haltReason")] public string? HaltReason { get; set; }
}

public class RunnerNetworkCard
{
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("broadcast")] public string? Broadcast { get; set; }
}

public class RunnerStepResult
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }
    [JsonPropertyName("commandLine")] public string? CommandLine { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("startedUtc")] public DateTime? StartedUtc { get; set; }
    [JsonPropertyName("endedUtc")] public DateTime? EndedUtc { get; set; }
}
