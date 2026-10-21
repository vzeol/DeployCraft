#nullable disable
namespace DeployCraft.Runner
{
    /// <summary>"Restart" step: shutdown.exe arguments (shipped with Windows).</summary>
    internal static class Shutdown
    {
        public static string Arguments(bool shutdownOnly, int seconds, string message)
        {
            // Delay: the logged-on user is warned, and the runner has time to return its result to the server.
            var args = (shutdownOnly ? "/s" : "/r") + $" /t {(seconds < 30 ? 30 : seconds)} /d p:4:1";
            var text = (message ?? "").Replace('"', '\'').Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 500) text = text.Substring(0, 500);
            return text.Length > 0 ? args + " /c \"" + text + "\"" : args;
        }
    }
}
