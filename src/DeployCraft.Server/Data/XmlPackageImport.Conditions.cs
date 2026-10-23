using System.Xml.Linq;

namespace DeployCraft.Server.Data;

/// <summary>XML import: conditions of a package or a step (Windows version, PowerShell, file, registry).</summary>
public static partial class XmlPackageImport
{
    private static readonly string[] Workstations = ["WindowsVista", "Windows7", "Windows8", "Windows81", "Windows10", "Windows11"];
    private static readonly string[] Servers = ["Server2008", "Server2008R2", "Server2012", "Server2012R2", "Server2016", "Server2019", "Server2022", "Server2025"];
    private static readonly string[] Legacy = ["WindowsXp", "Server2003", "Server2003R2"];
    private static readonly string[] OlderWorkstations = ["WindowsVista", "Windows7", "Windows8", "Windows81"];

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
}
