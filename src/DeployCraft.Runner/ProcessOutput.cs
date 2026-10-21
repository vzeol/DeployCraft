using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Sortie d'un programme, gardée telle quelle (octets) puis décodée d'un bloc : un outil peut écrire en page de code
    /// OEM (la plupart des outils console) ou en UTF-16 (msiexec, certains utilitaires), voire mélanger les deux.
    /// </summary>
    internal sealed class ProcessOutput
    {
        // A plain list (not a stream): nothing to dispose, and a late write after a timeout cannot fail.
        private readonly List<byte> _bytes = new List<byte>();
        private readonly int _maxBytes;

        public ProcessOutput(int maxBytes) => _maxBytes = maxBytes;

        public void Add(byte[] buffer, int count)
        {
            lock (_bytes)
            {
                var room = _maxBytes - _bytes.Count;
                if (room > 0) _bytes.AddRange(new ArraySegment<byte>(buffer, 0, count < room ? count : room));
            }
        }

        /// <summary>Lit <paramref name="stream"/> jusqu'au bout (fin du programme).</summary>
        public void ReadAll(Stream stream)
        {
            var buffer = new byte[4096];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) Add(buffer, read);
        }

        public string Text(Encoding oem)
        {
            lock (_bytes) return Decode(_bytes.ToArray(), oem);
        }

        /// <summary>
        /// Décode les passages UTF-16 (un octet nul après chaque caractère courant) en UTF-16, le reste en
        /// <paramref name="oem"/>. Un texte OEM ne contient jamais d'octet nul : pas de confusion possible.
        /// </summary>
        public static string Decode(byte[] data, Encoding oem)
        {
            var text = new StringBuilder();
            int start = 0, i = 0, n = data.Length;
            while (i < n)
            {
                // Marque d'ordre UTF-16 en tête de sortie : ignorée.
                if (i + 1 < n && data[i] == 0xFF && data[i + 1] == 0xFE)
                {
                    text.Append(oem.GetString(data, start, i - start));
                    i += 2;
                    start = i;
                    continue;
                }
                var run = Utf16Run(data, i, n);
                if (run >= 6)
                {
                    text.Append(oem.GetString(data, start, i - start));
                    text.Append(Encoding.Unicode.GetString(data, i, run));
                    i += run;
                    start = i;
                    continue;
                }
                i++;
            }
            text.Append(oem.GetString(data, start, n - start));
            return text.Replace("\0", "").ToString();
        }

        /// <summary>Longueur (en octets) du passage UTF-16 qui commence à <paramref name="i"/>.</summary>
        private static int Utf16Run(byte[] data, int i, int n)
        {
            var j = i;
            while (j + 1 < n)
            {
                if (data[j] != 0 && data[j + 1] == 0)
                {
                    j += 2;
                    continue;
                }
                // Caractère hors Latin-1 (apostrophe typographique…) au milieu d'un passage UTF-16 déjà reconnu.
                if (j - i >= 6 && data[j + 1] != 0 && j + 3 < n && data[j + 2] != 0 && data[j + 3] == 0)
                {
                    j += 2;
                    continue;
                }
                break;
            }
            return j - i;
        }
    }
}
