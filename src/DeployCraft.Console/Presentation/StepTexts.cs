using System.IO;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Texts and layout rules of the step editor that do not depend on the window.</summary>
public static class StepTexts
{
    /// <summary>Title of a step in the package tree.</summary>
    public static string Header(PackageStep step, int index) =>
        (string.IsNullOrWhiteSpace(step.Name)
            ? $"Étape {index + 1} ({StepSummary.TypeText(step.Type)})"
            : $"{index + 1}. {step.Name}") + (step.Enabled ? "" : " (désactivée)");

    /// <summary>Un .exe sans paramètre silencieux attend un clic qui n'arrivera jamais : on rappelle les paramètres courants.</summary>
    public static string SilentHint(string? file) => Path.GetExtension(file ?? "").ToLowerInvariant() switch
    {
        ".exe" => "Paramètres silencieux courants : /S (NSIS), /VERYSILENT /NORESTART (Inno Setup), /quiet /norestart, /s /v\"/qn\" (InstallShield).",
        ".msi" or ".msp" or ".msu" => "Options de Windows Installer ci-dessous (silencieux et sans redémarrage par défaut).",
        _ => "",
    };

    public static string ErrorModeHint(PackageStep step) => step.Type == StepType.Nested
        ? "Sans effet : chaque étape du paquet imbriqué garde son propre comportement en cas d'erreur."
        : step.ErrorMode switch
        {
            ErrorMode.Continue => "L'étape est notée en échec (le poste aussi), mais les étapes suivantes s'exécutent.",
            ErrorMode.StopSuccess => "Les étapes suivantes ne s'exécutent pas et le poste est compté réussi : " +
                                     "utile quand l'échec veut dire « rien à faire » sur ce poste.",
            _ => "Les étapes suivantes ne s'exécutent pas et le poste est en échec.",
        };

    public static string TimeoutHint(StepType type, int packageTimeout) => type switch
    {
        StepType.Pause => "Sans effet sur une pause : elle dure toujours la durée choisie.",
        StepType.Nested => "Sans effet : chaque étape du paquet imbriqué garde son délai (sinon celui du paquet imbriqué).",
        StepType.Reboot => $"Vide : délai du paquet ({packageTimeout} min). Temps laissé au poste pour revenir après le redémarrage.",
        StepType.Message => $"Vide : délai du paquet ({packageTimeout} min). Attente maximale d'un clic quand la durée d'affichage est 0.",
        _ => $"Vide : délai du paquet ({packageTimeout} min). Au-delà, le processus est arrêté et l'étape échoue.",
    };

    /// <summary>Windows Installer options shown for the file: restart / quiet, operation (not for .msp), repair (not for .msu).</summary>
    public static (bool Msi, bool Operation, bool Repair) InstallOptions(string? file)
    {
        var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
        var msi = InstallCommand.HasMsiOptions(file);
        return (msi, msi && ext != ".msp", ext != ".msu");
    }
}
