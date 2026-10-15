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

// ---------- Préférences de déploiement ----------

api.MapGet("/settings/defaults", (Store store) => store.GetDefaults());

api.MapPut("/settings/defaults", (DeploymentDefaults defaults, Store store) =>
{
    defaults.Offline ??= new();
    if (defaults.TimeoutMinutes < 1) return Results.BadRequest(new { error = "Le délai max doit être d'au moins 1 minute." });
    if (ValidateOffline(defaults.Offline) is { } error) return Results.BadRequest(new { error = $"Hors ligne : {error}" });
    store.SaveDefaults(defaults);
    return Results.Ok(defaults);
});

// ---------- Variables ----------

api.MapGet("/variables", (Store store) => store.ListVariables());

// La page enregistre toutes les variables d'un bloc.
api.MapPut("/variables", (List<Variable> variables, Store store) =>
{
    foreach (var v in variables)
    {
        v.Name = (v.Name ?? "").Trim();
        v.Value ??= "";
    }
    if (VariableText.Validate(variables) is { } error) return Results.BadRequest(new { error });
    store.SaveVariables(variables);
    return Results.Ok(store.ListVariables());
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
// Un compte local est propre à chaque poste : on l'essaie sur le poste indiqué.
api.MapPost("/credentials/test", (CredentialInput input, DeployCredentials cred) =>
{
    var account = input.ToCredential(input.Id ?? 0);
    var computer = input.Computer?.Trim();
    if (account.IsLocal && string.IsNullOrEmpty(computer))
        return Results.BadRequest(new { error = "Indiquez le poste sur lequel essayer ce compte local." });
    try
    {
        var password = input.Password;
        if (string.IsNullOrEmpty(password) && input.Id is { } id && cred.Get(id) is { } stored) password = cred.Password(stored);
        if (string.IsNullOrEmpty(password)) return Results.BadRequest(new { error = "Saisissez le mot de passe." });
        if (account.IsLocal) DeployCredentials.TestOnComputer(account, password, computer!);
        else ActiveDirectory.TestConnection(account, password);
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
    p.Id = 0;
    if (Validate(p, store) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(store.SavePackage(p));
});

api.MapPut("/packages/{id:long}", (long id, Package p, Store store) =>
{
    if (store.GetPackage(id) == null) return Results.NotFound();
    p.Id = id;
    if (Validate(p, store) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(store.SavePackage(p));
});

api.MapDelete("/packages/{id:long}", (long id, Store store) =>
{
    if (Package.UsersOf(store.ListPackages(), id) is { Count: > 0 } users)
        return Results.BadRequest(new
        {
            error = "Ce paquet est utilisé comme paquet imbriqué par : " + string.Join(", ", users.Select(u => $"« {u.Name} »")) +
                    ". Retirez-le de ces paquets avant de le supprimer.",
        });
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

static string? Validate(Package p, Store store)
{
    // Un client qui envoie null obtient « aucune condition ».
    p.Conditions ??= new();
    foreach (var step in p.Steps)
    {
        step.Conditions ??= new();
        step.ExtraConditions = null; // posées seulement au lancement, à partir des paquets imbriqués
    }
    p.SortSteps();
    if (string.IsNullOrWhiteSpace(p.Name)) return "Le paquet doit avoir un nom.";
    if (p.TimeoutMinutes is < 1) return "Options : le délai max doit être d'au moins 1 minute.";
    if (ValidateConditions(p.Conditions) is { } conditionError) return $"Conditions du paquet : {conditionError}";
    // Paquets imbriqués : choisis, existants, sans boucle ni trop de niveaux (le paquet enregistré compte pour lui-même).
    try
    {
        p.Flatten(id => id == p.Id ? p : store.GetPackage(id));
    }
    catch (ArgumentException ex)
    {
        return ex.Message;
    }
    if (p.Offline != null && ValidateOffline(p.Offline) is { } offlineError) return $"Hors ligne : {offlineError}";
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
            case StepType.Pause when s.PauseSeconds is < 1 or > 86400:
                return $"{label} : la pause doit durer de 1 seconde à 24 heures (86 400 secondes).";
        }
        if (s.TimeoutMinutes is < 1) return $"{label} : le délai max doit être d'au moins 1 minute.";
        if (ValidateConditions(s.Conditions) is { } error) return $"{label}, conditions : {error}";
    }
    return null;
}

static string? ValidateConditions(Conditions c)
{
    if (c.File != PresenceCondition.None && string.IsNullOrWhiteSpace(c.FilePath)) return "indiquez le chemin du fichier.";
    if (c.Registry != PresenceCondition.None)
    {
        if (string.IsNullOrWhiteSpace(c.RegistryKey)) return "indiquez la clé de registre.";
        var hive = c.RegistryKey.Trim().Split('\\')[0].ToUpperInvariant();
        if (hive is not ("HKLM" or "HKEY_LOCAL_MACHINE" or "HKCU" or "HKEY_CURRENT_USER" or "HKCR" or "HKEY_CLASSES_ROOT" or "HKU" or "HKEY_USERS"))
            return "la clé de registre doit commencer par HKLM, HKCU, HKCR ou HKU.";
    }
    return null;
}

static string? ValidateOffline(OfflineSettings o)
{
    if (o.WakeOnLan && o.WakeWaitMinutes < 1) return "l'attente après le réveil doit être d'au moins 1 minute.";
    if (o.Retry && o.RetryAttempts < 2) return "il faut au moins 2 essais pour réessayer.";
    if (o.Retry && o.RetryIntervalMinutes < 1) return "l'intervalle entre deux essais doit être d'au moins 1 minute.";
    return null;
}

record CredentialInput(long? Id, string? Domain, string User, string? Description, string? Password, string? Computer = null)
{
    public Credential ToCredential(long id) => new Credential { Id = id, Domain = Domain, User = User ?? "", Description = Description }.Normalize();
}
record DeployInput(List<long> PackageIds, List<TargetRequest> Targets, string? LaunchedBy, DeployOptions? Options);
record AbortInput(List<long>? TargetIds, string? By);
record RunScheduleInput(string? LaunchedBy);
