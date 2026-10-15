#nullable disable
using System;
using System.IO;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>Résultat d'une étape « Installer » : journal de Windows Installer et explication des codes d'erreur courants.</summary>
    public static class InstallLog
    {
        /// <summary>Journal demandé à msiexec par la ligne de commande générée (« /log output.log »).</summary>
        public const string FileName = "output.log";

        /// <summary>
        /// Ajoute à la sortie de l'étape le journal laissé dans <paramref name="execDir"/> (sa fin, la plus utile, si
        /// tout ne tient pas), puis le supprime pour que l'étape suivante ne le reprenne pas.
        /// </summary>
        public static string Append(string output, string execDir, int maxChars)
        {
            var path = Path.Combine(execDir, FileName);
            try
            {
                if (!File.Exists(path)) return output;
                string log;
                // Windows Installer écrit en UTF-16 (avec marque d'ordre) ; sinon page de code du système.
                using (var reader = new StreamReader(path, Encoding.Default, detectEncodingFromByteOrderMarks: true))
                    log = reader.ReadToEnd();
                File.Delete(path);
                if (log.Length > maxChars) log = "[…]" + log.Substring(log.Length - maxChars);
                var header = "--- Journal de l'installation (" + FileName + ") ---" + Environment.NewLine;
                return string.IsNullOrWhiteSpace(output) ? header + log : output + Environment.NewLine + header + log;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return output;
            }
        }

        /// <summary>Explication d'un code de sortie de Windows Installer (.msi, .msp, .msu) ; null pour les autres fichiers ou codes.</summary>
        public static string ErrorMessage(string file, int code)
        {
            var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
            if (ext == ".msu")
            {
                switch (code)
                {
                    case 1:
                    case unchecked((int)0x80240017):
                        return "Cette mise à jour ne concerne pas ce poste (déjà intégrée ou version de Windows différente).";
                    case 5: return "Accès refusé pendant l'installation de la mise à jour.";
                    case 1630: return "Fichier de mise à jour non reconnu (fichier endommagé, ou pas une mise à jour Windows).";
                }
            }
            if (ext != ".msi" && ext != ".msp" && ext != ".msu") return null;
            switch (code)
            {
                case 1601: return "Le service Windows Installer ne répond pas sur ce poste.";
                case 1602: return "Installation annulée.";
                case 1603: return "Erreur grave pendant l'installation : voir le journal ci-dessous.";
                case 1605:
                case 1614: return "Ce produit n'est pas installé sur ce poste.";
                case 1618: return "Une autre installation est déjà en cours sur ce poste : réessayez plus tard.";
                case 1619: return "Fichier d'installation introuvable ou illisible.";
                case 1620: return "Fichier d'installation invalide ou endommagé.";
                case 1622: return "Impossible d'écrire le journal de l'installation.";
                case 1624: return "Fichier de transformation (.mst) introuvable ou invalide.";
                case 1632: return "Dossier temporaire plein ou inaccessible.";
                case 1633: return "Ce fichier ne convient pas à ce processeur (32 / 64 bits).";
                case 1638: return "Une autre version de ce produit est déjà installée.";
                case 1639: return "Paramètre de ligne de commande invalide.";
                case 1642: return "Le correctif ne s'applique pas : le produit à corriger n'est pas installé (ou pas dans cette version).";
                case 1704: return "Une installation précédente de ce produit est restée inachevée.";
                case 17031: return "Un prérequis de cette installation manque sur ce poste.";
                default: return WindowsMessage(code);
            }
        }

        /// <summary>Texte que Windows associe à ce code (les codes de Windows Installer en font partie), ou null s'il n'en a pas.</summary>
        public static string WindowsMessage(int code)
        {
            if (code <= 0 || code > 0xFFFF) return null;
            var message = new System.ComponentModel.Win32Exception(code).Message.Trim();
            // Code inconnu de Windows : son texte se termine par le code en hexadécimal.
            return message.Length == 0 || message.EndsWith(")") && message.Contains("0x") ? null : message;
        }
    }
}
