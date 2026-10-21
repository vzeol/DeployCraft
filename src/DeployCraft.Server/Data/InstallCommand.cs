namespace DeployCraft.Server.Data;

/// <summary>
/// Command line of an "Installer" step, run in the computer's working folder (where the file is copied).
/// Shared by the console (preview) and the service (sent to the runner).
/// </summary>
public static class InstallCommand
{
    /// <summary>File written by Windows Installer (.msi, .msp); reused in the step's result.</summary>
    public const string LogFile = "output.log";

    /// <summary>Extensions recognized by the "Installer" step.</summary>
    public static readonly string[] Extensions = [".exe", ".msi", ".msp", ".msu", ".ps1", ".bat", ".cmd", ".vbs", ".reg"];

    /// <summary>The file has Windows Installer options (operation, restart, silent).</summary>
    public static bool HasMsiOptions(string? file) => Extension(file) is ".msi" or ".msp" or ".msu";

    /// <summary>Hand-written line if there is one, otherwise the generated line.</summary>
    public static string For(PackageStep step) =>
        string.IsNullOrWhiteSpace(step.CustomCommandLine) ? Generate(step) : step.CustomCommandLine.Trim();

    /// <summary>Line generated from the file type, the Windows Installer options and the parameters.</summary>
    public static string Generate(PackageStep step)
    {
        var name = System.IO.Path.GetFileName(step.File ?? "");
        if (name.Length == 0) return ""; // no file chosen yet
        var file = $"\"{name}\"";
        var args = step.Arguments?.Trim() ?? "";
        var quiet = step.MsiQuiet;
        // Per-machine install, unless the parameters already set ALLUSERS (library packages, imports, typed by hand).
        var allUsers = args.Contains("ALLUSERS=", StringComparison.OrdinalIgnoreCase) ? "" : "ALLUSERS=1";
        var restart = step.MsiRestart switch
        {
            MsiRestart.Never => "/norestart",
            MsiRestart.Always => "/forcerestart",
            MsiRestart.Prompt => "/promptrestart",
            _ => "",
        };
        return Extension(name) switch
        {
            ".msi" => Join("msiexec.exe", step.MsiOperation switch
            {
                MsiOperation.Repair => "/f",
                MsiOperation.Uninstall => "/x",
                _ => "/i",
            }, file, allUsers, quiet ? "/qn" : "", restart, $"/log {LogFile}", args),
            ".msp" => Join("msiexec.exe", "/p", file, quiet ? "/qn" : "", restart, $"/log {LogFile}", args),
            ".msu" => Join("wusa.exe", step.MsiOperation == MsiOperation.Uninstall ? "/uninstall" : "", file, quiet ? "/quiet" : "", restart, args),
            ".ps1" => Join("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File", file, args),
            ".vbs" => Join("cscript.exe //NoLogo //B", file, args),
            ".reg" => Join("reg.exe import", file),
            _ => Join(file, args),
        };
    }

    private static string Extension(string? file) => System.IO.Path.GetExtension(file ?? "").ToLowerInvariant();

    private static string Join(params string[] parts) => string.Join(" ", parts.Where(p => p.Length > 0));
}
