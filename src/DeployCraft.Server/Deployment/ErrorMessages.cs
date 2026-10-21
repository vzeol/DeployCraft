using System.ComponentModel;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Deployment;

/// <summary>Translates technical errors into understandable messages.</summary>
public static class ErrorMessages
{
    private static readonly Dictionary<int, string> ExitCodes = new()
    {
        [1602] = "Installation annulée par l'utilisateur.",
        [1603] = "Erreur fatale pendant l'installation MSI (voir le journal de l'installeur).",
        [1605] = "Ce produit n'est pas installé (désinstallation impossible).",
        [1618] = "Une autre installation est déjà en cours sur le poste. Réessayez plus tard.",
        [1619] = "Impossible d'ouvrir le paquet MSI (fichier absent ou corrompu).",
        [1620] = "Paquet MSI invalide.",
        [1625] = "Installation bloquée par une stratégie de groupe.",
        [1633] = "Plateforme non prise en charge (32/64 bits ?).",
        [1638] = "Une autre version de ce produit est déjà installée.",
        [1639] = "Paramètres de ligne de commande invalides.",
        [unchecked((int)0x80240017)] = "Mise à jour non applicable à ce système.",
    };

    public static string ForStep(StepOutcome step)
    {
        if (!string.IsNullOrEmpty(step.Error)) return $"Étape « {step.Name} » : {step.Error}";
        var detail = step.ExitCode is int code && ExitCodes.TryGetValue(code, out var known) ? " — " + known : "";
        return $"Étape « {step.Name} » : code retour {step.ExitCode}{detail}";
    }

    public static string ForException(Exception ex) => ex switch
    {
        DeployException d => d.Message,
        Win32Exception { NativeErrorCode: 5 } =>
            "Accès refusé au gestionnaire de services du poste : le compte de déploiement doit être administrateur.",
        Win32Exception { NativeErrorCode: 1722 or 1753 } =>
            "Le gestionnaire de services du poste ne répond pas (pare-feu ? poste éteint ?).",
        Win32Exception { NativeErrorCode: 1326 } =>
            "Identifiant ou mot de passe du compte de déploiement incorrect.",
        Win32Exception w => w.Message,
        _ => ex.Message,
    };
}
