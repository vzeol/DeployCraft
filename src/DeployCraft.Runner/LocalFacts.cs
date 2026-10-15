using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace DeployCraft.Runner
{
    /// <summary>Le poste réel, lu au moment de vérifier les conditions.</summary>
    internal sealed class LocalFacts : ITargetFacts
    {
        public string WindowsFamily
        {
            get
            {
                using (var root = Hklm())
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build);
                    // CurrentMajorVersionNumber n'existe qu'à partir de Windows 10.
                    var major = key?.GetValue("CurrentMajorVersionNumber") is int m ? m : 6;
                    var server = !string.Equals(key?.GetValue("InstallationType") as string, "Client", StringComparison.OrdinalIgnoreCase);
                    return ConditionCheck.Family(build, server, major);
                }
            }
        }

        public bool Is64Bit => Environment.Is64BitOperatingSystem;

        public int PowerShellVersion
        {
            get
            {
                var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
                if (Directory.Exists(pwsh) && Directory.EnumerateFiles(pwsh, "pwsh.exe", SearchOption.AllDirectories).Any()) return 7;
                using (var root = Hklm())
                {
                    using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine"))
                        if (Version.TryParse(key?.GetValue("PowerShellVersion") as string, out var v)) return v.Major;
                    using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\PowerShell\1\PowerShellEngine"))
                        return key?.GetValue("PowerShellVersion") is string ? 2 : 0;
                }
            }
        }

        public bool UserLoggedOn => UserProcess.IsUserLoggedOn();

        public bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        public bool RegistryExists(string key, string value)
        {
            var sep = key.IndexOf('\\');
            var hiveName = (sep < 0 ? key : key.Substring(0, sep)).Trim().ToUpperInvariant();
            var subKey = sep < 0 ? "" : key.Substring(sep + 1).Trim('\\');
            RegistryHive hive;
            switch (hiveName)
            {
                case "HKLM": case "HKEY_LOCAL_MACHINE": hive = RegistryHive.LocalMachine; break;
                case "HKCU": case "HKEY_CURRENT_USER": hive = RegistryHive.CurrentUser; break;
                case "HKCR": case "HKEY_CLASSES_ROOT": hive = RegistryHive.ClassesRoot; break;
                case "HKU": case "HKEY_USERS": hive = RegistryHive.Users; break;
                default: throw new ArgumentException("Clé de registre invalide (elle doit commencer par HKLM, HKCU, HKCR ou HKU) : " + key);
            }
            using (var root = RegistryKey.OpenBaseKey(hive, View))
            using (var k = subKey.Length == 0 ? null : root.OpenSubKey(subKey))
            {
                var target = subKey.Length == 0 ? root : k;
                if (target == null) return false;
                return string.IsNullOrEmpty(value) || target.GetValueNames().Contains(value, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Cartes réseau actives (Ethernet, Wi-Fi) avec une adresse IPv4 : MAC et adresse de diffusion du sous-réseau.</summary>
        public static List<NetworkCard> NetworkCards()
        {
            var cards = new List<NetworkCard>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    var mac = nic.GetPhysicalAddress().GetAddressBytes();
                    if (mac.Length != 6) continue;
                    var ipv4 = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 == null) continue;

                    string broadcast = null;
                    if (ipv4.IPv4Mask != null)
                    {
                        var ip = ipv4.Address.GetAddressBytes();
                        var mask = ipv4.IPv4Mask.GetAddressBytes();
                        broadcast = string.Join(".", ip.Select((b, i) => (byte)(b | ~mask[i])));
                    }
                    cards.Add(new NetworkCard { Mac = BitConverter.ToString(mac), Broadcast = broadcast });
                }
            }
            catch (NetworkInformationException)
            {
                // Pas de réveil possible pour ce poste : sans conséquence sur le déploiement.
            }
            return cards;
        }

        private static RegistryView View => Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;

        private static RegistryKey Hklm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, View);
    }
}
