using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Checks that the computer names typed in "Déployer une fois" exist in DNS (as seen from the server, which will contact
/// the computers). Answers are kept for one minute: adding then removing a computer asks nothing again.
/// </summary>
public sealed class NameCheck(Func<string, CancellationToken, Task<string>>? lookup = null)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);
    private readonly Func<string, CancellationToken, Task<string>> _lookup = lookup ?? DnsLookup;
    private readonly ConcurrentDictionary<string, (NameCheckResult Result, DateTime Utc)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<NameCheckResult[]> CheckAsync(IEnumerable<string> names, CancellationToken ct) =>
        Task.WhenAll(names.Select(n => CheckAsync(n, ct)));

    public async Task<NameCheckResult> CheckAsync(string name, CancellationToken ct)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) return new(clean, false, null, Msg.Get("NameCheck_Empty"));
        if (IPAddress.TryParse(clean, out _)) return new(clean, true, clean, null); // an IP address is kept as is
        if (_cache.TryGetValue(clean, out var hit) && DateTime.UtcNow - hit.Utc < CacheLifetime) return hit.Result;

        var result = await ResolveAsync(clean, ct);
        _cache[clean] = (result, DateTime.UtcNow);
        return result;
    }

    /// <summary>
    /// Resolves the name as typed, as the deployment will: a full name in the wrong domain is
    /// reported, even if the short name exists.
    /// </summary>
    private async Task<NameCheckResult> ResolveAsync(string name, CancellationToken ct)
    {
        try
        {
            return new(name, true, await _lookup(name == "." ? "localhost" : name, ct), null);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return new(name, false, null, Msg.Get("NameCheck_NotInDns"));
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return new(name, false, null, Msg.Format("NameCheck_Failed", ex.Message));
        }
    }

    /// <summary>Full DNS name of the computer (or the name as is if DNS gives no other).</summary>
    private static async Task<string> DnsLookup(string host, CancellationToken ct)
    {
        var entry = await Dns.GetHostEntryAsync(host, ct);
        if (entry.AddressList.Length == 0) throw new SocketException((int)SocketError.NoData);
        return string.IsNullOrWhiteSpace(entry.HostName) ? host : entry.HostName;
    }
}
