using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using DeployCraft.Server.Data;
using Microsoft.AspNetCore.Authentication;

namespace DeployCraft.Server.Security;

/// <summary>Compte ou groupe Windows autorisé à utiliser la console, retenu par son SID (insensible aux renommages).</summary>
public record ConsoleUser(string Sid, string Name);

/// <summary>Compte connecté au service et heure de sa dernière demande.</summary>
public record ConsoleSession(string User, DateTime LastSeenUtc);

/// <summary>
/// Qui a le droit d'utiliser le service : les administrateurs locaux du serveur (toujours, pour qu'on ne puisse pas
/// se fermer la porte), plus les comptes et groupes de la liste « Utilisateurs de la console ».
/// </summary>
public class ConsoleAccess(Store store)
{
    public const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>Une console interroge le service toutes les 2 s : au-delà de 2 minutes sans demande, elle est fermée.</summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(2);

    private const string SettingKey = "console.users";
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public List<ConsoleUser> ListUsers() =>
        store.GetSetting(SettingKey) is { } json ? JsonSerializer.Deserialize<List<ConsoleUser>>(json, Store.Json) ?? [] : [];

    /// <summary>Ajoute un compte ou un groupe (DOMAINE\nom) ; renvoie l'entrée enregistrée, ou une erreur lisible.</summary>
    public (ConsoleUser? User, string? Error) AddUser(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, "Saisissez un compte ou un groupe (DOMAINE\\nom).");
        ConsoleUser user;
        try
        {
            var sid = (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
            user = new ConsoleUser(sid.Value, sid.Translate(typeof(NTAccount)).Value);
        }
        catch (SystemException) // nom mal écrit, inconnu, ou annuaire injoignable
        {
            return (null, $"Compte ou groupe « {name} » introuvable. Écrivez-le sous la forme DOMAINE\\nom.");
        }
        var users = ListUsers();
        if (user.Sid == AdministratorsSid || users.Any(u => u.Sid == user.Sid)) return (null, $"« {user.Name} » a déjà accès.");
        users.Add(user);
        Save(users);
        return (user, null);
    }

    public void RemoveUser(string sid) => Save(ListUsers().Where(u => u.Sid != sid).ToList());

    private void Save(List<ConsoleUser> users) =>
        store.SetSetting(SettingKey, users.Count == 0 ? null : JsonSerializer.Serialize(users, Store.Json));

    /// <summary>SID de la personne et de ses groupes, y compris ceux que le contrôle de compte (UAC) garde en refus seul.</summary>
    public static HashSet<string> Sids(ClaimsPrincipal user)
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in user.Claims)
            if (claim.Type is ClaimTypes.PrimarySid or ClaimTypes.GroupSid or ClaimTypes.DenyOnlySid)
                sids.Add(claim.Value);
        return sids;
    }

    /// <summary>
    /// Une console ouverte sans élévation présente le groupe Administrateurs « en refus seul » : on le compte quand
    /// même, la personne est bien administrateur du serveur.
    /// </summary>
    public static bool IsServerAdmin(IReadOnlySet<string> sids) => sids.Contains(AdministratorsSid);

    public static bool IsAllowed(IReadOnlySet<string> sids, IEnumerable<ConsoleUser> users) =>
        IsServerAdmin(sids) || users.Any(u => sids.Contains(u.Sid));

    public void Seen(string user) => _sessions[user] = DateTime.UtcNow;

    public List<ConsoleSession> ActiveSessions()
    {
        var limit = DateTime.UtcNow - SessionTimeout;
        foreach (var old in _sessions.Where(s => s.Value < limit).ToList()) _sessions.TryRemove(old);
        return _sessions.Select(s => new ConsoleSession(s.Key, s.Value)).OrderBy(s => s.User, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Filtre de l'API : la console s'identifie avec la session Windows de la personne (Negotiate, sans mot de passe à
    /// saisir) ; un compte qui n'est ni administrateur du serveur ni dans la liste est refusé avec un message clair.
    /// </summary>
    public async Task Check(HttpContext context, Func<Task> next)
    {
        if (context.User.Identity is not { IsAuthenticated: true, Name: { } name })
        {
            await context.ChallengeAsync();
            return;
        }
        if (!IsAllowed(Sids(context.User), ListUsers()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Votre compte {name} n'est pas autorisé à utiliser DeployCraft sur {Environment.MachineName}. " +
                        "Un administrateur de ce serveur peut l'ajouter dans Options › Utilisateurs de la console.",
            });
            return;
        }
        Seen(name);
        await next();
    }
}
