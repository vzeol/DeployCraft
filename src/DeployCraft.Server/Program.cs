using System.Security.Principal;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // nécessaire quand on tourne en service Windows
});
builder.Host.UseWindowsService(o => o.ServiceName = "DeployCraft");

// Données dans C:\ProgramData\DeployCraft (base SQLite) ; --DataDir pour une instance de test.
var dataDir = builder.Configuration["DataDir"]
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DeployCraft");
DataFolder.Restrict(dataDir, LoggerFactory.Create(l => l.AddConsole()).CreateLogger("DeployCraft"));
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5080");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton(new Store(Path.Combine(dataDir, "deploycraft.db")));
builder.Services.AddSingleton<DeployCredentials>();
builder.Services.AddSingleton<ActiveDirectory>();
builder.Services.AddSingleton<DeploymentEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeploymentEngine>());
builder.Services.AddSingleton<Scheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());

var app = builder.Build();

var api = app.MapGroup("/api");

// ---------- État ----------

api.MapGet("/status", (DeployCredentials cred) =>
{
    using var identity = WindowsIdentity.GetCurrent();
    var all = cred.List();
    return new
    {
        machine = Environment.MachineName,
        runningAs = identity.Name,
        elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
        defaultCredential = all.FirstOrDefault(c => c.IsDefault) ?? all.FirstOrDefault(),
        unreadableCredentials = all.Count(c => c.PasswordUnreadable),
        version = typeof(Program).Assembly.GetName().Version?.ToString(3),
    };
});

// ---------- Identifiants ----------

api.MapGet("/credentials", (DeployCredentials cred) => cred.List());

api.MapPost("/credentials", (CredentialInput input, DeployCredentials cred) =>
{
    if (ValidateCredential(input, isNew: true) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(cred.Save(input.ToCredential(0), input.Password));
});

api.MapPut("/credentials/{id:long}", (long id, CredentialInput input, DeployCredentials cred) =>
{
    if (cred.Get(id) == null) return Results.NotFound();
    if (ValidateCredential(input, isNew: false) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(cred.Save(input.ToCredential(id), input.Password));
});

api.MapDelete("/credentials/{id:long}", (long id, DeployCredentials cred, Store store) =>
{
    var users = store.ListSchedules().Where(s => s.CredentialId == id).Select(s => $"« {s.Name} »").ToList();
    if (users.Count > 0)
        return Results.BadRequest(new { error = $"Cet identifiant est utilisé par la planification {string.Join(", ", users)} : choisissez-en un autre dans ses options avant de le supprimer." });
    cred.Delete(id);
    return Results.NoContent();
});

api.MapPost("/credentials/{id:long}/default", (long id, DeployCredentials cred) =>
{
    if (cred.Get(id) == null) return Results.NotFound();
    cred.SetDefault(id);
    return Results.NoContent();
});

// Teste un compte du domaine auprès de l'annuaire (mot de passe vide + id = celui enregistré).
// Un compte local ne peut être vérifié que par chaque poste, au moment du déploiement.
api.MapPost("/credentials/test", (CredentialInput input, DeployCredentials cred) =>
{
    var account = input.ToCredential(input.Id ?? 0);
    if (account.IsLocal)
        return Results.BadRequest(new { error = "Un compte local est vérifié par chaque poste au moment du déploiement : il ne peut pas être testé ici." });
    try
    {
        var password = input.Password;
        if (string.IsNullOrEmpty(password) && input.Id is { } id && cred.Get(id) is { } stored) password = cred.Password(stored);
        if (string.IsNullOrEmpty(password)) return Results.BadRequest(new { error = "Saisissez le mot de passe." });
        ActiveDirectory.TestConnection(account, password);
        return Results.Ok(new { ok = true });
    }
    catch (DeployException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ---------- Active Directory ----------

api.MapGet("/ad/computers", (string? search, ActiveDirectory ad) =>
{
    try
    {
        return Results.Ok(ad.ListComputers(search));
    }
    catch (DeployException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ---------- Paquets ----------

api.MapGet("/packages", (Store store) => store.ListPackages());

api.MapGet("/packages/{id:long}", (long id, Store store) =>
    store.GetPackage(id) is { } p ? Results.Ok(p) : Results.NotFound());

api.MapPost("/packages", (Package p, Store store) =>
{
    if (Validate(p) is { } error) return Results.BadRequest(new { error });
    p.Id = 0;
    return Results.Ok(store.SavePackage(p));
});

api.MapPut("/packages/{id:long}", (long id, Package p, Store store) =>
{
    if (store.GetPackage(id) == null) return Results.NotFound();
    if (Validate(p) is { } error) return Results.BadRequest(new { error });
    p.Id = id;
    return Results.Ok(store.SavePackage(p));
});

api.MapDelete("/packages/{id:long}", (long id, Store store) =>
{
    store.DeletePackage(id);
    return Results.NoContent();
});

// « Mémoriser ces cibles » : la fenêtre Déployer une fois les propose à nouveau pour ce paquet.
api.MapGet("/packages/{id:long}/targets", (long id, Store store) => store.GetPackageTargets(id));

api.MapPut("/packages/{id:long}/targets", (long id, List<string> computers, Store store) =>
{
    if (store.GetPackage(id) == null) return Results.NotFound();
    store.SetPackageTargets(id, computers.Select(c => c.Trim()).Where(c => c.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    return Results.NoContent();
});

// ---------- Déploiements ----------

// Un déploiement par paquet, dans l'ordre : la réponse est la liste des déploiements créés.
api.MapPost("/deployments", (DeployInput input, Store store, DeploymentEngine engine) =>
{
    var packages = new List<Package>();
    foreach (var id in input.PackageIds)
    {
        if (store.GetPackage(id) is not { } package) return Results.NotFound(new { error = $"Paquet introuvable (n° {id})." });
        packages.Add(package);
    }
    try
    {
        return Results.Ok(engine.Start(packages, input.Targets, input.LaunchedBy, input.Options ?? new DeployOptions()));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/deployments", (Store store) => store.ListDeployments());

api.MapGet("/deployments/{id:long}", (long id, Store store) =>
    store.GetDeployment(id) is { } d ? Results.Ok(d) : Results.NotFound());

api.MapPost("/deployments/{id:long}/abort", (long id, AbortInput input, DeploymentEngine engine) =>
    Results.Ok(new { aborted = engine.Abort(id, input.TargetIds, input.By) }));

api.MapDelete("/deployments/{id:long}", (long id, Store store) =>
{
    if (store.GetDeployment(id) is not { } d) return Results.NoContent();
    if (d.Status is DeploymentStatus.Running or DeploymentStatus.Queued)
        return Results.BadRequest(new { error = "Ce déploiement est en cours : attendez qu'il se termine." });
    store.DeleteDeployment(id);
    return Results.NoContent();
});

// ---------- Planifications ----------

api.MapGet("/schedules", (Store store) => store.ListSchedules().Select(WithNextRun));

api.MapGet("/schedules/{id:long}", (long id, Store store) =>
    store.GetSchedule(id) is { } s ? Results.Ok(WithNextRun(s)) : Results.NotFound());

api.MapPost("/schedules", (Schedule s, Store store, DeployCredentials cred) =>
{
    if (ValidateSchedule(s, cred) is { } error) return Results.BadRequest(new { error });
    s.Id = 0;
    return Results.Ok(WithNextRun(store.SaveSchedule(s)));
});

api.MapPut("/schedules/{id:long}", (long id, Schedule s, Store store, DeployCredentials cred) =>
{
    if (store.GetSchedule(id) == null) return Results.NotFound();
    if (ValidateSchedule(s, cred) is { } error) return Results.BadRequest(new { error });
    s.Id = id;
    return Results.Ok(WithNextRun(store.SaveSchedule(s)));
});

api.MapDelete("/schedules/{id:long}", (long id, Store store) =>
{
    store.DeleteSchedule(id);
    return Results.NoContent();
});

// « Démarrer maintenant » : même règles qu'un déclenchement (postes réussis ou en cours ignorés).
api.MapPost("/schedules/{id:long}/run", (long id, RunScheduleInput input, Store store, Scheduler scheduler) =>
{
    if (store.GetSchedule(id) is not { } s) return Results.NotFound();
    var deployments = scheduler.Run(s, input.LaunchedBy);
    return Results.Ok(new { deployments, result = store.GetSchedule(id)?.LastResult });
});

api.MapGet("/schedules/{id:long}/history", (long id, Store store) => store.GetScheduleHistory(id));

// Efface des lignes de l'historique des cibles (toutes si la liste est vide) : ces postes seront redéployés.
api.MapPost("/schedules/{id:long}/history/clear", (long id, List<ScheduleHistoryKey>? keys, Store store) =>
{
    store.ClearScheduleHistory(id, keys);
    return Results.NoContent();
});

app.Run();

static Schedule WithNextRun(Schedule s)
{
    s.NextRunUtc = s.Enabled ? TriggerCalendar.Next(s, DateTime.Now)?.ToUniversalTime() : null;
    return s;
}

static string? ValidateCredential(CredentialInput input, bool isNew)
{
    if (string.IsNullOrWhiteSpace(input.User)) return "Saisissez le nom d'utilisateur.";
    if (input.User.Contains('\\') || input.User.Contains('@')) return "Saisissez le domaine dans sa propre case, et l'utilisateur seul.";
    if (isNew && string.IsNullOrEmpty(input.Password)) return "Saisissez le mot de passe.";
    return null;
}

static string? ValidateSchedule(Schedule s, DeployCredentials cred)
{
    if (string.IsNullOrWhiteSpace(s.Name)) return "La planification doit avoir un nom.";
    if (s.CredentialId is { } credentialId && cred.Get(credentialId) == null) return "L'identifiant choisi n'existe plus.";
    foreach (var t in s.Triggers)
    {
        if (t.Type == TriggerType.Interval && t.IntervalMinutes < 1) return "L'intervalle doit être d'au moins 1 minute.";
        if (t.Type == TriggerType.Weekly && t.Days.Count == 0) return "Déclencheur hebdomadaire : cochez au moins un jour.";
        if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start) return "La date de fin d'un déclencheur doit suivre son début.";
    }
    if (s.StopAfterFailures is < 1) return "Le nombre d'échecs doit être d'au moins 1.";
    if (s.QueueTimeoutMinutes is < 1) return "Le délai d'attente doit être d'au moins 1 minute.";
    return null;
}

static string? Validate(Package p)
{
    if (string.IsNullOrWhiteSpace(p.Name)) return "Le paquet doit avoir un nom.";
    for (var i = 0; i < p.Steps.Count; i++)
    {
        var s = p.Steps[i];
        var label = $"Étape {i + 1}";
        switch (s.Type)
        {
            case StepType.Install when string.IsNullOrWhiteSpace(s.File):
                return $"{label} : choisissez le fichier d'installation.";
            case StepType.Command or StepType.PowerShell when string.IsNullOrWhiteSpace(s.Command):
                return $"{label} : la commande est vide.";
        }
    }
    return null;
}

record CredentialInput(long? Id, string? Domain, string User, string? Description, string? Password)
{
    public Credential ToCredential(long id) => new Credential { Id = id, Domain = Domain, User = User ?? "", Description = Description }.Normalize();
}
record DeployInput(List<long> PackageIds, List<TargetRequest> Targets, string? LaunchedBy, DeployOptions? Options);
record AbortInput(List<long>? TargetIds, string? By);
record RunScheduleInput(string? LaunchedBy);
