#nullable disable
namespace DeployCraft.Runner
{
    /// <summary>Étape « redémarrer » : arguments de shutdown.exe (fourni avec Windows).</summary>
    internal static class Shutdown
    {
        public static string Arguments(bool shutdownOnly, int seconds, string message)
        {
            // Délai : l'utilisateur connecté est prévenu, et le runner a le temps de rendre son résultat au serveur.
            var args = (shutdownOnly ? "/s" : "/r") + $" /t {(seconds < 30 ? 30 : seconds)} /d p:4:1";
            var text = (message ?? "").Replace('"', '\'').Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 500) text = text.Substring(0, 500);
            return text.Length > 0 ? args + " /c \"" + text + "\"" : args;
        }
    }
}
