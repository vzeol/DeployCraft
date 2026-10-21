using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DeployCraft.Server.Data;

/// <summary>
/// Reading of XML package files (packages and package folders).
/// Each setting with no DeployCraft equivalent is reported; a step whose conditions cannot be carried
/// over exactly is imported disabled, so it never runs on more computers than intended.
/// </summary>
public static partial class XmlPackageImport
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
            throw new FormatException(Msg.Format("XmlPackageImport_UnreadableXml", ex.Message));
        }

        // A single package or folder, or a file that contains some at the first level.
        IEnumerable<XElement> items = root.Name.LocalName is "Package" or "Folder" ? [root] : root.Elements();
        if (!items.Any(e => e.Name.LocalName is "Package" or "Folder" or "Installer"))
            throw new FormatException(Msg.Get("XmlPackageImport_UnsupportedXml"));

        var report = new ImportReport { Source = ImportReport.SourceXml };
        var repo = string.IsNullOrWhiteSpace(repository) ? null : repository.Trim().TrimEnd('\\');
        ReadChildren(items, report, repo);
        if (report.Packages.Count == 0 && report.Notes.Count == 0) report.Notes.Add(Msg.Get("XmlPackageImport_NoPackage"));
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
                    report.Notes.Add(Msg.Format("XmlPackageImport_FolderNote", Text(e, "Name")));
                    ReadChildren((e.Element("Children") ?? e).Elements(), report, repo);
                    break;
                case "Installer":
                    report.Notes.Add(Msg.Format("XmlPackageImport_OldFormat", Text(e, "Name")));
                    break;
                case "Schedule":
                    report.Notes.Add(Msg.Format("XmlPackageImport_ScheduleSkipped", Text(e, "Name")));
                    break;
                case "Target" or "TargetList":
                    report.Notes.Add(Msg.Get("XmlPackageImport_TargetListSkipped"));
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
            result.Name = Msg.Get("XmlPackageImport_NoName");
            result.Skip(Msg.Get("XmlPackageImport_NoNameSkipped"));
            return result;
        }

        var def = e.Elements("PackageDefinition").FirstOrDefault() ?? new XElement("PackageDefinition");
        var p = new Package
        {
            Name = name,
            Version = NullIfEmpty(Text(e, "Version")),
            Description = NullIfEmpty(Text(e, "Description")),
            RunAs = RunAs(Text(def, "RunAs"), result.Warn, Msg.Get("XmlPackageImport_WhatPackage")),
            CopyMode = Text(def, "CopyMode") switch { "Push" => CopyMode.Push, "Pull" => CopyMode.Pull, _ => null },
            TimeoutMinutes = Bool(def, "UseCustomTimeout") && Int(def, "Timeout") is > 0 and var t ? t : null,
        };
        result.Package = p;

        p.Conditions = ReadConditions(def.Element("Conditions"), note => result.Warn(Msg.Format("XmlPackageImport_PackageConditions", note)), out var packageConditionsLost);
        if (Raw(e, "LibraryPackageId") is { Length: > 0 } lib && lib != "null")
            result.Warn(Msg.Get("XmlPackageImport_OnlineLibrary"));

        // The "before" and "after" sections do not exist in DeployCraft: the steps are put end to end, in execution order.
        var stepElements = (def.Element("Steps")?.Elements() ?? []).ToList();
        var ordered = stepElements.Where(s => Bool(s, "IsPreStep"))
            .Concat(stepElements.Where(s => !Bool(s, "IsPreStep") && !Bool(s, "IsPostStep")))
            .Concat(stepElements.Where(s => Bool(s, "IsPostStep") && !Bool(s, "IsPreStep")))
            .ToList();
        if (stepElements.Any(s => Bool(s, "IsPreStep") || Bool(s, "IsPostStep")))
            result.Warn(Msg.Get("XmlPackageImport_Sections"));

        foreach (var s in ordered)
        {
            var (step, line) = ReadStep(s, result, repo);
            result.Steps.Add(line);
            if (step == null) continue;
            if (packageConditionsLost && step.Enabled)
            {
                step.Enabled = false;
                line.Warn(Msg.Get("XmlPackageImport_DisabledPackageConditions"));
            }
            p.Steps.Add(step);
            result.StepResults[step] = line;
        }
        if (p.Steps.Count == 0 && ordered.Count > 0) result.Warn(Msg.Get("XmlPackageImport_NoStep"));
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
            ErrorMode = ErrorModeOf(Text(e, "ErrorMode")),
            RunAs = RunAs(Text(e, "RunAs"), line.Warn, Msg.Get("XmlPackageImport_WhatStep")),
        };

        switch (type)
        {
            case "Install":
                ReadInstall(e, step, line);
                break;

            case "Command":
                step.Type = StepType.Command;
                step.Command = Text(e, "Command");
                step.SuccessCodes = SuccessCodes(e);
                step.AdditionalFiles = AdditionalFiles(e);
                break;

            case "PowerShell":
                step.Type = StepType.PowerShell;
                step.Command = Text(e, "Script");
                step.SuccessCodes = SuccessCodes(e);
                if (Text(e, "CustomCommandLine").Trim().Length > 0)
                    line.Warn(Msg.Get("XmlPackageImport_CustomCommandLine"));
                step.AdditionalFiles = AdditionalFiles(e);
                break;

            case "FileCopy":
                ReadFileCopy(e, step);
                break;

            case "Sleep":
                step.Type = StepType.Pause;
                var seconds = Int(e, "Seconds") ?? 30;
                step.PauseSeconds = Math.Clamp(seconds, 1, 86400);
                if (step.PauseSeconds != seconds) line.Warn(Msg.Format("XmlPackageImport_DurationClamped", step.PauseSeconds));
                break;

            case "NestedPackage":
                step.Type = StepType.Nested;
                var target = Text(e, "TargetPackagePath").Trim() is { Length: > 0 } targetPath ? targetPath : Text(e, "TargetPackageName").Trim();
                if (target.Length == 0)
                {
                    line.Skip(Msg.Get("XmlPackageImport_NestedUnspecified"));
                    return (null, line);
                }
                package.NestedRefs[step] = target;
                if (e.Elements("UseNestedPackageConditions").Any() && !Bool(e, "UseNestedPackageConditions"))
                    line.Warn(Msg.Get("XmlPackageImport_NestedConditions"));
                break;

            case "Reboot":
                ReadReboot(e, step, line);
                break;
            case "Message":
                line.Skip(Msg.Get("XmlPackageImport_MessageStep"));
                return (null, line);
            case "Logoff":
                line.Skip(Msg.Get("XmlPackageImport_LogoffStep"));
                return (null, line);
            case "ScanStep" or "Scan":
                line.Skip(Msg.Get("XmlPackageImport_InventoryScan"));
                return (null, line);
            default:
                line.Skip(Msg.Format("XmlPackageImport_UnknownStep", (type.Length > 0 ? type : e.Name.LocalName)));
                return (null, line);
        }

        step.Conditions = ReadConditions(e.Element("Conditions"), note => line.Warn(Msg.Format("XmlPackageImport_StepConditions", note)), out var lost);
        if (lost && step.Enabled)
        {
            step.Enabled = false;
            line.Warn(Msg.Get("XmlPackageImport_DisabledStepConditions"));
        }
        ReplaceVariables(step, line, repo);
        return (step, line);
    }

    /// <summary>Additional files, one path per line (none = null).</summary>

    private static ErrorMode ErrorModeOf(string text) => text switch
    {
        "Continue" => ErrorMode.Continue,
        "StopDeploymentSuccess" => ErrorMode.StopSuccess,
        _ => ErrorMode.Stop,
    };

    private static void ReadReboot(XElement e, PackageStep step, ImportStepResult line)
    {
        step.Type = StepType.Reboot;
        step.RebootShutdownOnly = Bool(e, "ShutdownOnly");
        var delay = Int(e, "Timeout") ?? 60;
        step.RebootDelaySeconds = Math.Clamp(delay, 30, 3600);
        if (step.RebootDelaySeconds != delay) line.Warn(Msg.Format("XmlPackageImport_RebootDelayClamped", step.RebootDelaySeconds));
        var message = Text(e, "Message").Trim();
        if (message.Length > 500)
        {
            message = message[..500];
            line.Warn(Msg.Get("XmlPackageImport_MessageShortened"));
        }
        step.RebootMessage = NullIfEmpty(message);
    }

    private static void ReadInstall(XElement e, PackageStep step, ImportStepResult line)
    {
        step.Type = StepType.Install;
        step.File = Text(e, "FileName").Trim();
        step.Arguments = NullIfEmpty(Text(e, "Parameters"));
        step.IncludeDirectory = Bool(e, "IncludeDirectory");
        step.SuccessCodes = SuccessCodes(e);
        step.CustomCommandLine = NullIfEmpty(Text(e, "CustomCommandLine").Trim());
        step.KeepInstallFile = Bool(e, "LeaveInstallFile");
        if (InstallCommand.HasMsiOptions(step.File))
        {
            var extension = Path.GetExtension(step.File).ToLowerInvariant();
            step.MsiQuiet = !e.Elements("MsiQuiet").Any() || Bool(e, "MsiQuiet");
            if (Text(e, "MsiOperation").Trim() is { Length: > 0 } operation)
            {
                if (!Enum.TryParse<MsiOperation>(operation, true, out var op))
                    line.Warn(Msg.Format("XmlPackageImport_UnknownMsiOperation", operation));
                else if (extension == ".msp" && op != MsiOperation.Install)
                    line.Warn(Msg.Get("XmlPackageImport_MspOperation"));
                else if (extension == ".msu" && op == MsiOperation.Repair)
                    line.Warn(Msg.Get("XmlPackageImport_MsuRepair"));
                else
                    step.MsiOperation = op;
            }
            if (Text(e, "MsiRestart").Trim() is { Length: > 0 } restart)
            {
                if (Enum.TryParse<MsiRestart>(restart, true, out var r))
                    step.MsiRestart = r;
                else
                    line.Warn(Msg.Format("XmlPackageImport_UnknownMsiRestart", restart));
            }
        }
        step.AdditionalFiles = AdditionalFiles(e);
    }

    private static void ReadFileCopy(XElement e, PackageStep step)
    {
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
    }

    private static string? AdditionalFiles(XElement e) =>
        PackageStep.SplitLines(Text(e, "Files")) is { Count: > 0 } files ? string.Join("\n", files) : null;

    /// <summary>
    /// File conditions → DeployCraft conditions. <paramref name="lost"/>: a condition has no exact equivalent
    /// (it is then reported through <paramref name="warn"/>).
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
                        Lose(Msg.Format("XmlPackageImport_InventoryCollection", Text(e, "InventoryCollectionName")));
                    break;
                case "File":
                    ReadFile(e, c, Lose);
                    break;
                case "Registry":
                    ReadRegistry(e, c, Lose);
                    break;
                default:
                    Lose(Msg.Format("XmlPackageImport_UnknownCondition", Text(e, "TypeName")));
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
                    lose(Msg.Get("XmlPackageImport_ServerVersions"));
                if (older > 0 && OlderWorkstations.Any(v => !versions.Contains(v)))
                    lose(Msg.Get("XmlPackageImport_OlderVersions"));
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
        else if (!x86 && !x64) lose(Msg.Get("XmlPackageImport_ArmOnly"));
    }

    private static void ReadPowerShell(string value, Conditions c, Action<string> lose)
    {
        var versions = Flags(value);
        if (versions.Count == 0 || versions.Contains("All")) return;
        string[] order = ["V1", "V2", "V3", "V4", "V5"];
        var first = Array.FindIndex(order, versions.Contains);
        if (first < 0) return;
        // DeployCraft only has a minimum version: all versions from the oldest chosen one are required.
        if (order.Skip(first).Any(v => !versions.Contains(v)))
        {
            lose(Msg.Get("XmlPackageImport_PowerShellVersions"));
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
            lose(Msg.Get("XmlPackageImport_SeveralFileConditions"));
            return;
        }
        var directory = Text(e, "Directory").Trim().TrimEnd('\\');
        var file = Text(e, "Filename").Trim();
        c.File = mode == "Exists" ? PresenceCondition.Exists : PresenceCondition.NotExists;
        c.FilePath = file.Length == 0 ? directory : directory.Length == 0 ? file : $@"{directory}\{file}";
        if (Text(e, "FileVersionCompare") is { Length: > 0 } fv && fv != "NoMatch" || Text(e, "ProductVersionCompare") is { Length: > 0 } pv && pv != "NoMatch")
            lose(Msg.Get("XmlPackageImport_FileVersionCompare"));
        if (Text(e, "CompanyName").Length > 0 || Text(e, "ProductName").Length > 0 || Text(e, "Description").Length > 0)
            lose(Msg.Get("XmlPackageImport_FileProperties"));
        if (Bool(e, "IncludeSubDirectories")) lose(Msg.Get("XmlPackageImport_FileSubfolders"));
        if (Bool(e, "MatchToAlternateVariable")) lose(Msg.Get("XmlPackageImport_File32bitPath"));
    }

    private static void ReadRegistry(XElement e, Conditions c, Action<string> lose)
    {
        var mode = Text(e, "ConditionMode");
        if (mode is not ("Exists" or "DoesNotExist")) return;
        if (c.Registry != PresenceCondition.None)
        {
            lose(Msg.Get("XmlPackageImport_SeveralRegistryConditions"));
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
            lose(Msg.Get("XmlPackageImport_RegistryValueCompare"));
        if (Bool(e, "IncludeSubKeys")) lose(Msg.Get("XmlPackageImport_RegistrySubkeys"));
        if (Bool(e, "MatchToWow6432")) lose(Msg.Get("XmlPackageImport_Registry32bit"));
    }

    /// <summary>
    /// <c>$(Repository)</c> is replaced by the repository folder given at import; the other variables
    /// (DeployCraft has none) are left as they are and reported.
    /// </summary>
    private static void ReplaceVariables(PackageStep step, ImportStepResult line, string? repo)
    {
        var missingRepository = false;
        var others = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        step.File = Replace(step.File);
        step.Arguments = Replace(step.Arguments);
        step.Command = Replace(step.Command);
        step.CustomCommandLine = Replace(step.CustomCommandLine);
        step.AdditionalFiles = Replace(step.AdditionalFiles);
        step.CopySource = Replace(step.CopySource);
        step.CopyDestination = Replace(step.CopyDestination);
        step.Conditions.FilePath = Replace(step.Conditions.FilePath);
        if (missingRepository) line.Warn(Msg.Get("XmlPackageImport_RepositoryVariable"));
        if (others.Count > 0) line.Warn(Msg.Format("XmlPackageImport_OtherVariables", string.Join(", ", others)));

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
                warn(Msg.Format("XmlPackageImport_DesktopAccount", what));
                return RunAsMode.DeployUser;
            default:
                return null;
        }
    }

    private static string SuccessCodes(XElement e) =>
        Text(e, "SuccessCodes").Trim() is { Length: > 0 } codes ? codes : Defaults.SuccessCodes;

    /// <summary>Text of a child element; "" if missing.</summary>
    private static string Text(XElement e, string name) => e.Element(name)?.Value ?? "";

    /// <summary>Boolean written as <c>&lt;X value="true" /&gt;</c> (or as text); false if missing.</summary>
    private static bool Bool(XElement e, string name) => bool.TryParse(Raw(e, name), out var b) && b;

    private static int? Int(XElement e, string name) => int.TryParse(Raw(e, name), out var i) ? i : null;

    /// <summary>Value written as a <c>value</c> attribute (numbers, booleans, numerals) or as text.</summary>
    private static string Raw(XElement e, string name)
    {
        var x = e.Element(name);
        return (string?)x?.Attribute("value") ?? x?.Value ?? "";
    }

    /// <summary>Flags written as "A, B, C".</summary>
    private static HashSet<string> Flags(string value) =>
        new(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
