using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record ServerStatus(string Machine, string RunningAs, bool Elevated, Credential? DefaultCredential, int UnreadableCredentials, string? Version,
    string? ConnectedAs);
public record ConsoleUser(string Sid, string Name);
public record ConsoleSession(string User, string Computer, DateTime LastSeenUtc);
public record ConsoleAccessInfo(List<ConsoleUser> Users, List<ConsoleSession> Sessions, bool CanEdit);
public record AdComputer(string Name, string? DnsName, string? OperatingSystem, string Ou, DateTime? LastLogon, bool Enabled);
public record ServiceInfo(bool IsService, string Account, bool LocalSystem, bool Elevated, DateTime StartedUtc);
public record ServiceState(ServiceInfo Info, bool CanEdit, bool Local);
public record ScheduleRunResult(List<Deployment> Deployments, string? Result);

/// <summary>Accès au service DeployCraft (API HTTP locale).</summary>
public static class Api
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Pour les fichiers enregistrés par la console (export de paquets).</summary>
    public static readonly JsonSerializerOptions JsonIndented = new(Json) { WriteIndented = true };

    // La session Windows de la personne sert d'identité auprès du service (authentification Windows).
    private static readonly HttpClient Http = new(new HttpClientHandler { UseDefaultCredentials = true })
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("DEPLOYCRAFT_URL") ?? "http://localhost:5080") + "/api/"),
        Timeout = TimeSpan.FromMinutes(2),
    };

    public static Task<ServerStatus> Status() => Get<ServerStatus>("status");

    public static Task<DeploymentDefaults> Defaults() => Get<DeploymentDefaults>("settings/defaults");
    public static Task<DeploymentDefaults> SaveDefaults(DeploymentDefaults d) => Send<DeploymentDefaults>(HttpMethod.Put, "settings/defaults", d);

    public static Task<List<Package>> Packages() => Get<List<Package>>("packages");
    public static Task<Package> Package(long id) => Get<Package>($"packages/{id}");
    public static Task<Package> SavePackage(Package p) =>
        p.Id == 0 ? Send<Package>(HttpMethod.Post, "packages", p) : Send<Package>(HttpMethod.Put, $"packages/{p.Id}", p);
    public static Task DeletePackage(long id) => Send<object>(HttpMethod.Delete, $"packages/{id}");
    public static Task MovePackage(long id, long? folderId) => Send<object>(HttpMethod.Put, $"packages/{id}/folder", new { folderId });

    /// <summary>Recherche de ces noms de postes dans le DNS du serveur.</summary>
    public static Task<List<NameCheckResult>> CheckNames(IEnumerable<string> names) =>
        Send<List<NameCheckResult>>(HttpMethod.Post, "names/check", names.ToList());

    /// <summary>Parmi ces chemins, ceux que le serveur ne trouve pas (avec l'identifiant par défaut).</summary>
    public static Task<List<string>> MissingFiles(IEnumerable<string> paths) => Send<List<string>>(HttpMethod.Post, "files/missing", paths.ToList());

    public static Task<List<PackageFolder>> Folders() => Get<List<PackageFolder>>("folders");
    public static Task<PackageFolder> SaveFolder(PackageFolder f) =>
        f.Id == 0 ? Send<PackageFolder>(HttpMethod.Post, "folders", f) : Send<PackageFolder>(HttpMethod.Put, $"folders/{f.Id}", f);
    public static Task DeleteFolder(long id) => Send<object>(HttpMethod.Delete, $"folders/{id}");

    /// <summary>Fichier d'export : les paquets choisis et les paquets imbriqués qu'ils utilisent.</summary>
    public static Task<PackageExport> ExportPackages(IEnumerable<long> ids) => Send<PackageExport>(HttpMethod.Post, "packages/export", ids.ToList());
    /// <summary>Import d'un fichier de paquets (.json ou .xml) : aperçu, ou création des paquets si <paramref name="apply"/>.</summary>
    public static Task<ImportReport> ImportPackages(string content, string? repository, bool apply) =>
        Send<ImportReport>(HttpMethod.Post, "packages/import", new { content, repository, apply });

    /// <summary>Sans recherche : tout le catalogue (paquets déjà téléchargés et logiciels courants en tête) ; sinon, les résultats.</summary>
    public static Task<LibraryInfo> Library(string? search = null) => Get<LibraryInfo>("library?search=" + Uri.EscapeDataString(search ?? ""));
    /// <summary>Seulement les logiciels qui ont un état propre (téléchargement en cours, erreur, paquet créé).</summary>
    public static Task<LibraryInfo> LibraryStatus() => Get<LibraryInfo>("library/status");
    public static Task RefreshLibrary() => Send<object>(HttpMethod.Post, "library/refresh");
    public static Task SetLibraryFolder(string folder) => Send<object>(HttpMethod.Put, "library/folder", new { folder });
    public static Task SetLibraryAutoUpdate(LibraryAutoUpdate mode, int days) => Send<object>(HttpMethod.Put, "library/auto-update", new { mode, days });
    /// <summary>Lance le téléchargement en arrière-plan ; la progression se lit dans <see cref="Library"/>.</summary>
    public static Task DownloadFromLibrary(string id) => Send<object>(HttpMethod.Post, "library/download", new { id });

    public static Task<List<Deployment>> Deployments() => Get<List<Deployment>>("deployments");
    public static Task<Deployment> Deployment(long id) => Get<Deployment>($"deployments/{id}");
    public static Task DeleteDeployment(long id) => Send<object>(HttpMethod.Delete, $"deployments/{id}");
    /// <summary>Un déploiement par paquet, dans l'ordre ; renvoie les déploiements créés.</summary>
    public static Task<List<Deployment>> Deploy(IEnumerable<long> packageIds, IEnumerable<TargetRequest> targets, DeployOptions options) =>
        Send<List<Deployment>>(HttpMethod.Post, "deployments",
            new { packageIds = packageIds.ToList(), targets = targets.ToList(), options });

    public static Task<List<string>> PackageTargets(long packageId) => Get<List<string>>($"packages/{packageId}/targets");
    public static Task SavePackageTargets(long packageId, IEnumerable<string> computers) =>
        Send<object>(HttpMethod.Put, $"packages/{packageId}/targets", computers.ToList());

    /// <summary>Annule des postes d'un déploiement (tous si la liste est vide).</summary>
    public static Task Abort(long deploymentId, IEnumerable<long>? targetIds = null) =>
        Send<object>(HttpMethod.Post, $"deployments/{deploymentId}/abort", new { targetIds = targetIds?.ToList() });

    public static Task<List<Schedule>> Schedules() => Get<List<Schedule>>("schedules");
    public static Task<Schedule> Schedule(long id) => Get<Schedule>($"schedules/{id}");
    public static Task<Schedule> SaveSchedule(Schedule s) =>
        s.Id == 0 ? Send<Schedule>(HttpMethod.Post, "schedules", s) : Send<Schedule>(HttpMethod.Put, $"schedules/{s.Id}", s);
    public static Task DeleteSchedule(long id) => Send<object>(HttpMethod.Delete, $"schedules/{id}");
    /// <summary>« Démarrer maintenant » ; renvoie le compte rendu du lancement.</summary>
    public static Task<ScheduleRunResult> RunSchedule(long id) =>
        Send<ScheduleRunResult>(HttpMethod.Post, $"schedules/{id}/run");
    public static Task<List<ScheduleTargetHistory>> ScheduleHistory(long id) => Get<List<ScheduleTargetHistory>>($"schedules/{id}/history");
    /// <summary>Efface des lignes de l'historique des cibles (toutes si la liste est vide).</summary>
    public static Task ClearScheduleHistory(long id, IEnumerable<ScheduleHistoryKey> keys) =>
        Send<object>(HttpMethod.Post, $"schedules/{id}/history/clear", keys.ToList());

    public static Task<List<Credential>> Credentials() => Get<List<Credential>>("credentials");
    public static Task<Credential> SaveCredential(long id, object input) =>
        id == 0 ? Send<Credential>(HttpMethod.Post, "credentials", input) : Send<Credential>(HttpMethod.Put, $"credentials/{id}", input);
    public static Task DeleteCredential(long id) => Send<object>(HttpMethod.Delete, $"credentials/{id}");
    public static Task SetDefaultCredential(long id) => Send<object>(HttpMethod.Post, $"credentials/{id}/default");
    /// <summary>Teste un compte du domaine (mot de passe vide + id = celui enregistré).</summary>
    public static Task TestCredential(object input) => Send<object>(HttpMethod.Post, "credentials/test", input);

    public static Task<ConsoleAccessInfo> Access() => Get<ConsoleAccessInfo>("access");
    public static Task<ConsoleUser> AddConsoleUser(string name) => Send<ConsoleUser>(HttpMethod.Post, "access/users", new { name });
    public static Task RemoveConsoleUser(string sid) => Send<object>(HttpMethod.Delete, $"access/users/{Uri.EscapeDataString(sid)}");

    public static Task<ServiceState> Service() => Get<ServiceState>("service");
    public static Task RestartService() => Send<object>(HttpMethod.Post, "service/restart");
    /// <summary>Compte « LocalSystem » (sans mot de passe) ou DOMAINE\nom : le service redémarre aussitôt.</summary>
    public static Task SetServiceAccount(string account, string password) => Send<object>(HttpMethod.Put, "service/account", new { account, password });

    public static Task<List<AdComputer>> AdComputers(string? search) =>
        Get<List<AdComputer>>("ad/computers?search=" + Uri.EscapeDataString(search ?? ""));

    private static Task<T> Get<T>(string url) => Send<T>(HttpMethod.Get, url);

    private static async Task<T> Send<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Le service DeployCraft ne répond pas. Vérifiez qu'il est démarré (services.msc).");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
                    if (error.TryGetProperty("error", out var e)) message = e.GetString();
                }
                catch (JsonException) { }
                if (message == null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Le service n'a pas pu vérifier votre compte Windows. Ouvrez la console avec un compte du domaine ou du serveur.";
                throw new ApiException(message ?? $"Erreur du service ({(int)response.StatusCode}).",
                    response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden);
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }
    }
}

public class ApiException(string message, bool accessDenied = false) : Exception(message)
{
    /// <summary>Le service répond, mais refuse ce compte Windows.</summary>
    public bool AccessDenied { get; } = accessDenied;
}
