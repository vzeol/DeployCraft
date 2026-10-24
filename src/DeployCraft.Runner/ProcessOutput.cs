using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Output kept as bytes then decoded in one go: UTF-8 for PowerShell steps, OEM for other programs,
    /// with UTF-16 passages from msiexec and some utilities recognized in either case.
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

        /// <summary>Reads <paramref name="stream"/> to the end (end of the program).</summary>
        public void ReadAll(Stream stream)
        {
            var buffer = new byte[4096];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) Add(buffer, read);
        }

        public static Encoding EncodingFor(string stepType, Encoding oem) =>
            string.Equals(stepType, "powershell", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8 : oem;

        public string Text(Encoding encoding)
        {
            lock (_bytes) return Decode(_bytes.ToArray(), encoding);
        }

        /// <summary>
        /// Decodes UTF-16 passages (a null byte after each common character) as UTF-16, the rest as
        /// <paramref name="encoding"/> (the step's output encoding).
        /// </summary>
        public static string Decode(byte[] data, Encoding encoding)
        {
            var text = new StringBuilder();
            int start = 0, i = 0, n = data.Length;
            while (i < n)
            {
                // UTF-16 byte order mark at the start of the output: ignored.
                if (i + 1 < n && data[i] == 0xFF && data[i + 1] == 0xFE)
                {
                    text.Append(encoding.GetString(data, start, i - start));
                    i += 2;
                    start = i;
                    continue;
                }
                var run = Utf16Run(data, i, n);
                if (run >= 6)
                {
                    text.Append(encoding.GetString(data, start, i - start));
                    text.Append(Encoding.Unicode.GetString(data, i, run));
                    i += run;
                    start = i;
                    continue;
                }
                i++;
            }
            text.Append(encoding.GetString(data, start, n - start));
            return text.Replace("\0", "").ToString();
        }

        /// <summary>Length (in bytes) of the UTF-16 passage starting at <paramref name="i"/>.</summary>
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
                // Character outside Latin-1 (typographic apostrophe...) in the middle of an already recognized UTF-16 passage.
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
