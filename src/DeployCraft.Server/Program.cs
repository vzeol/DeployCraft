using System.Security.Principal;
using System.Text.Json.Serialization;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Library;
using DeployCraft.Server.Remote;
using DeployCraft.Server.Security;
using Microsoft.AspNetCore.Authentication.Negotiate;

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
builder.Services.AddSingleton<ConsoleAccess>();
builder.Services.AddSingleton<ActiveDirectory>();
builder.Services.AddSingleton(new NameCheck());
builder.Services.AddSingleton<DeploymentEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeploymentEngine>());
builder.Services.AddSingleton<Scheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());
builder.Services.AddHostedService<HistoryCleaner>();
builder.Services.AddSingleton(sp => new PackageLibrary(sp.GetRequiredService<Store>(), sp.GetRequiredService<DeployCredentials>(),
    dataDir, sp.GetRequiredService<ILogger<PackageLibrary>>()));
builder.Services.AddHostedService<LibraryUpdater>();
// La console s'identifie avec la session Windows de la personne (aucun mot de passe à saisir).
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

var app = builder.Build();

app.UseAuthentication();
var access = app.Services.GetRequiredService<ConsoleAccess>();
app.Use(access.Check);

var api = app.MapGroup("/api");

// ---------- État ----------

api.MapGet("/status", (HttpContext context, DeployCredentials cred) =>
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
        connectedAs = context.User.Identity?.Name,
    };
});

// ---------- Utilisateurs de la console ----------

api.MapGet("/access", (HttpContext context, ConsoleAccess access) => new
{
    users = access.ListUsers(),
    sessions = access.ActiveSessions(),
    canEdit = ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User)),
});

api.MapPost("/access/users", (HttpContext context, ConsoleUserInput input, ConsoleAccess access) =>
{
    if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return OnlyAdmins();
    var (user, error) = access.AddUser(input.Name);
    return user != null ? Results.Ok(user) : Results.BadRequest(new { error });
});

api.MapDelete("/access/users/{sid}", (HttpContext context, string sid, ConsoleAccess access) =>
{
    if (!ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(context.User))) return OnlyAdmins();
    access.RemoveUser(sid);
    return Results.NoContent();
});

// ---------- Préférences de déploiement ----------

api.MapGet("/settings/defaults", (Store store) => store.GetDefaults());

api.MapPut("/settings/defaults", (DeploymentDefaults defaults, Store store, DeploymentEngine engine) =>
{
    defaults.Offline ??= new();
    defaults.History ??= new();
    defaults.Performance ??= new();
    if (defaults.TimeoutMinutes < 1) return Results.BadRequest(new { error = "Le délai max doit être d'au moins 1 minute." });
    if (ValidateOffline(defaults.Offline) is { } error) return Results.BadRequest(new { error = $"Hors ligne : {error}" });
    if (HistorySettings.Validate(defaults.History.KeepDays) is { } historyError) return Results.BadRequest(new { error = $"Historique : {historyError}" });
    if (PerformanceSettings.Validate(defaults.Performance) is { } performanceError) return Results.BadRequest(new { error = $"Performance : {performanceError}" });
    store.SaveDefaults(defaults);
    engine.ApplyPerformance(defaults.Performance);
    return Results.Ok(defaults);
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
    if (p.FolderId is { } folder && store.ListFolders().All(f => f.Id != folder)) p.FolderId = null;
    if (Validate(p, store.GetPackage) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(store.SavePackage(p));
});

api.MapPut("/packages/{id:long}", (long id, Package p, Store store) =>
{
    if (store.GetPackage(id) == null) return Results.NotFound();
    p.Id = id;
    if (Validate(p, store.GetPackage) is { } error) return Results.BadRequest(new { error });
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

// Export : les paquets choisis et les paquets imbriqués qu'ils utilisent, dans un fichier qui se suffit à lui-même.
// Avertissements de la fenêtre du paquet : chemins introuvables depuis le serveur, lus comme pour un déploiement
// (partage ouvert avec l'identifiant par défaut). Une vérification impossible (identifiant refusé…) ne signale rien.
api.MapPost("/files/missing", (List<string> paths, DeployCredentials cred) =>
{
    var account = cred.Default;
    return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Where(path =>
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            var host = expanded.StartsWith(@"\\") ? expanded[2..].Split('\\')[0] : null;
            return !cred.RunAs(account, host, () => File.Exists(expanded) || Directory.Exists(expanded));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return false;
        }
    }).ToList();
});

// « Déployer une fois » : chaque poste ajouté est cherché dans le DNS du serveur (coche ou croix dans la liste).
api.MapPost("/names/check", (List<string> names, NameCheck check, CancellationToken ct) => check.CheckAsync(names.Take(1000), ct));

// ---------- Dossiers de paquets ----------

api.MapGet("/folders", (Store store) => store.ListFolders());

api.MapPost("/folders", (PackageFolder f, Store store) =>
{
    f.Id = 0;
    if (PackageFolder.Check(f, store.ListFolders()) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(store.SaveFolder(f));
});

api.MapPut("/folders/{id:long}", (long id, PackageFolder f, Store store) =>
{
    var all = store.ListFolders();
    if (all.All(x => x.Id != id)) return Results.NotFound();
    f.Id = id;
    if (PackageFolder.Check(f, all) is { } error) return Results.BadRequest(new { error });
    return Results.Ok(store.SaveFolder(f));
});

api.MapDelete("/folders/{id:long}", (long id, Store store) =>
{
    if (PackageFolder.CheckDelete(id, store.ListFolders(), store.ListPackages()) is { } error) return Results.BadRequest(new { error });
    store.DeleteFolder(id);
    return Results.NoContent();
});

api.MapPut("/packages/{id:long}/folder", (long id, PackageMove move, Store store) =>
{
    if (store.GetPackage(id) == null) return Results.NotFound();
    if (move.FolderId is { } folder && store.ListFolders().All(f => f.Id != folder))
        return Results.BadRequest(new { error = "Dossier introuvable." });
    store.MovePackage(id, move.FolderId);
    return Results.NoContent();
});

api.MapPost("/packages/export", (List<long> ids, Store store) =>
{
    try
    {
        return Results.Ok(PackageExchange.Export(ids, store.GetPackage));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Import d'un fichier de paquets (.json ou .xml) : aperçu (Apply = false) ou création des paquets.
api.MapPost("/packages/import", (ImportInput input, Store store) =>
{
    ImportReport report;
    try
    {
        report = PackageExchange.Read(input.Content ?? "", input.Repository);
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    PackageExchange.Complete(report, store.ListPackages(), Validate, input.Apply ? store.SavePackage : null);
    return Results.Ok(report);
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

// ---------- Bibliothèque de paquets ----------

api.MapGet("/library", (string? search, PackageLibrary library) => library.Info(search));

api.MapGet("/library/status", (PackageLibrary library) => library.Status());

api.MapPost("/library/refresh", (PackageLibrary library) =>
{
    library.StartRefresh();
    return Results.NoContent();
});

api.MapPut("/library/folder", (LibraryFolderInput input, PackageLibrary library) =>
    library.SetFolder(input.Folder) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

api.MapPut("/library/auto-update", (LibraryAutoUpdateInput input, PackageLibrary library) =>
    library.SetAutoUpdate(input.Mode, input.Days) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

// La fiche du logiciel est lue tout de suite (refus expliqué) ; le téléchargement continue en arrière-plan.
api.MapPost("/library/download", async (LibraryDownloadInput input, PackageLibrary library) =>
    await library.StartDownloadAsync(input.Id ?? "") is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

// ---------- Déploiements ----------

// Un déploiement par paquet, dans l'ordre : la réponse est la liste des déploiements créés.
api.MapPost("/deployments", (HttpContext context, DeployInput input, Store store, DeploymentEngine engine) =>
{
    var packages = new List<Package>();
    foreach (var id in input.PackageIds)
    {
        if (store.GetPackage(id) is not { } package) return Results.NotFound(new { error = $"Paquet introuvable (n° {id})." });
        packages.Add(package);
    }
    try
    {
        return Results.Ok(engine.Start(packages, input.Targets, context.User.Identity?.Name, input.Options ?? new DeployOptions()));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/deployments", (Store store) => store.ListDeployments());

api.MapGet("/deployments/{id:long}", (long id, Store store) =>
    store.GetDeployment(id) is { } d ? Results.Ok(d) : Results.NotFound());

api.MapPost("/deployments/{id:long}/abort", (HttpContext context, long id, AbortInput input, DeploymentEngine engine) =>
    Results.Ok(new { aborted = engine.Abort(id, input.TargetIds, context.User.Identity?.Name) }));

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
api.MapPost("/schedules/{id:long}/run", (HttpContext context, long id, Store store, Scheduler scheduler) =>
{
    if (store.GetSchedule(id) is not { } s) return Results.NotFound();
    var deployments = scheduler.Run(s, context.User.Identity?.Name);
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

static IResult OnlyAdmins() => Results.Json(
    new { error = "Seuls les administrateurs de ce serveur peuvent modifier la liste des utilisateurs de la console." },
    statusCode: StatusCodes.Status403Forbidden);

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

static string? Validate(Package p, Func<long, Package?> lookup)
{
    // Un client qui envoie null obtient « aucune condition ».
    p.Conditions ??= new();
    foreach (var step in p.Steps)
    {
        step.Conditions ??= new();
        step.ExtraConditions = null; // posées seulement au lancement, à partir des paquets imbriqués
    }
    if (string.IsNullOrWhiteSpace(p.Name)) return "Le paquet doit avoir un nom.";
    if (p.TimeoutMinutes is < 1) return "Options : le délai max doit être d'au moins 1 minute.";
    if (ValidateConditions(p.Conditions) is { } conditionError) return $"Conditions du paquet : {conditionError}";
    // Paquets imbriqués : choisis, existants, sans boucle ni trop de niveaux (le paquet enregistré compte pour lui-même).
    try
    {
        p.Flatten(id => id == p.Id ? p : lookup(id));
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
            case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopySource):
                return $"{label} : choisissez le fichier ou le dossier à copier.";
            case StepType.CopyFiles when string.IsNullOrWhiteSpace(s.CopyDestination):
                return $"{label} : indiquez le dossier de destination sur le poste.";
            case StepType.Reboot when s.RebootDelaySeconds is < 30 or > 3600:
                return $"{label} : le délai avant le redémarrage doit être de 30 secondes à 1 heure (3 600 secondes).";
            case StepType.Reboot when s.RebootMessage?.Length > 500:
                return $"{label} : le message ne doit pas dépasser 500 caractères.";
            case StepType.Message when string.IsNullOrWhiteSpace(s.MessageText):
                return $"{label} : le message est vide.";
            case StepType.Message when s.MessageText!.Length > 2000 || s.MessageTitle?.Length > 100:
                return $"{label} : 2 000 caractères au plus pour le message, 100 pour le titre.";
            case StepType.Message when s.MessageSeconds is < 0 or > 86400:
                return $"{label} : la durée d'affichage doit aller de 0 (jusqu'au clic) à 24 heures (86 400 secondes).";
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
record DeployInput(List<long> PackageIds, List<TargetRequest> Targets, DeployOptions? Options);
record AbortInput(List<long>? TargetIds);
record ConsoleUserInput(string? Name);
record ImportInput(string? Content, string? Repository, bool Apply);
record LibraryFolderInput(string? Folder);
record LibraryAutoUpdateInput(LibraryAutoUpdate Mode, int Days);
record LibraryDownloadInput(string? Id);
record PackageMove(long? FolderId);
