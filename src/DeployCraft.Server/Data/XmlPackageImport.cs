using System.Xml;
using System.Xml.Linq;

namespace DeployCraft.Server.Data;

/// <summary>
/// Reading of XML package files (packages and package folders).
/// Each setting with no DeployCraft equivalent is reported; a step whose conditions cannot be carried
/// over exactly is imported disabled, so it never runs on more computers than intended.
/// Packages and steps here; conditions in XmlPackageImport.Conditions.cs, variables and value helpers in
/// XmlPackageImport.Values.cs.
/// </summary>
public static partial class XmlPackageImport
{
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
}
