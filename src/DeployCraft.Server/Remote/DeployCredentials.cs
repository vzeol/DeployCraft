using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using DeployCraft.Server.Data;
using Microsoft.Win32.SafeHandles;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Identifiants (comptes admin des postes). Les mots de passe sont chiffrés avec DPAPI
/// (portée machine) : la base copiée sur un autre PC ne permet pas de les relire. Le dossier
/// de données est en plus réservé à Système et aux Administrateurs (voir <see cref="DataFolder"/>).
/// </summary>
public class DeployCredentials
{
    private static readonly byte[] Entropy = "DeployCraft.Credentials.v1"u8.ToArray();

    private readonly Store _store;

    public DeployCredentials(Store store)
    {
        _store = store;
        MigrateSingleAccount();
    }

    /// <summary>Avant 0.9, un seul compte rangé dans les réglages : il devient le premier identifiant (par défaut).</summary>
    private void MigrateSingleAccount()
    {
        var user = _store.GetSetting("cred.user");
        if (string.IsNullOrEmpty(user)) return;
        if (_store.ListCredentials().Count == 0)
            _store.SaveCredential(new Credential { Domain = _store.GetSetting("cred.domain"), User = user }, _store.GetSetting("cred.password"));
        _store.SetSetting("cred.domain", null);
        _store.SetSetting("cred.user", null);
        _store.SetSetting("cred.password", null);
    }

    public List<Credential> List() => _store.ListCredentials().Select(WithState).ToList();

    public Credential? Get(long id) => _store.GetCredential(id) is { } c ? WithState(c) : null;

    public Credential? Default
    {
        get
        {
            var all = List();
            return all.FirstOrDefault(c => c.IsDefault) ?? all.FirstOrDefault();
        }
    }

    /// <summary>L'identifiant désigné, ou celui par défaut (null : aucun identifiant enregistré).</summary>
    public Credential? Resolve(long? id) => id is { } i ? Get(i) : Default;

    /// <summary>Enregistre un identifiant ; mot de passe vide = on garde l'actuel (l'interface ne le renvoie jamais).</summary>
    public Credential Save(Credential c, string? password)
    {
        c.Normalize();
        c.Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim();
        var secret = string.IsNullOrEmpty(password)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.LocalMachine));
        return WithState(_store.SaveCredential(c, secret));
    }

    public void Delete(long id) => _store.DeleteCredential(id);

    public void SetDefault(long id) => _store.SetDefaultCredential(id);

    private Credential WithState(Credential c)
    {
        c.PasswordUnreadable = TryPassword(c.Id) == null;
        return c;
    }

    private string? TryPassword(long id)
    {
        var stored = _store.GetCredentialSecret(id);
        if (stored == null) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.LocalMachine));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // chiffré sur un autre serveur : à ressaisir
        }
    }

    internal string Password(Credential c) => TryPassword(c.Id)
        ?? throw new DeployException($"Le mot de passe de {c.Account} ne peut pas être relu : ressaisissez-le (Options › Identifiants).");

    /// <summary>
    /// Nom sous lequel le compte s'authentifie auprès de <paramref name="computer"/> : un compte local
    /// est vérifié par le poste lui-même, donc « POSTE\utilisateur ».
    /// </summary>
    public static string LogonDomain(Credential c, string? computer)
    {
        if (!c.IsLocal) return c.Domain!;
        if (computer == null || TargetSession.IsLocalComputer(computer)) return Environment.MachineName;
        return IPAddress.TryParse(computer, out _) ? computer : computer.Split('.')[0];
    }

    /// <summary>
    /// Exécute <paramref name="action"/> en utilisant <paramref name="c"/> pour tout accès réseau vers
    /// <paramref name="computer"/> (partage ADMIN$, gestionnaire de services distant). Sans identifiant : identité du service.
    /// </summary>
    public T RunAs<T>(Credential? c, string? computer, Func<T> action) =>
        c == null ? action() : RunAs(c, Password(c), computer, action);

    public void RunAs(Credential? c, string? computer, Action action) => RunAs(c, computer, () => { action(); return 0; });

    private static T RunAs<T>(Credential c, string password, string? computer, Func<T> action)
    {
        // NEW_CREDENTIALS : l'identité locale ne change pas, seuls les accès réseau utilisent ce compte.
        if (!Win32.LogonUser(c.User, LogonDomain(c, computer), password, Win32.LOGON32_LOGON_NEW_CREDENTIALS,
                Win32.LOGON32_PROVIDER_WINNT50, out SafeAccessTokenHandle token))
            throw Win32.LastError($"Ouverture de session de {c.Account}");
        using (token)
            return WindowsIdentity.RunImpersonated(token, action);
    }

    /// <summary>
    /// Essaie le compte sur un poste, comme le ferait un déploiement : il doit ouvrir le partage
    /// d'administration ADMIN$. Sur le serveur lui-même, on vérifie seulement le mot de passe.
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

    /// <summary>Identifiant pour lire l'Active Directory : celui par défaut s'il est du domaine, sinon le premier du domaine.</summary>
    public Credential ForDirectory()
    {
        var all = List();
        return all.FirstOrDefault(c => c.IsDefault && !c.IsLocal) ?? all.FirstOrDefault(c => !c.IsLocal)
            ?? throw new DeployException("Ajoutez un identifiant du domaine (Options › Identifiants) pour lire l'Active Directory.");
    }
}
