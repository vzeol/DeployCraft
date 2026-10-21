using System.Collections.Generic;
using System.Globalization;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Messages written by the runner, in the language of the job ("fr" or "en", French by default). Kept in code
    /// rather than in resource files: the runner is copied to each computer as a single program.
    /// Both lists have the same keys (checked by the tests).
    /// </summary>
    public static class RunnerTexts
    {
        /// <summary>"fr" or "en", from job.json.</summary>
        public static string Language { get; set; } = "fr";

        public static string Get(string key)
        {
            var texts = Language == "en" ? English : French;
            return texts.TryGetValue(key, out var text) ? text : "[" + key + "]";
        }

        public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

        public static readonly Dictionary<string, string> French = new Dictionary<string, string>
        {
        };

        public static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
        };
    }
}
