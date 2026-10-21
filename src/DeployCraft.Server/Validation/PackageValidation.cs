using DeployCraft.Server.Data;

namespace DeployCraft.Server.Validation;

/// <summary>Checks a package (and its options) before it is saved.</summary>
internal static class PackageValidation
{
    internal static string? Validate(Package p, Func<long, Package?> lookup)
    {
        // A client that sends null gets "no condition".
        p.Conditions ??= new();
        foreach (var step in p.Steps)
        {
            step.Conditions ??= new();
            step.ExtraConditions = null; // set only at launch, from nested packages
        }
        if (string.IsNullOrWhiteSpace(p.Name)) return Msg.Get("PackageValidation_NameRequired");
        if (p.TimeoutMinutes is < 1) return Msg.Get("PackageValidation_TimeoutMin");
        if (ValidateConditions(p.Conditions) is { } conditionError) return Msg.Format("PackageValidation_PackageConditions", conditionError);
        // Nested packages: chosen, existing, no loop nor too many levels (the saved package counts for itself).
        try
        {
            p.Flatten(id => id == p.Id ? p : lookup(id));
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
        if (p.Offline != null && ValidateOffline(p.Offline) is { } offlineError) return Msg.Format("PackageValidation_Offline", offlineError);
        for (var i = 0; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            var label = Msg.Format("PackageValidation_StepLabel", i + 1);
            switch (s.Type)
            {
                case StepType.Install when string.IsNullOrWhiteSpace(s.File):
                    return Msg.Format("PackageValidation_InstallFile", label);
                case StepType.Command or StepType.PowerShell when string.IsNullOrWhiteSpace(s.Command):
                    return Msg.Format("PackageValidation_CommandEmpty", label);
                case StepType.Pause when s.PauseSeconds is < 1 or > 86400:
                    return Msg.Format("PackageValidation_PauseRange", label);
                case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopySource):
                    return Msg.Format("PackageValidation_CopySource", label);
                case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopyDestination):
                    return Msg.Format("PackageValidation_CopyDestination", label);
                case StepType.Reboot when s.RebootDelaySeconds is < 30 or > 3600:
                    return Msg.Format("PackageValidation_RebootDelay", label);
                case StepType.Reboot when s.RebootMessage?.Length > 500:
                    return Msg.Format("PackageValidation_RebootMessageLength", label);
                case StepType.Message when string.IsNullOrWhiteSpace(s.MessageText):
                    return Msg.Format("PackageValidation_MessageEmpty", label);
                case StepType.Message when s.MessageText!.Length > 2000 || s.MessageTitle?.Length > 100:
                    return Msg.Format("PackageValidation_MessageLength", label);
                case StepType.Message when s.MessageSeconds is < 0 or > 86400:
                    return Msg.Format("PackageValidation_MessageDuration", label);
            }
            if (s.TimeoutMinutes is < 1) return Msg.Format("PackageValidation_StepTimeout", label);
            if (ValidateConditions(s.Conditions) is { } error) return Msg.Format("PackageValidation_StepConditions", label, error);
        }
        return null;
    }

    internal static string? ValidateConditions(Conditions c)
    {
        if (c.File != PresenceCondition.None && string.IsNullOrWhiteSpace(c.FilePath)) return Msg.Get("PackageValidation_FilePath");
        if (c.Registry != PresenceCondition.None)
        {
            if (string.IsNullOrWhiteSpace(c.RegistryKey)) return Msg.Get("PackageValidation_RegistryKey");
            var hive = c.RegistryKey.Trim().Split('\\')[0].ToUpperInvariant();
            if (hive is not ("HKLM" or "HKEY_LOCAL_MACHINE" or "HKCU" or "HKEY_CURRENT_USER" or "HKCR" or "HKEY_CLASSES_ROOT" or "HKU" or "HKEY_USERS"))
                return Msg.Get("PackageValidation_RegistryHive");
        }
        return null;
    }

    internal static string? ValidateOffline(OfflineSettings o)
    {
        if (o.WakeOnLan && o.WakeWaitMinutes < 1) return Msg.Get("PackageValidation_WakeWait");
        if (o.Retry && o.RetryAttempts < 2) return Msg.Get("PackageValidation_RetryAttempts");
        if (o.Retry && o.RetryIntervalMinutes < 1) return Msg.Get("PackageValidation_RetryInterval");
        return null;
    }
}
