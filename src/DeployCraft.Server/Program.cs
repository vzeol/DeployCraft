using System.Text.Json.Serialization;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Endpoints;
using DeployCraft.Server.Library;
using DeployCraft.Server.Remote;
using DeployCraft.Server.Security;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // needed when running as a Windows service
});
builder.Host.UseWindowsService(o => o.ServiceName = "DeployCraft");

// Data in C:\ProgramData\DeployCraft (SQLite database); --DataDir for a test instance.
var dataDir = builder.Configuration["DataDir"]
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DeployCraft");
DataFolder.Restrict(dataDir, LoggerFactory.Create(l => l.AddConsole()).CreateLogger("DeployCraft"));
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5080");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
var store = new Store(Path.Combine(dataDir, "deploycraft.db"));
builder.Services.AddSingleton(store.Settings);
builder.Services.AddSingleton(store.Packages);
builder.Services.AddSingleton(store.Folders);
builder.Services.AddSingleton(store.Computers);
builder.Services.AddSingleton(store.Deployments);
builder.Services.AddSingleton(store.Schedules);
builder.Services.AddSingleton(store.Credentials);
builder.Services.AddSingleton<DeployCredentials>();
builder.Services.AddSingleton<ConsoleAccess>();
builder.Services.AddSingleton<ServiceAccount>();
builder.Services.AddSingleton<ActiveDirectory>();
builder.Services.AddSingleton(new NameCheck());
builder.Services.AddSingleton<IRemoteTargets, RemoteTargets>();
builder.Services.AddSingleton<DeploymentEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeploymentEngine>());
builder.Services.AddSingleton<Scheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());
builder.Services.AddHostedService<HistoryCleaner>();
builder.Services.AddSingleton(sp => new PackageLibrary(store.Packages, store.Settings, sp.GetRequiredService<DeployCredentials>(),
    dataDir, sp.GetRequiredService<ILogger<PackageLibrary>>()));
builder.Services.AddHostedService<LibraryUpdater>();
// The console identifies itself with the person's Windows session (no password to type).
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

var app = builder.Build();

app.UseAuthentication();
var access = app.Services.GetRequiredService<ConsoleAccess>();
app.Use(access.Check);

var api = app.MapGroup("/api");
api.MapStatusEndpoints();
api.MapAccessEndpoints();
api.MapServiceEndpoints();
api.MapSettingsEndpoints();
api.MapCredentialEndpoints();
api.MapTargetEndpoints();
api.MapPackageEndpoints();
api.MapFolderEndpoints();
api.MapLibraryEndpoints();
api.MapDeploymentEndpoints();
api.MapScheduleEndpoints();

app.Run();
