namespace DeployCraft.Server.Data;

/// <summary>Texts of the Details tab of a package page: location, conditions, offline, packages that use it.</summary>
public static class PackageDetails
{
    /// <summary>Path of the folder in the tree ("Paquets › Bureautique › PDF"), "Paquets" at the root.</summary>
    public static string FolderPath(long? folderId, IEnumerable<PackageFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        var names = new List<string>();
        // Depth limit: a hand-edited database must not make it loop forever.
        while (folderId is { } id && byId.TryGetValue(id, out var folder) && names.Count < 50)
        {
            names.Insert(0, folder.Name);
            folderId = folder.ParentId;
        }
        return string.Join(" › ", names.Prepend(Msg.Get("PackageDetails_RootFolder")));
    }

    /// <summary>Conditions on one readable line, or "Aucune".</summary>
    public static string ConditionsText(Conditions c)
    {
        if (c.IsEmpty) return Msg.Get("PackageDetails_NoConditions");
        var parts = new List<string>();
        if (c.Windows != WindowsVersions.None)
        {
            var versions = new List<string>();
            if (c.Windows.HasFlag(WindowsVersions.Windows11)) versions.Add("Windows 11");
            if (c.Windows.HasFlag(WindowsVersions.Windows10)) versions.Add("Windows 10");
            if (c.Windows.HasFlag(WindowsVersions.Server)) versions.Add("Windows Server");
            if (c.Windows.HasFlag(WindowsVersions.Older)) versions.Add(Msg.Get("PackageDetails_WindowsOlder"));
            parts.Add(string.Join(Msg.Get("PackageDetails_Or"), versions));
        }
        if (c.Architecture != ArchitectureCondition.Any) parts.Add(c.Architecture == ArchitectureCondition.X64 ? Msg.Get("PackageDetails_Bits64") : Msg.Get("PackageDetails_Bits32"));
        if (c.MinPowerShell is { } ps) parts.Add(Msg.Format("PackageDetails_PowerShellMin", ps));
        if (c.LoggedOn != LoggedOnCondition.Any) parts.Add(c.LoggedOn == LoggedOnCondition.LoggedOn ? Msg.Get("PackageDetails_UserLoggedOn") : Msg.Get("PackageDetails_NobodyLoggedOn"));
        if (c.File != PresenceCondition.None) parts.Add(Msg.Format("PackageDetails_FileCondition", c.FilePath, Presence(c.File)));
        if (c.Registry != PresenceCondition.None)
        {
            var key = string.IsNullOrWhiteSpace(c.RegistryValue) ? c.RegistryKey : $@"{c.RegistryKey}\{c.RegistryValue}";
            parts.Add(Msg.Format("PackageDetails_RegistryCondition", key, Presence(c.Registry)));
        }
        return string.Join(Msg.Get("PackageDetails_ConditionSeparator"), parts);
    }

    private static string Presence(PresenceCondition p) => p == PresenceCondition.Exists ? Msg.Get("PackageDetails_Present") : Msg.Get("PackageDetails_Absent");

    /// <summary>Offline settings on one line; <paramref name="fromDefaults"/>: those from Preferences.</summary>
    public static string OfflineText(OfflineSettings o, bool fromDefaults)
    {
        var parts = new List<string>
        {
            o.WakeOnLan ? Msg.Format("PackageDetails_OfflineWake", o.WakeWaitMinutes) : Msg.Get("PackageDetails_OfflineNoWake"),
            o.Retry ? Msg.Format("PackageDetails_OfflineRetry", o.RetryAttempts, o.RetryIntervalMinutes) : Msg.Get("PackageDetails_OfflineNoRetry"),
        };
        var text = string.Join(", ", parts);
        return fromDefaults ? Msg.Format("PackageDetails_OfflineFromDefaults", text) : char.ToUpper(text[0]) + text[1..];
    }

    /// <summary>Packages that contain this one as a nested package, by name.</summary>
    public static string UsedByText(long id, IEnumerable<Package> packages)
    {
        var users = Package.UsersOf(packages, id).Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        return users.Count == 0 ? Msg.Get("PackageDetails_NoPackage") : string.Join(", ", users);
    }
}
