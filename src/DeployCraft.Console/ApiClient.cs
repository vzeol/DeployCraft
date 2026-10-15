using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record ServerStatus(string Machine, string RunningAs, bool Elevated, Credential? DefaultCredential, int UnreadableCredentials, string? Version);
public record AdComputer(string Name, string? DnsName, string? OperatingSystem, string Ou, DateTime? LastLogon, bool Enabled);
public record ScheduleRunResult(List<Deployment> Deployments, string? Result);

/// <summary>Accès au service DeployCraft (API HTTP locale).</summary>
public static class Api
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("DEPLOYCRAFT_URL") ?? "http://localhost:5080") + "/api/"),
        Timeout = TimeSpan.FromMinutes(2),
    };

    public static Task<ServerStatus> Status() => Get<ServerStatus>("status");

    public static Task<DeploymentDefaults> Defaults() => Get<DeploymentDefaults>("settings/defaults");
    public static Task<DeploymentDefaults> SaveDefaults(DeploymentDefaults d) => Send<DeploymentDefaults>(HttpMethod.Put, "settings/defaults", d);

    public static Task<List<Variable>> Variables() => Get<List<Variable>>("variables");
    public static Task<List<Variable>> SaveVariables(List<Variable> v) => Send<List<Variable>>(HttpMethod.Put, "variables", v);

    public static Task<List<Package>> Packages() => Get<List<Package>>("packages");
    public static Task<Package> Package(long id) => Get<Package>($"packages/{id}");
    public static Task<Package> SavePackage(Package p) =>
        p.Id == 0 ? Send<Package>(HttpMethod.Post, "packages", p) : Send<Package>(HttpMethod.Put, $"packages/{p.Id}", p);
    public static Task DeletePackage(long id) => Send<object>(HttpMethod.Delete, $"packages/{id}");

    public static Task<List<Deployment>> Deployments() => Get<List<Deployment>>("deployments");
    public static Task<Deployment> Deployment(long id) => Get<Deployment>($"deployments/{id}");
    public static Task DeleteDeployment(long id) => Send<object>(HttpMethod.Delete, $"deployments/{id}");
    /// <summary>Un déploiement par paquet, dans l'ordre ; renvoie les déploiements créés.</summary>
    public static Task<List<Deployment>> Deploy(IEnumerable<long> packageIds, IEnumerable<TargetRequest> targets, DeployOptions options) =>
        Send<List<Deployment>>(HttpMethod.Post, "deployments",
            new { packageIds = packageIds.ToList(), targets = targets.ToList(), launchedBy = CurrentUser, options });

    public static Task<List<string>> PackageTargets(long packageId) => Get<List<string>>($"packages/{packageId}/targets");
    public static Task SavePackageTargets(long packageId, IEnumerable<string> computers) =>
        Send<object>(HttpMethod.Put, $"packages/{packageId}/targets", computers.ToList());

    /// <summary>Annule des postes d'un déploiement (tous si la liste est vide).</summary>
    public static Task Abort(long deploymentId, IEnumerable<long>? targetIds = null) =>
        Send<object>(HttpMethod.Post, $"deployments/{deploymentId}/abort", new { targetIds = targetIds?.ToList(), by = CurrentUser });

    public static Task<List<Schedule>> Schedules() => Get<List<Schedule>>("schedules");
    public static Task<Schedule> Schedule(long id) => Get<Schedule>($"schedules/{id}");
    public static Task<Schedule> SaveSchedule(Schedule s) =>
        s.Id == 0 ? Send<Schedule>(HttpMethod.Post, "schedules", s) : Send<Schedule>(HttpMethod.Put, $"schedules/{s.Id}", s);
    public static Task DeleteSchedule(long id) => Send<object>(HttpMethod.Delete, $"schedules/{id}");
    /// <summary>« Démarrer maintenant » ; renvoie le compte rendu du lancement.</summary>
    public static Task<ScheduleRunResult> RunSchedule(long id) =>
        Send<ScheduleRunResult>(HttpMethod.Post, $"schedules/{id}/run", new { launchedBy = CurrentUser });
    public static Task<List<ScheduleTargetHistory>> ScheduleHistory(long id) => Get<List<ScheduleTargetHistory>>($"schedules/{id}/history");
    /// <summary>Efface des lignes de l'historique des cibles (toutes si la liste est vide).</summary>
    public static Task ClearScheduleHistory(long id, IEnumerable<ScheduleHistoryKey> keys) =>
        Send<object>(HttpMethod.Post, $"schedules/{id}/history/clear", keys.ToList());

    private static string CurrentUser => $@"{Environment.UserDomainName}\{Environment.UserName}";

    public static Task<List<Credential>> Credentials() => Get<List<Credential>>("credentials");
    public static Task<Credential> SaveCredential(long id, object input) =>
        id == 0 ? Send<Credential>(HttpMethod.Post, "credentials", input) : Send<Credential>(HttpMethod.Put, $"credentials/{id}", input);
    public static Task DeleteCredential(long id) => Send<object>(HttpMethod.Delete, $"credentials/{id}");
    public static Task SetDefaultCredential(long id) => Send<object>(HttpMethod.Post, $"credentials/{id}/default");
    /// <summary>Teste un compte du domaine (mot de passe vide + id = celui enregistré).</summary>
    public static Task TestCredential(object input) => Send<object>(HttpMethod.Post, "credentials/test", input);

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
                throw new ApiException(message ?? $"Erreur du service ({(int)response.StatusCode}).");
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }
    }
}

public class ApiException(string message) : Exception(message);
