#nullable disable
using System;
using System.Collections.Generic;
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

        /// <summary>Codes of a Windows update (.msu) that mean something else than for Windows Installer.</summary>
        private static readonly Dictionary<int, string> UpdateMessages = new Dictionary<int, string>
        {
            [1] = "Cette mise à jour ne concerne pas ce poste (déjà intégrée ou version de Windows différente).",
            [unchecked((int)0x80240017)] = "Cette mise à jour ne concerne pas ce poste (déjà intégrée ou version de Windows différente).",
            [5] = "Accès refusé pendant l'installation de la mise à jour.",
            [1630] = "Fichier de mise à jour non reconnu (fichier endommagé, ou pas une mise à jour Windows).",
        };

        /// <summary>Common Windows Installer codes, explained in our words; the others take the Windows text.</summary>
        private static readonly Dictionary<int, string> InstallerMessages = new Dictionary<int, string>
        {
            [1601] = "Le service Windows Installer ne répond pas sur ce poste.",
            [1602] = "Installation annulée.",
            [1603] = "Erreur grave pendant l'installation : voir le journal ci-dessous.",
            [1605] = "Ce produit n'est pas installé sur ce poste.",
            [1614] = "Ce produit n'est pas installé sur ce poste.",
            [1618] = "Une autre installation est déjà en cours sur ce poste : réessayez plus tard.",
            [1619] = "Fichier d'installation introuvable ou illisible.",
            [1620] = "Fichier d'installation invalide ou endommagé.",
            [1622] = "Impossible d'écrire le journal de l'installation.",
            [1624] = "Fichier de transformation (.mst) introuvable ou invalide.",
            [1632] = "Dossier temporaire plein ou inaccessible.",
            [1633] = "Ce fichier ne convient pas à ce processeur (32 / 64 bits).",
            [1638] = "Une autre version de ce produit est déjà installée.",
            [1639] = "Paramètre de ligne de commande invalide.",
            [1642] = "Le correctif ne s'applique pas : le produit à corriger n'est pas installé (ou pas dans cette version).",
            [1704] = "Une installation précédente de ce produit est restée inachevée.",
            [17031] = "Un prérequis de cette installation manque sur ce poste.",
        };

        /// <summary>Explication d'un code de sortie de Windows Installer (.msi, .msp, .msu) ; null pour les autres fichiers ou codes.</summary>
        public static string ErrorMessage(string file, int code)
        {
            var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
            if (ext == ".msu" && UpdateMessages.TryGetValue(code, out var update)) return update;
            if (ext != ".msi" && ext != ".msp" && ext != ".msu") return null;
            return InstallerMessages.TryGetValue(code, out var message) ? message : WindowsMessage(code);
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
