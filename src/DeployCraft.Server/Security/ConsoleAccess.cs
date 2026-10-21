using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using DeployCraft.Server.Data;
using Microsoft.AspNetCore.Authentication;

namespace DeployCraft.Server.Security;

/// <summary>Windows account or group allowed to use the console, kept by its SID (unaffected by renames).</summary>
public record ConsoleUser(string Sid, string Name);

/// <summary>Console connected to the service: account, computer it connects from, time of its last request.</summary>
public record ConsoleSession(string User, string Computer, DateTime LastSeenUtc);

/// <summary>
/// Who may use the service: the server's local administrators (always, so nobody can lock themselves
/// out), plus the accounts and groups of the "Utilisateurs de la console" list.
/// </summary>
public class ConsoleAccess(ISettingsRepository settingsRepository)
{
    public const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>A console polls the service every 2 s: after more than 2 minutes without a request, it is closed.</summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(2);

    private const string SettingKey = "console.users";
    // Key: "account|address" (the same person can open the console on two computers).
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.OrdinalIgnoreCase);
    // Computer names from their address, looked up once in DNS (the address is shown meanwhile).
    private readonly ConcurrentDictionary<string, string> _computers = new();

    public List<ConsoleUser> ListUsers() =>
        settingsRepository.Get(SettingKey) is { } json ? JsonSerializer.Deserialize<List<ConsoleUser>>(json, Database.Json) ?? [] : [];

    /// <summary>Adds an account or group (DOMAIN\name); returns the saved entry, or a readable error.</summary>
    public (ConsoleUser? User, string? Error) AddUser(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, Msg.Get("ConsoleAccess_EnterAccount"));
        ConsoleUser user;
        try
        {
            var sid = (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
            user = new ConsoleUser(sid.Value, sid.Translate(typeof(NTAccount)).Value);
        }
        catch (SystemException) // misspelled name, unknown, or directory unreachable
        {
            return (null, Msg.Format("ConsoleAccess_NotFound", name));
        }
        var users = ListUsers();
        if (user.Sid == AdministratorsSid || users.Any(u => u.Sid == user.Sid)) return (null, Msg.Format("ConsoleAccess_AlreadyAllowed", user.Name));
        users.Add(user);
        Save(users);
        return (user, null);
    }

    public void RemoveUser(string sid) => Save(ListUsers().Where(u => u.Sid != sid).ToList());

    private void Save(List<ConsoleUser> users) =>
        settingsRepository.Set(SettingKey, users.Count == 0 ? null : JsonSerializer.Serialize(users, Database.Json));

    /// <summary>SID of the person and of their groups, including those that User Account Control (UAC) keeps deny-only.</summary>
    public static HashSet<string> Sids(ClaimsPrincipal user)
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in user.Claims)
            if (claim.Type is ClaimTypes.PrimarySid or ClaimTypes.GroupSid or ClaimTypes.DenyOnlySid)
                sids.Add(claim.Value);
        return sids;
    }

    /// <summary>
    /// A console opened without elevation presents the Administrators group as "deny-only": it is counted
    /// anyway, the person really is an administrator of the server.
    /// </summary>
    public static bool IsServerAdmin(IReadOnlySet<string> sids) => sids.Contains(AdministratorsSid);

    public static bool IsAllowed(IReadOnlySet<string> sids, IEnumerable<ConsoleUser> users) =>
        IsServerAdmin(sids) || users.Any(u => sids.Contains(u.Sid));

    /// <param name="address">Console address (null or loopback: the console of the server itself).</param>
    public void Seen(string user, IPAddress? address = null)
    {
        var key = address == null || IPAddress.IsLoopback(address) ? "" : address.ToString();
        _sessions[$"{user}|{key}"] = DateTime.UtcNow;
        if (key.Length > 0 && _computers.TryAdd(key, key)) _ = ResolveAsync(address!, key);
    }

    private async Task ResolveAsync(IPAddress address, string key)
    {
        try
        {
            var name = (await Dns.GetHostEntryAsync(address)).HostName;
            if (!string.IsNullOrWhiteSpace(name)) _computers[key] = name.Split('.')[0].ToUpperInvariant();
        }
        catch (System.Net.Sockets.SocketException) { /* l'adresse reste affichée */ }
    }

    public List<ConsoleSession> ActiveSessions()
    {
        var limit = DateTime.UtcNow - SessionTimeout;
        foreach (var old in _sessions.Where(s => s.Value < limit).ToList()) _sessions.TryRemove(old);
        return _sessions.Select(s =>
            {
                var cut = s.Key.LastIndexOf('|');
                var address = s.Key[(cut + 1)..];
                return new ConsoleSession(s.Key[..cut], address.Length == 0 ? Environment.MachineName : _computers.GetValueOrDefault(address, address), s.Value);
            })
            .OrderBy(s => s.User, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Computer, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// API filter: the console identifies itself with the person's Windows session (Negotiate, no password to
    /// type); an account that is neither a server administrator nor in the list is refused with a clear message.
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
                error = Msg.Format("ConsoleAccess_Refused", name, Environment.MachineName),
            });
            return;
        }
        Seen(name, context.Connection.RemoteIpAddress);
        await next();
    }
}
