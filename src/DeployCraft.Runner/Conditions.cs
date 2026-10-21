#nullable disable
using System;
using System.Linq;

namespace DeployCraft.Runner
{
    /// <summary>What the conditions need to know about the computer (replaceable in tests).</summary>
    public interface ITargetFacts
    {
        /// <summary>"10", "11", "server" or "older".</summary>
        string WindowsFamily { get; }
        bool Is64Bit { get; }
        /// <summary>Highest PowerShell version installed (7 if PowerShell 7, otherwise Windows PowerShell's; 0 = none).</summary>
        int PowerShellVersion { get; }
        bool UserLoggedOn { get; }
        bool PathExists(string path);
        bool RegistryExists(string key, string value);
    }

    /// <summary>Checks the conditions of a package or a step on the computer.</summary>
    public static class ConditionCheck
    {
        /// <summary>null if all conditions are met, otherwise the reason (in plain words) for the first one that is not.</summary>
        public static string Failure(JobConditions c, ITargetFacts facts)
        {
            if (c == null) return null;

            if (c.Windows != null && c.Windows.Count > 0 && !c.Windows.Contains(facts.WindowsFamily))
                return RunnerTexts.Format("Conditions_WindowsMismatch", FamilyText(facts.WindowsFamily), string.Join(", ", c.Windows.Select(FamilyText)));

            if (c.Architecture == "x64" && !facts.Is64Bit) return RunnerTexts.Get("Conditions_Arch32");
            if (c.Architecture == "x86" && facts.Is64Bit) return RunnerTexts.Get("Conditions_Arch64");

            if (c.MinPowerShell is int min && facts.PowerShellVersion < min)
                return facts.PowerShellVersion == 0
                    ? RunnerTexts.Format("Conditions_PowerShellMissing", min)
                    : RunnerTexts.Format("Conditions_PowerShellVersion", facts.PowerShellVersion, min);

            if (c.LoggedOn == true && !facts.UserLoggedOn) return RunnerTexts.Get("Conditions_NobodyLoggedOn");
            if (c.LoggedOn == false && facts.UserLoggedOn) return RunnerTexts.Get("Conditions_SomeoneLoggedOn");

            if (c.FileExists is bool fileWanted && !string.IsNullOrWhiteSpace(c.FilePath))
            {
                var path = Environment.ExpandEnvironmentVariables(c.FilePath.Trim());
                if (facts.PathExists(path) != fileWanted)
                    return RunnerTexts.Format(fileWanted ? "Conditions_FileMissing" : "Conditions_FileExists", path);
            }

            if (c.RegistryExists is bool keyWanted && !string.IsNullOrWhiteSpace(c.RegistryKey))
            {
                var key = c.RegistryKey.Trim();
                var what = string.IsNullOrEmpty(c.RegistryValue) ? key : $"{key} › {c.RegistryValue}";
                if (facts.RegistryExists(key, c.RegistryValue) != keyWanted)
                    return RunnerTexts.Format(keyWanted ? "Conditions_RegistryMissing" : "Conditions_RegistryExists", what);
            }
            return null;
        }

        private static string FamilyText(string family) => family switch
        {
            "10" => "Windows 10",
            "11" => "Windows 11",
            "server" => "Windows Server",
            _ => RunnerTexts.Get("Conditions_FamilyOlder"),
        };

        /// <summary>"10.0.22631" + installation type → family. Servers are recognized separately.</summary>
        public static string Family(int build, bool isServer, int major) =>
            isServer ? "server" : major < 10 ? "older" : build >= 22000 ? "11" : "10";
    }
}
