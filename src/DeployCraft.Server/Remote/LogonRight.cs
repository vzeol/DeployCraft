using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Accorde au compte de déploiement le droit « Ouvrir une session en tant que service » sur un poste,
/// sans quoi le service temporaire ne peut pas démarrer sous ce compte (erreur 1069).
/// À appeler sous l'identité du compte de déploiement (il doit être admin du poste).
/// </summary>
internal static class LogonRight
{
    private const string SeServiceLogonRight = "SeServiceLogonRight";
    private const uint POLICY_CREATE_ACCOUNT = 0x10;
    private const uint POLICY_LOOKUP_NAMES = 0x800;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(ref LSA_UNICODE_STRING systemName, ref LSA_OBJECT_ATTRIBUTES attributes, uint access, out IntPtr policy);

    [DllImport("advapi32.dll")]
    private static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LSA_UNICODE_STRING[] rights, uint count);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr policy);

    [DllImport("advapi32.dll")]
    private static extern int LsaNtStatusToWinError(uint status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupAccountName(string? systemName, string accountName, byte[]? sid, ref uint sidSize,
        StringBuilder? domain, ref uint domainSize, out int use);

    /// <param name="computer">null pour la machine locale.</param>
    /// <param name="account">DOMAINE\utilisateur (ou POSTE\utilisateur pour un compte local).</param>
    public static void GrantServiceLogon(string? computer, string account)
    {
        var sid = LookupSid(computer, account);
        var system = ToLsa(computer == null ? "" : @"\\" + computer);
        var right = ToLsa(SeServiceLogonRight);
        try
        {
            var attributes = new LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>() };
            Check(LsaOpenPolicy(ref system, ref attributes, POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES, out var policy),
                "Ouverture de la stratégie de sécurité du poste");
            try
            {
                Check(LsaAddAccountRights(policy, sid, [right], 1), "Octroi du droit « Ouvrir une session en tant que service »");
            }
            finally
            {
                _ = LsaClose(policy); // closing a handle: nothing to do if it fails
            }
        }
        finally
        {
            Marshal.FreeHGlobal(system.Buffer);
            Marshal.FreeHGlobal(right.Buffer);
        }
    }

    private static byte[] LookupSid(string? computer, string account)
    {
        uint sidSize = 0, domainSize = 0;
        LookupAccountName(computer, account, null, ref sidSize, null, ref domainSize, out _);
        var sid = new byte[sidSize];
        var domain = new StringBuilder((int)domainSize);
        if (sidSize == 0 || !LookupAccountName(computer, account, sid, ref sidSize, domain, ref domainSize, out _))
            throw new DeployException($"Compte de déploiement « {account} » introuvable depuis {computer ?? "ce serveur"}.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return sid;
    }

    private static LSA_UNICODE_STRING ToLsa(string value) => new()
    {
        Buffer = Marshal.StringToHGlobalUni(value),
        Length = (ushort)(value.Length * 2),
        MaximumLength = (ushort)((value.Length + 1) * 2),
    };

    private static void Check(uint status, string action)
    {
        if (status == 0) return;
        var code = LsaNtStatusToWinError(status);
        throw new Win32Exception(code, $"{action} : {new Win32Exception(code).Message} (code {code})");
    }
}
