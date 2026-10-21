using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using DeployCraft.Server.Remote;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace DeployCraft.Server.Security;

/// <summary>State of the DeployCraft service, returned to the Options › Service en arrière-plan window.</summary>
public record ServiceInfo(bool IsService, string Account, bool LocalSystem, bool Elevated, DateTime StartedUtc);

/// <summary>
/// The DeployCraft service itself: restart, and the Windows account it runs under (local System, or a server
/// administrator account). Only the real service can act on itself: a test instance started by hand
/// never touches the installed service.
/// </summary>
public class ServiceAccount(ILogger<ServiceAccount> log)
{
    public const string ServiceName = "DeployCraft";
    /// <summary>Value to choose to go back to the factory account.</summary>
    public const string LocalSystem = "LocalSystem";

    public static bool IsService => WindowsServiceHelpers.IsWindowsService();

    public static ServiceInfo Info()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new ServiceInfo(IsService, identity.Name, identity.IsSystem, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            Process.GetCurrentProcess().StartTime.ToUniversalTime());
    }

    /// <summary>
    /// Restarts the service in two seconds (long enough to answer the console): a separate command stops it then
    /// starts it again. Error if this is not the installed service.
    /// </summary>
    public string? Restart()
    {
        if (!IsService) return "Ce serveur ne tourne pas en service Windows (instance lancée à la main) : redémarrez-le vous-même.";
        log.LogInformation("Redémarrage du service demandé depuis la console");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & net stop {ServiceName} & net start {ServiceName}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        return null;
    }

    /// <summary>
    /// Entered account → (domain, user) for logon and name for the service.
    /// "DOMAIN\name", ".\name" (local account) or "name@domain"; readable error otherwise.
    /// </summary>
    public static (string? Domain, string User, string ServiceName, string? Error) Parse(string? account)
    {
        var text = (account ?? "").Trim();
        if (text.Length == 0) return (null, "", "", "Indiquez un compte (DOMAINE\\nom, ou .\\nom pour un compte local).");
        if (text.Contains('@') && !text.Contains('\\')) return (null, text, text, null);
        var parts = text.Split('\\');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return (null, "", "", "Écrivez le compte sous la forme DOMAINE\\nom (ou .\\nom pour un compte local du serveur).");
        var domain = parts[0] == "." ? Environment.MachineName : parts[0];
        return (domain, parts[1], $"{parts[0]}\\{parts[1]}", null);
    }

    /// <summary>
    /// Runs the service under another account, then restarts it. The account must exist, have this password and
    /// be a server administrator (data folder, deployments). <see cref="LocalSystem"/>: back to the factory account.
    /// </summary>
    public string? Change(string? account, string? password)
    {
        if (!IsService) return "Ce serveur ne tourne pas en service Windows (instance lancée à la main) : son compte ne se change pas ici.";

        string serviceName;
        if (string.Equals(account?.Trim(), LocalSystem, StringComparison.OrdinalIgnoreCase))
        {
            serviceName = LocalSystem;
            password = "";
        }
        else
        {
            var (domain, user, name, error) = Parse(account);
            if (error != null) return error;
            if (string.IsNullOrEmpty(password)) return "Indiquez le mot de passe du compte.";
            if (!Win32.LogonUser(user, domain, password, Win32.LOGON32_LOGON_NETWORK, Win32.LOGON32_PROVIDER_DEFAULT, out var check))
                return $"Compte ou mot de passe refusé : {Win32.LastError("ouverture de session").Message}";
            check.Dispose();
            // Check before any change: a refused account gets no right on the server. The "scheduled task"
            // logon type gives the full token (without User Account Control filtering)
            // and is allowed by default only to administrators.
            if (!Win32.LogonUser(user, domain, password, Win32.LOGON32_LOGON_BATCH, Win32.LOGON32_PROVIDER_DEFAULT, out var token))
                return $"{name} n'est pas administrateur de ce serveur : le service ne pourrait ni lire ses données ni déployer.";
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    return $"{name} n'est pas administrateur de ce serveur : le service ne pourrait ni lire ses données ni déployer.";
            try
            {
                GrantServiceLogon(new NTAccount(domain ?? "", user));
            }
            catch (Exception ex) when (ex is IdentityNotMappedException or Win32Exception)
            {
                return $"Droit « ouvrir une session en tant que service » non accordé : {ex.Message}";
            }
            serviceName = name;
        }

        var scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return Win32.LastError("Gestionnaire de services").Message;
        try
        {
            var service = Win32.OpenService(scm, ServiceName, Win32.SERVICE_CHANGE_CONFIG);
            if (service == IntPtr.Zero) return Win32.LastError("Service DeployCraft").Message;
            try
            {
                if (!Win32.ChangeServiceConfig(service, Win32.SERVICE_NO_CHANGE, Win32.SERVICE_NO_CHANGE, Win32.SERVICE_NO_CHANGE,
                        null, null, IntPtr.Zero, null, serviceName, password, null))
                    return Win32.LastError("Changement du compte du service").Message;
            }
            finally
            {
                Win32.CloseServiceHandle(service);
            }
        }
        finally
        {
            Win32.CloseServiceHandle(scm);
        }
        log.LogInformation("Compte du service changé : {Account}", serviceName);
        return Restart();
    }

    /// <summary>Windows right an account needs to run a service (granting it has no effect if it already has it).</summary>
    private static void GrantServiceLogon(NTAccount account)
    {
        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);

        var attributes = new Win32.LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<Win32.LSA_OBJECT_ATTRIBUTES>() };
        var status = Win32.LsaOpenPolicy(IntPtr.Zero, ref attributes, Win32.POLICY_CREATE_ACCOUNT | Win32.POLICY_LOOKUP_NAMES, out var policy);
        if (status != 0) throw new Win32Exception(Win32.LsaNtStatusToWinError(status));
        const string right = "SeServiceLogonRight";
        var buffer = Marshal.StringToHGlobalUni(right);
        try
        {
            var rights = new[] { new Win32.LSA_UNICODE_STRING { Buffer = buffer, Length = (ushort)(right.Length * 2), MaximumLength = (ushort)((right.Length + 1) * 2) } };
            status = Win32.LsaAddAccountRights(policy, bytes, rights, 1);
            if (status != 0) throw new Win32Exception(Win32.LsaNtStatusToWinError(status));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = Win32.LsaClose(policy); // closing a handle: nothing to do if it fails
        }
    }
}
