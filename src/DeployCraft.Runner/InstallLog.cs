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
                var header = RunnerTexts.Format("InstallLog_Header", FileName) + Environment.NewLine;
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
            [1] = "InstallLog_UpdateNotApplicable",
            [unchecked((int)0x80240017)] = "InstallLog_UpdateNotApplicable",
            [5] = "InstallLog_UpdateAccessDenied",
            [1630] = "InstallLog_UpdateBadFile",
        };

        /// <summary>Common Windows Installer codes, explained in our words (keys of <see cref="RunnerTexts"/>); the others take the Windows text.</summary>
        private static readonly Dictionary<int, string> InstallerMessages = new Dictionary<int, string>
        {
            [1601] = "InstallLog_Msi1601",
            [1602] = "InstallLog_Msi1602",
            [1603] = "InstallLog_Msi1603",
            [1605] = "InstallLog_Msi1605",
            [1614] = "InstallLog_Msi1605",
            [1618] = "InstallLog_Msi1618",
            [1619] = "InstallLog_Msi1619",
            [1620] = "InstallLog_Msi1620",
            [1622] = "InstallLog_Msi1622",
            [1624] = "InstallLog_Msi1624",
            [1632] = "InstallLog_Msi1632",
            [1633] = "InstallLog_Msi1633",
            [1638] = "InstallLog_Msi1638",
            [1639] = "InstallLog_Msi1639",
            [1642] = "InstallLog_Msi1642",
            [1704] = "InstallLog_Msi1704",
            [17031] = "InstallLog_Msi17031",
        };

        /// <summary>Explanation of a Windows Installer exit code (.msi, .msp, .msu); null for other files or codes.</summary>
        public static string ErrorMessage(string file, int code)
        {
            var ext = Path.GetExtension(file ?? "").ToLowerInvariant();
            if (ext == ".msu" && UpdateMessages.TryGetValue(code, out var update)) return RunnerTexts.Get(update);
            if (ext != ".msi" && ext != ".msp" && ext != ".msu") return null;
            return InstallerMessages.TryGetValue(code, out var message) ? RunnerTexts.Get(message) : WindowsMessage(code);
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
