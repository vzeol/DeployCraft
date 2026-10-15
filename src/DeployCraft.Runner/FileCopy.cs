#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace DeployCraft.Runner
{
    /// <summary>Étape « copie de fichiers » : arguments de robocopy (fourni avec Windows).</summary>
    internal static class FileCopy
    {
        /// <summary>Copie des fichiers de <paramref name="folder"/> qui suivent <paramref name="patterns"/> vers <paramref name="destination"/>.</summary>
        public static string Arguments(string folder, string destination, IEnumerable<string> patterns, bool subfolders, bool overwrite)
        {
            var args = new List<string> { Quote(folder), Quote(destination) };
            args.AddRange((patterns ?? Enumerable.Empty<string>()).DefaultIfEmpty("*").Select(p => "\"" + p + "\""));
            if (subfolders) args.Add("/E");
            // Sans écraser : un fichier déjà présent sur le poste est gardé, qu'il soit plus ancien, plus récent ou différent.
            if (!overwrite) args.AddRange(new[] { "/XC", "/XN", "/XO" });
            // Un fichier bloqué n'est réessayé qu'une fois (robocopy attend sinon des jours) ; pas de pourcentage dans la sortie.
            args.AddRange(new[] { "/R:1", "/W:1", "/NP" });
            return string.Join(" ", args);
        }

        /// <summary>
        /// Mode pull : une source fichier devient son dossier + son nom, sans sous-dossiers (sinon robocopy recréerait
        /// l'arborescence du dossier et y copierait les fichiers du même nom).
        /// </summary>
        public static string PullArguments(string source, bool isFile, string destination, IEnumerable<string> patterns, bool subfolders, bool overwrite)
        {
            if (!isFile) return Arguments(source, destination, patterns, subfolders, overwrite);
            var cut = source.LastIndexOf('\\');
            return Arguments(source.Substring(0, cut), destination, new[] { source.Substring(cut + 1) }, false, overwrite);
        }

        /// <summary>Chemin entre guillemets : un « \ » final échapperait le guillemet, la racine d'un disque devient « C:\. ».</summary>
        private static string Quote(string path)
        {
            var p = (path ?? "").Trim().TrimEnd('\\');
            if (p.EndsWith(":")) p += "\\.";
            return "\"" + p + "\"";
        }
    }
}
