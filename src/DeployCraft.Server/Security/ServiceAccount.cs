using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using DeployCraft.Server.Remote;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace DeployCraft.Server.Security;

/// <summary>État du service DeployCraft, renvoyé à la fenêtre Options › Service en arrière-plan.</summary>
public record ServiceInfo(bool IsService, string Account, bool LocalSystem, bool Elevated, DateTime StartedUtc);

/// <summary>
/// Le service DeployCraft lui-même : redémarrage, et compte Windows sous lequel il tourne (Système local, ou un compte
/// administrateur du serveur). Seul le vrai service peut agir sur lui-même : une instance de test lancée à la main
/// ne touche jamais au service installé.
/// </summary>
public class ServiceAccount(ILogger<ServiceAccount> log)
{
    public const string ServiceName = "DeployCraft";
    /// <summary>Valeur à choisir pour revenir au compte d'usine.</summary>
    public const string LocalSystem = "LocalSystem";

    public static bool IsService => WindowsServiceHelpers.IsWindowsService();

    public static ServiceInfo Info()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new ServiceInfo(IsService, identity.Name, identity.IsSystem, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            Process.GetCurrentProcess().StartTime.ToUniversalTime());
    }

    /// <summary>
    /// Redémarre le service dans deux secondes (le temps de répondre à la console) : une commande séparée l'arrête puis
    /// le relance. Erreur si ce n'est pas le service installé.
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
    /// Compte saisi → (domaine, utilisateur) pour l'ouverture de session et nom pour le service.
    /// « DOMAINE\nom », « .\nom » (compte local) ou « nom@domaine » ; erreur lisible sinon.
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
    /// Fait tourner le service sous un autre compte, puis le redémarre. Le compte doit exister, avoir ce mot de passe et
    /// être administrateur du serveur (dossier de données, déploiements). <see cref="LocalSystem"/> : retour au compte d'usine.
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
            // Contrôle avant tout changement : un compte refusé ne reçoit aucun droit sur le serveur. L'ouverture de
            // session « tâche planifiée » donne le jeton complet (sans le filtrage du contrôle de compte d'utilisateur)
            // et n'est permise d'usine qu'aux administrateurs.
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

    /// <summary>Droit Windows nécessaire à un compte pour faire tourner un service (accordé sans effet s'il l'a déjà).</summary>
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
            Win32.LsaClose(policy);
        }
    }
}
