#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>Result of an "Installer" step: Windows Installer log and explanation of the common error codes.</summary>
    public static class InstallLog
    {
        /// <summary>Log requested from msiexec by the generated command line ("/log output.log").</summary>
        public const string FileName = "output.log";

        /// <summary>
        /// Appends to the step output the log left in <paramref name="execDir"/> (its end, the most useful part, if
        /// it does not all fit), then deletes it so that the next step does not pick it up.
        /// </summary>
        public static string Append(string output, string execDir, int maxChars)
        {
            var path = Path.Combine(execDir, FileName);
            try
            {
                if (!File.Exists(path)) return output;
                string log;
                // Windows Installer writes in UTF-16 (with byte order mark); otherwise the system code page.
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

        /// <summary>Explanation of a Windows Installer exit code (.msi, .msp, .msu); null for other files or codes.</summary>
        public static string ErrorMessage(string file, int code)
        {
            var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
            if (ext == ".msu" && UpdateMessages.TryGetValue(code, out var update)) return update;
            if (ext != ".msi" && ext != ".msp" && ext != ".msu") return null;
            return InstallerMessages.TryGetValue(code, out var message) ? message : WindowsMessage(code);
        }

        /// <summary>Text that Windows associates with this code (Windows Installer codes are part of it), or null if it has none.</summary>
        public static string WindowsMessage(int code)
        {
            if (code <= 0 || code > 0xFFFF) return null;
            var message = new System.ComponentModel.Win32Exception(code).Message.Trim();
            // Code unknown to Windows: its text ends with the code in hexadecimal.
            return message.Length == 0 || message.EndsWith(")") && message.Contains("0x") ? null : message;
        }
    }
}
