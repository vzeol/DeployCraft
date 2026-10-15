using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

// Miroir de DeployCraft.Runner/Job.cs : job.json et status.json échangés avec le runner.

public class RunnerJob
{
    /// <summary>Conditions du paquet : non remplies, aucune étape n'est exécutée.</summary>
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
    [JsonPropertyName("steps")] public List<RunnerJobStep> Steps { get; set; } = [];
}

public class RunnerJobStep
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    /// <summary>pause : durée d'attente en secondes.</summary>
    [JsonPropertyName("seconds")] public int Seconds { get; set; }
    [JsonPropertyName("successCodes")] public List<int> SuccessCodes { get; set; } = [0];
    [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("continueOnError")] public bool ContinueOnError { get; set; }
    /// <summary>En cas d'échec : arrêter le paquet et compter le poste réussi (prime sur continueOnError).</summary>
    [JsonPropertyName("stopSuccessOnError")] public bool StopSuccessOnError { get; set; }
    /// <summary>Étape désactivée : jamais exécutée, notée « skipped ».</summary>
    [JsonPropertyName("disabled")] public bool Disabled { get; set; }
    /// <summary>Lancer l'étape dans la session de l'utilisateur connecté (le runner tourne en Système).</summary>
    [JsonPropertyName("runAsLoggedOnUser")] public bool RunAsLoggedOnUser { get; set; }
    /// <summary>Mode pull : fichier (UNC) que le runner copie lui-même dans exec avant l'étape.</summary>
    [JsonPropertyName("sourcePath")] public string? SourcePath { get; set; }
    [JsonPropertyName("includeDirectory")] public bool IncludeDirectory { get; set; }
    /// <summary>Conditions de l'étape : non remplies, l'étape est sautée.</summary>
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
    /// <summary>Conditions supplémentaires (paquet imbriqué), vérifiées avant celles de l'étape (null = aucune).</summary>
    [JsonPropertyName("extraConditions")] public List<RunnerConditionGroup>? ExtraConditions { get; set; }
}

public class RunnerConditionGroup
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("conditions")] public RunnerConditions? Conditions { get; set; }
}

/// <summary>Conditions à plat, en chaînes : le runner (.NET Framework) les lit sans dépendre des énumérations du serveur.</summary>
public class RunnerConditions
{
    /// <summary>Versions acceptées : « 10 », « 11 », « server », « older » (vide = toutes).</summary>
    [JsonPropertyName("windows")] public List<string> Windows { get; set; } = [];
    /// <summary>« x64 », « x86 » ou null.</summary>
    [JsonPropertyName("architecture")] public string? Architecture { get; set; }
    [JsonPropertyName("minPowerShell")] public int? MinPowerShell { get; set; }
    /// <summary>true = quelqu'un doit être connecté, false = personne, null = indifférent.</summary>
    [JsonPropertyName("loggedOn")] public bool? LoggedOn { get; set; }
    [JsonPropertyName("filePath")] public string? FilePath { get; set; }
    /// <summary>true = doit exister, false = ne doit pas exister, null = pas de condition.</summary>
    [JsonPropertyName("fileExists")] public bool? FileExists { get; set; }
    [JsonPropertyName("registryKey")] public string? RegistryKey { get; set; }
    [JsonPropertyName("registryValue")] public string? RegistryValue { get; set; }
    [JsonPropertyName("registryExists")] public bool? RegistryExists { get; set; }

    /// <summary>null si aucune condition n'est posée.</summary>
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
    /// <summary>Cartes réseau du poste, retenues pour le réveiller plus tard (Wake-on-LAN).</summary>
    [JsonPropertyName("network")] public List<RunnerNetworkCard>? Network { get; set; }
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
