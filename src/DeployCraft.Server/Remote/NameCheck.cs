using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Vérifie que les noms de postes saisis dans « Déployer une fois » existent dans le DNS (vu du serveur, qui contactera
/// les postes). Les réponses sont gardées une minute : ajouter puis retirer un poste ne redemande rien.
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
        if (clean.Length == 0) return new(clean, false, null, "Nom vide.");
        if (IPAddress.TryParse(clean, out _)) return new(clean, true, clean, null); // une adresse IP se garde telle quelle
        if (_cache.TryGetValue(clean, out var hit) && DateTime.UtcNow - hit.Utc < CacheLifetime) return hit.Result;

        var result = await ResolveAsync(clean, ct);
        _cache[clean] = (result, DateTime.UtcNow);
        return result;
    }

    /// <summary>
    /// Résout le nom tel qu'il est saisi, comme le fera le déploiement : un nom complet dans un mauvais domaine est
    /// signalé, même si le nom court existe.
    /// </summary>
    private async Task<NameCheckResult> ResolveAsync(string name, CancellationToken ct)
    {
        try
        {
            return new(name, true, await _lookup(name == "." ? "localhost" : name, ct), null);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return new(name, false, null, "Introuvable dans le DNS (nom mal écrit, ou poste éteint depuis longtemps).");
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return new(name, false, null, $"Vérification impossible : {ex.Message}");
        }
    }

    /// <summary>Nom DNS complet du poste (ou le nom tel quel si le DNS n'en donne pas d'autre).</summary>
    private static async Task<string> DnsLookup(string host, CancellationToken ct)
    {
        var entry = await Dns.GetHostEntryAsync(host, ct);
        if (entry.AddressList.Length == 0) throw new SocketException((int)SocketError.NoData);
        return string.IsNullOrWhiteSpace(entry.HostName) ? host : entry.HostName;
    }
}
