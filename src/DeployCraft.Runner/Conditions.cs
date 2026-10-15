#nullable disable
using System;
using System.Linq;

namespace DeployCraft.Runner
{
    /// <summary>Ce que les conditions ont besoin de savoir du poste (remplaçable dans les tests).</summary>
    public interface ITargetFacts
    {
        /// <summary>« 10 », « 11 », « server » ou « older ».</summary>
        string WindowsFamily { get; }
        bool Is64Bit { get; }
        /// <summary>Plus haute version de PowerShell installée (7 si PowerShell 7, sinon celle de Windows PowerShell ; 0 = aucune).</summary>
        int PowerShellVersion { get; }
        bool UserLoggedOn { get; }
        bool PathExists(string path);
        bool RegistryExists(string key, string value);
    }

    /// <summary>Vérifie les conditions d'un paquet ou d'une étape sur le poste.</summary>
    public static class ConditionCheck
    {
        /// <summary>null si toutes les conditions sont remplies, sinon la raison (en clair) de la première qui ne l'est pas.</summary>
        public static string Failure(JobConditions c, ITargetFacts facts)
        {
            if (c == null) return null;

            if (c.Windows != null && c.Windows.Count > 0 && !c.Windows.Contains(facts.WindowsFamily))
                return $"Windows : {FamilyText(facts.WindowsFamily)} n'est pas dans les versions demandées ({string.Join(", ", c.Windows.Select(FamilyText))}).";

            if (c.Architecture == "x64" && !facts.Is64Bit) return "Architecture : le poste est en 32 bits (64 bits demandé).";
            if (c.Architecture == "x86" && facts.Is64Bit) return "Architecture : le poste est en 64 bits (32 bits demandé).";

            if (c.MinPowerShell is int min && facts.PowerShellVersion < min)
                return facts.PowerShellVersion == 0
                    ? $"PowerShell : absent du poste ({min} ou plus demandé)."
                    : $"PowerShell : version {facts.PowerShellVersion} sur le poste ({min} ou plus demandé).";

            if (c.LoggedOn == true && !facts.UserLoggedOn) return "Utilisateur : personne n'est connecté sur le poste.";
            if (c.LoggedOn == false && facts.UserLoggedOn) return "Utilisateur : quelqu'un est connecté sur le poste.";

            if (c.FileExists is bool fileWanted && !string.IsNullOrWhiteSpace(c.FilePath))
            {
                var path = Environment.ExpandEnvironmentVariables(c.FilePath.Trim());
                if (facts.PathExists(path) != fileWanted)
                    return fileWanted ? $"Fichier : {path} n'existe pas." : $"Fichier : {path} existe déjà.";
            }

            if (c.RegistryExists is bool keyWanted && !string.IsNullOrWhiteSpace(c.RegistryKey))
            {
                var key = c.RegistryKey.Trim();
                var what = string.IsNullOrEmpty(c.RegistryValue) ? key : $"{key} › {c.RegistryValue}";
                if (facts.RegistryExists(key, c.RegistryValue) != keyWanted)
                    return keyWanted ? $"Registre : {what} n'existe pas." : $"Registre : {what} existe déjà.";
            }
            return null;
        }

        private static string FamilyText(string family) => family switch
        {
            "10" => "Windows 10",
            "11" => "Windows 11",
            "server" => "Windows Server",
            _ => "version antérieure à Windows 10",
        };

        /// <summary>« 10.0.22631 » + type d'installation → famille. Les serveurs sont reconnus à part.</summary>
        public static string Family(int build, bool isServer, int major) =>
            isServer ? "server" : major < 10 ? "older" : build >= 22000 ? "11" : "10";
    }
}
