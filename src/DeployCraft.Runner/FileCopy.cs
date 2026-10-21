#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace DeployCraft.Runner
{
    /// <summary>"File copy" step: robocopy arguments (shipped with Windows).</summary>
    internal static class FileCopy
    {
        /// <summary>Copies the files of <paramref name="folder"/> that match <paramref name="patterns"/> to <paramref name="destination"/>.</summary>
        public static string Arguments(string folder, string destination, IEnumerable<string> patterns, bool subfolders, bool overwrite)
        {
            var args = new List<string> { Quote(folder), Quote(destination) };
            args.AddRange((patterns ?? Enumerable.Empty<string>()).DefaultIfEmpty("*").Select(p => "\"" + p + "\""));
            if (subfolders) args.Add("/E");
            // Without overwriting: a file already present on the computer is kept, whether older, newer or different.
            if (!overwrite) args.AddRange(new[] { "/XC", "/XN", "/XO" });
            // A locked file is retried only once (robocopy otherwise waits for days); no percentage in the output.
            args.AddRange(new[] { "/R:1", "/W:1", "/NP" });
            return string.Join(" ", args);
        }

        /// <summary>
        /// Pull mode: a file source becomes its folder + its name, without subfolders (otherwise robocopy would recreate
        /// the folder tree and copy the files of the same name into it).
        /// </summary>
        public static string PullArguments(string source, bool isFile, string destination, IEnumerable<string> patterns, bool subfolders, bool overwrite)
        {
            if (!isFile) return Arguments(source, destination, patterns, subfolders, overwrite);
            var cut = source.LastIndexOf('\\');
            return Arguments(source.Substring(0, cut), destination, new[] { source.Substring(cut + 1) }, false, overwrite);
        }

        /// <summary>Quoted path: a trailing "\" would escape the quote, a drive root becomes "C:\.".</summary>
        private static string Quote(string path)
        {
            var p = (path ?? "").Trim().TrimEnd('\\');
            if (p.EndsWith(":")) p += "\\.";
            return "\"" + p + "\"";
        }
    }
}
