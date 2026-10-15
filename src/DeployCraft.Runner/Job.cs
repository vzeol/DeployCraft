#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DeployCraft.Runner
{
    // Contrat d'échange avec le serveur : job.json (consignes) et status.json (résultat).
    // Les noms JSON doivent rester alignés avec DeployCraft.Server/Remote/RunnerContract.cs.

    [DataContract]
    public class Job
    {
        /// <summary>Conditions du paquet : non remplies, aucune étape n'est exécutée.</summary>
        [DataMember(Name = "conditions")] public JobConditions Conditions { get; set; }
        [DataMember(Name = "steps")] public List<JobStep> Steps { get; set; } = new List<JobStep>();
    }

    [DataContract]
    public class JobConditions
    {
        /// <summary>« 10 », « 11 », « server », « older » (vide = toutes).</summary>
        [DataMember(Name = "windows")] public List<string> Windows { get; set; } = new List<string>();
        /// <summary>« x64 », « x86 » ou null.</summary>
        [DataMember(Name = "architecture")] public string Architecture { get; set; }
        [DataMember(Name = "minPowerShell")] public int? MinPowerShell { get; set; }
        /// <summary>true = quelqu'un doit être connecté, false = personne, null = indifférent.</summary>
        [DataMember(Name = "loggedOn")] public bool? LoggedOn { get; set; }
        [DataMember(Name = "filePath")] public string FilePath { get; set; }
        [DataMember(Name = "fileExists")] public bool? FileExists { get; set; }
        [DataMember(Name = "registryKey")] public string RegistryKey { get; set; }
        [DataMember(Name = "registryValue")] public string RegistryValue { get; set; }
        [DataMember(Name = "registryExists")] public bool? RegistryExists { get; set; }
    }

    /// <summary>Conditions héritées (paquet imbriqué), avec leur origine pour expliquer une étape ignorée.</summary>
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
        /// <summary>install | command | powershell | pause</summary>
        [DataMember(Name = "type")] public string Type { get; set; }
        /// <summary>install : nom du fichier dans exec\. command : ligne cmd. powershell : script.</summary>
        [DataMember(Name = "command")] public string Command { get; set; }
        [DataMember(Name = "arguments")] public string Arguments { get; set; }
        /// <summary>pause : durée d'attente en secondes.</summary>
        [DataMember(Name = "seconds")] public int Seconds { get; set; }
        [DataMember(Name = "successCodes")] public List<int> SuccessCodes { get; set; } = new List<int> { 0 };
        [DataMember(Name = "timeoutSeconds")] public int TimeoutSeconds { get; set; } = 3600;
        [DataMember(Name = "continueOnError")] public bool ContinueOnError { get; set; }
        /// <summary>En cas d'échec : arrêter le paquet et compter le poste réussi (prime sur continueOnError).</summary>
        [DataMember(Name = "stopSuccessOnError")] public bool StopSuccessOnError { get; set; }
        /// <summary>Étape désactivée : jamais exécutée, notée « skipped ».</summary>
        [DataMember(Name = "disabled")] public bool Disabled { get; set; }
        /// <summary>Lancer l'étape dans la session de l'utilisateur connecté (le runner tourne en Système).</summary>
        [DataMember(Name = "runAsLoggedOnUser")] public bool RunAsLoggedOnUser { get; set; }
        /// <summary>Mode pull : fichier (UNC) à copier dans exec\ avant l'étape, avec l'identité du runner.</summary>
        [DataMember(Name = "sourcePath")] public string SourcePath { get; set; }
        [DataMember(Name = "includeDirectory")] public bool IncludeDirectory { get; set; }
        /// <summary>Conditions de l'étape : non remplies, l'étape est sautée.</summary>
        [DataMember(Name = "conditions")] public JobConditions Conditions { get; set; }
        /// <summary>Conditions supplémentaires (paquet imbriqué), vérifiées avant celles de l'étape (null = aucune).</summary>
        [DataMember(Name = "extraConditions")] public List<JobConditionGroup> ExtraConditions { get; set; }
    }

    [DataContract]
    public class RunStatus
    {
        /// <summary>running | done</summary>
        [DataMember(Name = "state")] public string State { get; set; } = "running";
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "steps")] public List<StepResult> Steps { get; set; } = new List<StepResult>();
        /// <summary>Cartes réseau du poste, retenues par le serveur pour le réveiller plus tard (Wake-on-LAN).</summary>
        [DataMember(Name = "network")] public List<NetworkCard> Network { get; set; }
    }

    [DataContract]
    public class StepResult
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        /// <summary>running | success | failed | skipped | stopped (échec avec arrêt en succès)</summary>
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
        /// Écriture atomique : le serveur ne lit jamais un fichier à moitié écrit. Le remplacement échoue tant que
        /// le serveur lit l'ancien fichier par le partage : on réessaie un peu.
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

        /// <summary>Relance <paramref name="action"/> après une IOException (fichier verrouillé), au plus <paramref name="attempts"/> fois.</summary>
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
