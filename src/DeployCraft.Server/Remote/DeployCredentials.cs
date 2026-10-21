using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using DeployCraft.Server.Data;
using Microsoft.Win32.SafeHandles;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Credentials (admin accounts of the computers). Passwords are encrypted with DPAPI
/// (machine scope): a database copied to another PC does not allow reading them. The data folder
/// is also restricted to System and Administrators (see <see cref="DataFolder"/>).
/// </summary>
public class DeployCredentials
{
    private static readonly byte[] Entropy = "DeployCraft.Credentials.v1"u8.ToArray();

    private readonly ICredentialRepository _credentialRepository;
    private readonly ISettingsRepository _settingsRepository;

    public DeployCredentials(ICredentialRepository credentialRepository, ISettingsRepository settingsRepository)
    {
        _credentialRepository = credentialRepository;
        _settingsRepository = settingsRepository;
        MigrateSingleAccount();
    }

    /// <summary>Before 0.9, a single account stored in the settings: it becomes the first credential (the default).</summary>
    private void MigrateSingleAccount()
    {
        var user = _settingsRepository.Get("cred.user");
        if (string.IsNullOrEmpty(user)) return;
        if (_credentialRepository.List().Count == 0)
            _credentialRepository.Save(new Credential { Domain = _settingsRepository.Get("cred.domain"), User = user }, _settingsRepository.Get("cred.password"));
        _settingsRepository.Set("cred.domain", null);
        _settingsRepository.Set("cred.user", null);
        _settingsRepository.Set("cred.password", null);
    }

    public List<Credential> List() => _credentialRepository.List().Select(WithState).ToList();

    public Credential? Get(long id) => _credentialRepository.Get(id) is { } c ? WithState(c) : null;

    public Credential? Default
    {
        get
        {
            var all = List();
            return all.FirstOrDefault(c => c.IsDefault) ?? all.FirstOrDefault();
        }
    }

    /// <summary>The designated credential, or the default one (null: no credential saved).</summary>
    public Credential? Resolve(long? id) => id is { } i ? Get(i) : Default;

    /// <summary>Saves a credential; empty password = keep the current one (the interface never sends it back).</summary>
    public Credential Save(Credential c, string? password)
    {
        c.Normalize();
        c.Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim();
        var secret = string.IsNullOrEmpty(password)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.LocalMachine));
        return WithState(_credentialRepository.Save(c, secret));
    }

    public void Delete(long id) => _credentialRepository.Delete(id);

    public void SetDefault(long id) => _credentialRepository.SetDefault(id);

    private Credential WithState(Credential c)
    {
        c.PasswordUnreadable = TryPassword(c.Id) == null;
        return c;
    }

    private string? TryPassword(long id)
    {
        var stored = _credentialRepository.GetSecret(id);
        if (stored == null) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.LocalMachine));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // encrypted on another server: to be re-entered
        }
    }

    internal string Password(Credential c) => TryPassword(c.Id)
        ?? throw new DeployException($"Le mot de passe de {c.Account} ne peut pas être relu : ressaisissez-le (Options › Identifiants).");

    /// <summary>
    /// Name under which the account authenticates to <paramref name="computer"/>: a local account
    /// is verified by the computer itself, hence "COMPUTER\user".
    /// </summary>
    public static string LogonDomain(Credential c, string? computer)
    {
        if (!c.IsLocal) return c.Domain!;
        if (computer == null || TargetSession.IsLocalComputer(computer)) return Environment.MachineName;
        return IPAddress.TryParse(computer, out _) ? computer : computer.Split('.')[0];
    }

    /// <summary>
    /// Runs <paramref name="action"/> using <paramref name="c"/> for all network access to
    /// <paramref name="computer"/> (ADMIN$ share, remote service manager). Without a credential: the service's identity.
    /// </summary>
    public T RunAs<T>(Credential? c, string? computer, Func<T> action) =>
        c == null ? action() : RunAs(c, Password(c), computer, action);

    public void RunAs(Credential? c, string? computer, Action action) => RunAs(c, computer, () => { action(); return 0; });

    private static T RunAs<T>(Credential c, string password, string? computer, Func<T> action)
    {
        // NEW_CREDENTIALS: the local identity does not change, only network access uses this account.
        if (!Win32.LogonUser(c.User, LogonDomain(c, computer), password, Win32.LOGON32_LOGON_NEW_CREDENTIALS,
                Win32.LOGON32_PROVIDER_WINNT50, out SafeAccessTokenHandle token))
            throw Win32.LastError($"Ouverture de session de {c.Account}");
        using (token)
            return WindowsIdentity.RunImpersonated(token, action);
    }

    /// <summary>
    /// Tries the account on a computer, as a deployment would: it must open the ADMIN$
    /// administrative share. On the server itself, only the password is checked.
    /// </summary>
    public static void TestOnComputer(Credential c, string password, string computer)
    {
        if (TargetSession.IsLocalComputer(computer))
        {
            if (!Win32.LogonUser(c.User, LogonDomain(c, computer), password, Win32.LOGON32_LOGON_NETWORK,
                    Win32.LOGON32_PROVIDER_DEFAULT, out SafeAccessTokenHandle local))
                throw new DeployException($"{c.Account} refusé par ce serveur : {new Win32Exception().Message}");
            local.Dispose();
            return;
        }
        try
        {
            RunAs(c, password, computer, () => Directory.EnumerateFileSystemEntries($@"\\{computer}\ADMIN$").FirstOrDefault());
        }
        catch (UnauthorizedAccessException)
        {
            throw new DeployException($"{c.Account} n'a pas accès à ADMIN$ sur {computer} : mot de passe incorrect ou compte non administrateur du poste.");
        }
        catch (IOException ex)
        {
            throw new DeployException($"{c.Account} refusé par {computer} : {new Win32Exception(ex.HResult & 0xFFFF).Message}");
        }
    }

    /// <summary>Credential for reading Active Directory: the default one if it belongs to the domain, otherwise the first of the domain.</summary>
    public Credential ForDirectory()
    {
        var all = List();
        return all.FirstOrDefault(c => c.IsDefault && !c.IsLocal) ?? all.FirstOrDefault(c => !c.IsLocal)
            ?? throw new DeployException("Ajoutez un identifiant du domaine (Options › Identifiants) pour lire l'Active Directory.");
    }
}
