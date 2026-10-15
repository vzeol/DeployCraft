using System.IO;

namespace DeployCraft.Server.Data;

/// <summary>
/// Ligne de commande d'une étape « Installer », lancée dans le dossier de travail du poste (où le fichier est copié).
/// Partagée par la console (aperçu) et le service (envoyée au runner).
/// </summary>
public static class InstallCommand
{
    /// <summary>Fichier écrit par Windows Installer (.msi, .msp) ; repris dans le résultat de l'étape.</summary>
    public const string LogFile = "output.log";

    /// <summary>Extensions reconnues par l'étape « Installer ».</summary>
    public static readonly string[] Extensions = [".exe", ".msi", ".msp", ".msu", ".ps1", ".bat", ".cmd", ".vbs", ".reg"];

    /// <summary>Le fichier a des options Windows Installer (opération, redémarrage, silencieux).</summary>
    public static bool HasMsiOptions(string? file) => Extension(file) is ".msi" or ".msp" or ".msu";

    /// <summary>Ligne écrite à la main si elle existe, sinon ligne générée.</summary>
    public static string For(PackageStep step) =>
        string.IsNullOrWhiteSpace(step.CustomCommandLine) ? Generate(step) : step.CustomCommandLine.Trim();

    /// <summary>Ligne générée d'après le type de fichier, les options Windows Installer et les paramètres.</summary>
    public static string Generate(PackageStep step)
    {
        var name = Path.GetFileName(step.File ?? "");
        var file = $"\"{name}\"";
        var args = step.Arguments?.Trim() ?? "";
        var quiet = step.MsiQuiet;
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
            }, file, "ALLUSERS=1", quiet ? "/qn" : "", restart, $"/log {LogFile}", args),
            ".msp" => Join("msiexec.exe", "/p", file, quiet ? "/qn" : "", restart, $"/log {LogFile}", args),
            ".msu" => Join("wusa.exe", step.MsiOperation == MsiOperation.Uninstall ? "/uninstall" : "", file, quiet ? "/quiet" : "", restart, args),
            ".ps1" => Join("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File", file, args),
            ".vbs" => Join("cscript.exe //NoLogo //B", file, args),
            ".reg" => Join("reg.exe import", file),
            _ => Join(file, args),
        };
    }

    private static string Extension(string? file) => Path.GetExtension(file ?? "").ToLowerInvariant();

    private static string Join(params string[] parts) => string.Join(" ", parts.Where(p => p.Length > 0));
}
