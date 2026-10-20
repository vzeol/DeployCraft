using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Rules and texts of the "Deploy once" window that do not depend on the window.</summary>
public static class DeployEditing
{
    /// <summary>
    /// Start steps offered for each computer: the steps of the first package, nested packages replaced by their steps
    /// (as at launch). A nested package that cannot be found leaves the package as is: the launch reports it.
    /// </summary>
    public static List<StepChoice> StepChoices(Package? first, Func<long, Package?> lookup)
    {
        try
        {
            first = first?.Flatten(lookup);
        }
        catch (ArgumentException) { /* paquet imbriqué introuvable : le lancement le signalera */ }
        if (first == null) return [];
        return first.Steps.Select((s, i) => new StepChoice(i + 1, string.IsNullOrWhiteSpace(s.Name) ? $"{i + 1}" : $"{i + 1}. {s.Name}")).ToList();
    }

    public static string RunAsHint(IReadOnlyList<RunAsMode> modes, bool hasCredentials) =>
        modes.Count != 1 ? "Chaque paquet garde son propre réglage." : modes[0] switch
        {
            RunAsMode.DeployUser when !hasCredentials => "Aucun identifiant enregistré : le service tournera en Système local.",
            RunAsMode.DeployUser => "Le service temporaire tourne sous l'identifiant choisi (accès réseau avec ce compte).",
            RunAsMode.LocalSystem => "Le service temporaire tourne en Système local (accès réseau avec le compte ordinateur du poste).",
            _ => "Chaque étape s'ouvre dans la session de l'utilisateur connecté ; échec si personne n'est connecté.",
        };

    public static string CopyModeHint(IReadOnlyList<CopyMode> modes) =>
        modes.Count != 1 ? "Chaque paquet garde son propre réglage." : modes[0] == CopyMode.Push
            ? "Le serveur copie les fichiers d'installation sur chaque poste."
            : "Chaque poste copie lui-même les fichiers depuis le partage (chemins \\\\serveur\\… seulement ; sinon push).";

    public static string CountText(int packages, int targets)
    {
        var text = targets == 0 ? "aucune cible" : $"{targets} poste(s)";
        return packages > 1 ? $"{packages} paquets · {text}" : char.ToUpper(text[0]) + text[1..];
    }
}
