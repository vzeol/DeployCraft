using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DeployCraft.Server.Data;

/// <summary>
/// Lecture des fichiers d'export XML de PDQ Deploy (paquets et dossiers de paquets).
/// Chaque réglage sans équivalent dans DeployCraft est signalé ; une étape dont les conditions ne peuvent pas être
/// reprises exactement est importée désactivée, pour qu'elle ne s'exécute jamais sur plus de postes que prévu.
/// </summary>
public static partial class PdqImport
{
    private static readonly string[] Workstations = ["WindowsVista", "Windows7", "Windows8", "Windows81", "Windows10", "Windows11"];
    private static readonly string[] Servers = ["Server2008", "Server2008R2", "Server2012", "Server2012R2", "Server2016", "Server2019", "Server2022", "Server2025"];
    private static readonly string[] Legacy = ["WindowsXp", "Server2003", "Server2003R2"];
    private static readonly string[] OlderWorkstations = ["WindowsVista", "Windows7", "Windows8", "Windows81"];

    public static ImportReport Read(string xml, string? repository)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Fichier XML illisible : {ex.Message}");
        }

        var code = (string?)root.Attribute("Code");
        if (root.Name == "AdminArsenal.Export" && code is not ("PDQDeploy" or "PDQDeployPro"))
            throw new FormatException($"Ce fichier XML n'est pas un export de PDQ Deploy{(code == null ? "" : $" (produit : {code})")}.");
        if (root.Name != "AdminArsenal.Export" && root.Name != "Package" && root.Name != "Folder")
            throw new FormatException("Ce fichier XML n'est pas un export de paquets de PDQ Deploy.");

        var report = new ImportReport { Source = ImportReport.SourcePdq };
        var repo = string.IsNullOrWhiteSpace(repository) ? null : repository.Trim().TrimEnd('\\');
        ReadChildren(root.Name == "AdminArsenal.Export" ? root.Elements() : [root], report, repo);
        if (report.Packages.Count == 0 && report.Notes.Count == 0) report.Notes.Add("Aucun paquet dans ce fichier.");
        return report;
    }

    private static void ReadChildren(IEnumerable<XElement> elements, ImportReport report, string? repo)
    {
        foreach (var e in elements)
        {
            switch (e.Name.LocalName)
            {
                case "Package":
                    report.Packages.Add(ReadPackage(e, repo));
                    break;
                case "Folder":
                    report.Notes.Add($"Dossier « {Text(e, "Name")} » : DeployCraft n'a pas de dossiers de paquets, ses paquets sont importés à la racine.");
                    ReadChildren((e.Element("Children") ?? e).Elements(), report, repo);
                    break;
                case "Installer":
                    report.Notes.Add($"« {Text(e, "Name")} » : paquet d'une très ancienne version de PDQ Deploy, non repris. Ré-exportez-le depuis une version récente.");
                    break;
                case "Schedule":
                    report.Notes.Add($"Planification « {Text(e, "Name")} » : non reprise (seuls les paquets sont importés).");
                    break;
                case "Target" or "TargetList":
                    report.Notes.Add("Liste de postes : non reprise (seuls les paquets sont importés).");
                    break;
            }
        }
    }

    private static ImportPackageResult ReadPackage(XElement e, string? repo)
    {
        var name = Text(e, "Name").Trim();
        var result = new ImportPackageResult { Name = name };
        var path = Text(e, "Path").Trim();
        if (path.Length > 0) result.Keys.Add(path);
        result.Keys.Add(name);
        if (name.Length == 0)
        {
            result.Name = "(sans nom)";
            result.Skip("Paquet sans nom : non repris.");
            return result;
        }

        var def = e.Elements("PackageDefinition").FirstOrDefault() ?? new XElement("PackageDefinition");
        var p = new Package
        {
            Name = name,
            Version = NullIfEmpty(Text(e, "Version")),
            Description = NullIfEmpty(Text(e, "Description")),
            RunAs = RunAs(Text(def, "RunAs"), result.Warn, "Paquet"),
            CopyMode = Text(def, "CopyMode") switch { "Push" => CopyMode.Push, "Pull" => CopyMode.Pull, _ => null },
            TimeoutMinutes = Bool(def, "UseCustomTimeout") && Int(def, "Timeout") is > 0 and var t ? t : null,
        };
        result.Package = p;

        p.Conditions = ReadConditions(def.Element("Conditions"), note => result.Warn($"Conditions du paquet : {note}"), out var packageConditionsLost);
        if (Raw(e, "LibraryPackageId") is { Length: > 0 } lib && lib != "null")
            result.Warn("Paquet de la bibliothèque de PDQ : il ne sera plus mis à jour automatiquement.");

        // Les sections « avant » et « après » n'existent pas dans DeployCraft : les étapes sont mises bout à bout, dans l'ordre d'exécution.
        var stepElements = (def.Element("Steps")?.Elements() ?? []).ToList();
        var ordered = stepElements.Where(s => Bool(s, "IsPreStep"))
            .Concat(stepElements.Where(s => !Bool(s, "IsPreStep") && !Bool(s, "IsPostStep")))
            .Concat(stepElements.Where(s => Bool(s, "IsPostStep") && !Bool(s, "IsPreStep")))
            .ToList();
        if (stepElements.Any(s => Bool(s, "IsPreStep") || Bool(s, "IsPostStep")))
            result.Warn("Étapes « avant » et « après » : mises à la suite des étapes principales (DeployCraft n'a pas de sections).");

        foreach (var s in ordered)
        {
            var (step, line) = ReadStep(s, result, repo);
            result.Steps.Add(line);
            if (step == null) continue;
            if (packageConditionsLost && step.Enabled)
            {
                step.Enabled = false;
                line.Warn("Désactivée : les conditions du paquet n'ont pas pu être reprises exactement.");
            }
            p.Steps.Add(step);
            result.StepResults[step] = line;
        }
        if (p.Steps.Count == 0 && ordered.Count > 0) result.Warn("Aucune étape reprise.");
        return result;
    }

    private static (PackageStep? Step, ImportStepResult Line) ReadStep(XElement e, ImportPackageResult package, string? repo)
    {
        var type = Text(e, "TypeName");
        var title = Text(e, "Title").Trim();
        var line = new ImportStepResult { Name = title.Length > 0 ? title : type };
        var step = new PackageStep
        {
            Name = title,
            Enabled = !e.Elements("IsEnabled").Any() || Bool(e, "IsEnabled"),
            ErrorMode = Text(e, "ErrorMode") switch
            {
                "Continue" => ErrorMode.Continue,
                "StopDeploymentSuccess" => ErrorMode.StopSuccess,
                _ => ErrorMode.Stop,
            },
            RunAs = RunAs(Text(e, "RunAs"), line.Warn, "Étape"),
        };

        switch (type)
        {
            case "Install":
                step.Type = StepType.Install;
                step.File = Text(e, "FileName").Trim();
                step.Arguments = NullIfEmpty(Text(e, "Parameters"));
                step.IncludeDirectory = Bool(e, "IncludeDirectory");
                step.SuccessCodes = SuccessCodes(e);
                if (Text(e, "CustomCommandLine").Trim().Length > 0)
                {
                    line.Skip("Ligne de commande personnalisée : pas encore disponible dans DeployCraft.");
                    return (null, line);
                }
                var isMsi = Path.GetExtension(step.File).ToLowerInvariant() is ".msi" or ".msp";
                if (isMsi && Text(e, "MsiOperation") is "Uninstall" or "Repair")
                {
                    line.Skip("Désinstallation ou réparation MSI : pas encore disponible dans DeployCraft.");
                    return (null, line);
                }
                if (isMsi && e.Elements("MsiQuiet").Any() && !Bool(e, "MsiQuiet"))
                    line.Warn("MSI non silencieux dans PDQ : DeployCraft installe toujours en silencieux.");
                if (isMsi && Text(e, "MsiRestart") is "Prompt" or "Always")
                    line.Warn("Redémarrage après l'installation MSI : DeployCraft ne redémarre jamais le poste.");
                if (Bool(e, "LeaveInstallFile"))
                    line.Warn("« Garder le fichier sur le poste » : pas disponible, le fichier est supprimé après l'étape.");
                AdditionalFiles(e, step, line);
                break;

            case "Command":
                step.Type = StepType.Command;
                step.Command = Text(e, "Command");
                step.SuccessCodes = SuccessCodes(e);
                AdditionalFiles(e, step, line);
                break;

            case "PowerShell":
                step.Type = StepType.PowerShell;
                step.Command = Text(e, "Script");
                step.SuccessCodes = SuccessCodes(e);
                if (Text(e, "CustomCommandLine").Trim().Length > 0)
                    line.Warn("Ligne de commande personnalisée ignorée : DeployCraft lance le script avec ses propres réglages.");
                AdditionalFiles(e, step, line);
                break;

            case "FileCopy":
                step.Type = StepType.CopyFiles;
                step.CopySource = Text(e, "Source").Trim();
                step.CopyDestination = Text(e, "Target").Trim();
                step.CopyOverwrite = Bool(e, "AllowOverwrite");
                step.CopyIgnoreErrors = Bool(e, "IgnoreOverwriteErrors");
                if (Text(e, "Mode") == "Folder")
                {
                    step.CopySubfolders = Bool(e, "IncludeSubFolders");
                    var patterns = Text(e, "FilePatternsText").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    step.CopyPatterns = Bool(e, "CopyAllFiles") || patterns.Length == 0 ? "*" : string.Join(";", patterns);
                }
                else
                {
                    step.CopySubfolders = false;
                    step.CopyPatterns = "*";
                }
                break;

            case "Sleep":
                step.Type = StepType.Pause;
                var seconds = Int(e, "Seconds") ?? 30;
                step.PauseSeconds = Math.Clamp(seconds, 1, 86400);
                if (step.PauseSeconds != seconds) line.Warn($"Durée ramenée à {step.PauseSeconds} s (de 1 seconde à 24 heures).");
                break;

            case "NestedPackage":
                step.Type = StepType.Nested;
                var target = Text(e, "TargetPackagePath").Trim() is { Length: > 0 } targetPath ? targetPath : Text(e, "TargetPackageName").Trim();
                if (target.Length == 0)
                {
                    line.Skip("Paquet imbriqué non précisé dans le fichier : étape non reprise.");
                    return (null, line);
                }
                package.NestedRefs[step] = target;
                if (e.Elements("UseNestedPackageConditions").Any() && !Bool(e, "UseNestedPackageConditions"))
                    line.Warn("« Ignorer les conditions du paquet imbriqué » : DeployCraft les applique toujours.");
                break;

            case "Reboot":
                line.Skip("Étape « Redémarrer » : pas encore disponible dans DeployCraft.");
                return (null, line);
            case "Message":
                line.Skip("Étape « Message » : pas encore disponible dans DeployCraft.");
                return (null, line);
            case "Logoff":
                line.Skip("Étape « Déconnexion » : pas encore disponible dans DeployCraft.");
                return (null, line);
            case "ScanStep" or "Scan":
                line.Skip("Scan d'inventaire : propre à PDQ Inventory, non repris.");
                return (null, line);
            default:
                line.Skip($"Type d'étape inconnu ({(type.Length > 0 ? type : e.Name.LocalName)}) : non repris.");
                return (null, line);
        }

        step.Conditions = ReadConditions(e.Element("Conditions"), note => line.Warn($"Conditions : {note}"), out var lost);
        if (lost && step.Enabled)
        {
            step.Enabled = false;
            line.Warn("Désactivée : ses conditions n'ont pas pu être reprises exactement. Vérifiez-les puis réactivez-la.");
        }
        ReplaceVariables(step, line, repo);
        return (step, line);
    }

    /// <summary>Fichiers additionnels : pas encore copiés par DeployCraft, l'étape risquerait d'échouer.</summary>
    private static void AdditionalFiles(XElement e, PackageStep step, ImportStepResult line)
    {
        var files = Text(e, "Files").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (files.Length == 0) return;
        line.Warn($"Fichiers additionnels non copiés ({string.Join(", ", files)}).");
        if (step.Enabled)
        {
            step.Enabled = false;
            line.Warn("Désactivée : elle a besoin des fichiers additionnels. Ajoutez une étape « Copie de fichiers » avant, puis réactivez-la.");
        }
    }

    /// <summary>
    /// Conditions de PDQ Deploy → conditions DeployCraft. <paramref name="lost"/> : une condition n'a pas d'équivalent exact
    /// (elle est alors signalée par <paramref name="warn"/>).
    /// </summary>
    private static Conditions ReadConditions(XElement? list, Action<string> warn, out bool lost)
    {
        var c = new Conditions();
        var isLost = false;
        foreach (var e in list?.Elements() ?? [])
        {
            switch (Text(e, "TypeName"))
            {
                case "OperatingSystem":
                    ReadOperatingSystem(e, c, Lose);
                    break;
                case "PowerShellVersion":
                    ReadPowerShell(Text(e, "Version"), c, Lose);
                    break;
                case "LoggedOnUser":
                    c.LoggedOn = Text(e, "IsUserLoggedOn") switch
                    {
                        "LoggedOnUser" => LoggedOnCondition.LoggedOn,
                        "NoLoggedOnUser" => LoggedOnCondition.NotLoggedOn,
                        _ => LoggedOnCondition.Any,
                    };
                    break;
                case "Collection":
                    if (Text(e, "ConditionMode") is "IsMember" or "IsNotMember")
                        Lose($"collection PDQ Inventory « {Text(e, "InventoryCollectionName")} » : propre à PDQ Inventory.");
                    break;
                case "File":
                    ReadFile(e, c, Lose);
                    break;
                case "Registry":
                    ReadRegistry(e, c, Lose);
                    break;
                default:
                    Lose($"condition inconnue ({Text(e, "TypeName")}).");
                    break;
            }
        }
        lost = isLost;
        return c;

        void Lose(string note)
        {
            isLost = true;
            warn(note);
        }
    }

    private static void ReadOperatingSystem(XElement e, Conditions c, Action<string> lose)
    {
        var versions = Flags(Text(e, "Version"));
        if (versions.Count > 0 && !versions.Contains("All"))
        {
            if (versions.Remove("AllWorkstations")) versions.UnionWith(Workstations);
            if (versions.Remove("AllServers")) versions.UnionWith(Servers);
            if (versions.Remove("Unsupported")) versions.UnionWith(Legacy);

            var all = Workstations.Concat(Servers).All(versions.Contains);
            if (!all)
            {
                if (versions.Contains("Windows10")) c.Windows |= WindowsVersions.Windows10;
                if (versions.Contains("Windows11")) c.Windows |= WindowsVersions.Windows11;
                var servers = Servers.Count(versions.Contains);
                if (servers > 0) c.Windows |= WindowsVersions.Server;
                var older = OlderWorkstations.Count(versions.Contains) + Legacy.Count(versions.Contains);
                if (older > 0) c.Windows |= WindowsVersions.Older;
                if (servers is > 0 && servers < Servers.Length)
                    lose("versions de Windows Server choisies une à une : DeployCraft ne distingue pas les versions serveur.");
                if (older > 0 && OlderWorkstations.Any(v => !versions.Contains(v)))
                    lose("anciennes versions de Windows choisies une à une : DeployCraft les regroupe (8.1 et avant).");
            }
        }

        var arch = Flags(Text(e, "Architecture"));
        arch.Remove("ArmAware");
        if (arch.Count == 0 || arch.Contains("All")) return;
        if (arch.Remove("Both")) arch.UnionWith(["Bits32", "Bits64"]);
        var x86 = arch.Contains("Bits32");
        var x64 = arch.Contains("Bits64");
        if (x64 && !x86) c.Architecture = ArchitectureCondition.X64;
        else if (x86 && !x64) c.Architecture = ArchitectureCondition.X86;
        else if (!x86 && !x64) lose("architecture ARM seule : pas de condition équivalente dans DeployCraft.");
    }

    private static void ReadPowerShell(string value, Conditions c, Action<string> lose)
    {
        var versions = Flags(value);
        if (versions.Count == 0 || versions.Contains("All")) return;
        string[] order = ["V1", "V2", "V3", "V4", "V5"];
        var first = Array.FindIndex(order, versions.Contains);
        if (first < 0) return;
        // Seule une version minimale existe dans DeployCraft : il faut toutes les versions à partir de la plus ancienne choisie.
        if (order.Skip(first).Any(v => !versions.Contains(v)))
        {
            lose("versions de PowerShell choisies une à une : DeployCraft ne gère qu'une version minimale.");
            return;
        }
        c.MinPowerShell = first switch { 2 => 3, 3 => 4, 4 => 5, _ => null };
    }

    private static void ReadFile(XElement e, Conditions c, Action<string> lose)
    {
        var mode = Text(e, "FileConditionMode");
        if (mode is not ("Exists" or "DoesNotExist")) return;
        if (c.File != PresenceCondition.None)
        {
            lose("plusieurs conditions de fichier : DeployCraft n'en garde qu'une.");
            return;
        }
        var directory = Text(e, "Directory").Trim().TrimEnd('\\');
        var file = Text(e, "Filename").Trim();
        c.File = mode == "Exists" ? PresenceCondition.Exists : PresenceCondition.NotExists;
        c.FilePath = file.Length == 0 ? directory : directory.Length == 0 ? file : $@"{directory}\{file}";
        if (Text(e, "FileVersionCompare") is { Length: > 0 } fv && fv != "NoMatch" || Text(e, "ProductVersionCompare") is { Length: > 0 } pv && pv != "NoMatch")
            lose("comparaison de version de fichier : DeployCraft vérifie seulement la présence du fichier.");
        if (Text(e, "CompanyName").Length > 0 || Text(e, "ProductName").Length > 0 || Text(e, "Description").Length > 0)
            lose("éditeur, produit ou description du fichier : DeployCraft vérifie seulement la présence du fichier.");
        if (Bool(e, "IncludeSubDirectories")) lose("recherche du fichier dans les sous-dossiers : pas disponible dans DeployCraft.");
        if (Bool(e, "MatchToAlternateVariable")) lose("chemin 32 bits de remplacement : pas disponible dans DeployCraft.");
    }

    private static void ReadRegistry(XElement e, Conditions c, Action<string> lose)
    {
        var mode = Text(e, "ConditionMode");
        if (mode is not ("Exists" or "DoesNotExist")) return;
        if (c.Registry != PresenceCondition.None)
        {
            lose("plusieurs conditions de registre : DeployCraft n'en garde qu'une.");
            return;
        }
        var hive = Text(e, "Hive") switch
        {
            "HKEY_CURRENT_USER" or "CurrentUser" => "HKCU",
            "HKEY_CLASSES_ROOT" or "ClassesRoot" => "HKCR",
            "HKEY_USERS" or "Users" => "HKU",
            "HKEY_LOCAL_MACHINE" or "LocalMachine" or "" => "HKLM",
            var other => other,
        };
        c.Registry = mode == "Exists" ? PresenceCondition.Exists : PresenceCondition.NotExists;
        c.RegistryKey = $@"{hive}\{Text(e, "Key").Trim().Trim('\\')}";
        c.RegistryValue = NullIfEmpty(Text(e, "ValueName").Trim());
        if (Text(e, "ValueCompare") is { Length: > 0 } vc && vc != "NoMatch")
            lose("comparaison de la valeur de registre : DeployCraft vérifie seulement sa présence.");
        if (Bool(e, "IncludeSubKeys")) lose("recherche dans les sous-clés : pas disponible dans DeployCraft.");
        if (Bool(e, "MatchToWow6432")) lose("vue 32 bits du registre : pas disponible dans DeployCraft.");
    }

    /// <summary>
    /// <c>$(Repository)</c> est remplacé par le dossier du dépôt indiqué à l'import ; les autres variables de PDQ
    /// (DeployCraft n'en a pas) restent telles quelles et sont signalées.
    /// </summary>
    private static void ReplaceVariables(PackageStep step, ImportStepResult line, string? repo)
    {
        var missingRepository = false;
        var others = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        step.File = Replace(step.File);
        step.Arguments = Replace(step.Arguments);
        step.Command = Replace(step.Command);
        step.CopySource = Replace(step.CopySource);
        step.CopyDestination = Replace(step.CopyDestination);
        step.Conditions.FilePath = Replace(step.Conditions.FilePath);
        if (missingRepository) line.Warn("Utilise $(Repository) : indiquez le dossier du dépôt PDQ à l'import pour le remplacer.");
        if (others.Count > 0) line.Warn($"Variables de PDQ laissées telles quelles : {string.Join(", ", others)}.");

        string? Replace(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return VariablePattern().Replace(text, m =>
            {
                if (m.Groups[1].Value.Equals("Repository", StringComparison.OrdinalIgnoreCase))
                {
                    if (repo != null) return repo;
                    missingRepository = true;
                }
                else
                {
                    others.Add(m.Value);
                }
                return m.Value;
            });
        }
    }

    [GeneratedRegex(@"\$\((\w+)\)")]
    private static partial Regex VariablePattern();

    private static RunAsMode? RunAs(string value, Action<string> warn, string what)
    {
        switch (value)
        {
            case "Admin":
                return RunAsMode.DeployUser;
            case "LocalSystem":
                return RunAsMode.LocalSystem;
            case "LoggedOnUser":
                return RunAsMode.LoggedOnUser;
            case "AdminOnDesktop":
                warn($"{what} : « compte de déploiement sur le bureau » dans PDQ ; DeployCraft utilise le compte de déploiement, sans bureau.");
                return RunAsMode.DeployUser;
            default:
                return null;
        }
    }

    private static string SuccessCodes(XElement e) =>
        Text(e, "SuccessCodes").Trim() is { Length: > 0 } codes ? codes : Defaults.SuccessCodes;

    /// <summary>Texte d'un élément enfant ; « » s'il manque.</summary>
    private static string Text(XElement e, string name) => e.Element(name)?.Value ?? "";

    /// <summary>Booléen écrit <c>&lt;X value="true" /&gt;</c> (ou en texte) ; faux s'il manque.</summary>
    private static bool Bool(XElement e, string name) => bool.TryParse(Raw(e, name), out var b) && b;

    private static int? Int(XElement e, string name) => int.TryParse(Raw(e, name), out var i) ? i : null;

    /// <summary>Valeur écrite en attribut <c>value</c> (nombres, booléens, numéros) ou en texte.</summary>
    private static string Raw(XElement e, string name)
    {
        var x = e.Element(name);
        return (string?)x?.Attribute("value") ?? x?.Value ?? "";
    }

    /// <summary>Indicateurs écrits « A, B, C ».</summary>
    private static HashSet<string> Flags(string value) =>
        new(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
