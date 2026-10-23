using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class MainWindowCoreTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    private static readonly ServerStatus Healthy = new("SERVEUR-01", "SYSTEM", true,
        new Credential { User = "admin" }, 0, "1.0.0", "admin");

    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private static MainWindowCore Core(Func<Task<ServerStatus>>? status = null,
        Func<Task<List<Deployment>>>? deployments = null, Func<Task>? packages = null,
        Action? showStatus = null, Func<MainView, List<Deployment>, Task>? showView = null, Action? completed = null) =>
        new(status ?? (() => Task.FromResult(Healthy)), deployments ?? (() => Task.FromResult(new List<Deployment>())),
            packages ?? (() => Task.CompletedTask), showStatus ?? (() => { }),
            showView ?? ((_, _) => Task.CompletedTask), completed ?? (() => { }));

    [Theory]
    [InlineData(MainView.Home, "accueil")]
    [InlineData(MainView.Deployments, "suivi")]
    [InlineData(MainView.Package, "paquets")]
    [InlineData(MainView.Schedules, "planifications")]
    [InlineData(MainView.Library, "bibliotheque")]
    public async Task Navigation_ControlsHelpAndPackageCommands(MainView view, string topic)
    {
        var core = Core();
        Assert.Equal(MainView.Home, core.View);
        Assert.False(core.Connected);
        core.Navigate(view);
        Assert.False(core.CanUsePackage(true));

        await core.RefreshAsync(false);

        Assert.Equal(view, core.View);
        Assert.Equal(topic, core.HelpTopic);
        Assert.Equal(view == MainView.Package, core.CanUsePackage(true));
        Assert.False(core.CanUsePackage(false));
    }

    [Theory]
    [InlineData("fr", "Service : SERVEUR-01 (SYSTEM) — vous : admin", "Identifiant par défaut : .\\admin", "2 déploiement(s) en cours")]
    [InlineData("en", "Service: SERVEUR-01 (SYSTEM) — you: admin", "Default credential: .\\admin", "2 deployment(s) running")]
    public async Task Refresh_PreservesOrderStatusAndRunningCounts(string language, string service, string account, string running)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var events = new List<string>();
        List<Deployment> list = [new() { Status = DeploymentStatus.Running }, new() { Status = DeploymentStatus.Queued },
            new() { Status = DeploymentStatus.Successful }, new() { Status = DeploymentStatus.Failed }];
        var core = Core(
            status: () => { events.Add("status"); return Task.FromResult(Healthy); },
            deployments: () => { events.Add("deployments"); return Task.FromResult(list); },
            packages: () => { events.Add("packages"); return Task.CompletedTask; },
            showStatus: () => events.Add("show status"),
            showView: (view, shown) => { events.Add("show view"); Assert.Equal(MainView.Home, view); Assert.Same(list, shown); return Task.CompletedTask; },
            completed: () => events.Add("buttons"));

        await core.RefreshAsync(true);

        Assert.Equal(["status", "show status", "packages", "deployments", "show view", "buttons"], events);
        Assert.Equal(service, core.ServerText);
        Assert.Equal(account, core.AccountText);
        Assert.Equal(running, core.RunningText);
        Assert.Equal("SERVEUR-01", core.ServerMachine);
        Assert.Empty(core.BannerText);
    }

    [Theory]
    [InlineData("fr", false, "Service injoignable")]
    [InlineData("fr", true, "Accès refusé")]
    [InlineData("en", false, "Service unreachable")]
    [InlineData("en", true, "Access denied")]
    public async Task AccessFailure_StopsRefreshAndRecoveryRestoresCommands(string language, bool denied, string text)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var fail = false;
        var calls = 0;
        var completed = 0;
        var core = Core(status: () => fail ? Task.FromException<ServerStatus>(new ApiException("Unavailable", denied)) : Task.FromResult(Healthy),
            deployments: () => { calls++; return Task.FromResult(new List<Deployment>()); }, completed: () => completed++);
        core.Navigate(MainView.Package);
        await core.RefreshAsync(false);
        fail = true;

        await core.RefreshAsync(true);

        Assert.False(core.Connected);
        Assert.False(core.CanUsePackage(true));
        Assert.Equal(text, core.ServerText);
        Assert.Empty(core.AccountText);
        Assert.Equal("Unavailable", core.BannerText);
        Assert.Equal("SERVEUR-01", core.ServerMachine);
        Assert.Equal(1, calls);
        Assert.Equal(2, completed);

        fail = false;
        await core.RefreshAsync(false);
        Assert.True(core.CanUsePackage(true));
        Assert.Empty(core.BannerText);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("fr", "Aucun identifiant", "Aucun déploiement en cours")]
    [InlineData("en", "No credential", "No deployment running")]
    public async Task ServiceWarnings_DoNotDisableCommands(string language, string account, string running)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var status = Healthy with { Elevated = false, DefaultCredential = null, UnreadableCredentials = 1 };
        var core = Core(status: () => Task.FromResult(status));

        await core.RefreshAsync(false);

        Assert.True(core.Connected);
        Assert.Equal(account, core.AccountText);
        Assert.Equal(running, core.RunningText);
        Assert.Equal(ServerIssues.For(status), core.BannerText);
        Assert.Equal(3, core.BannerText.Split(Environment.NewLine).Length);
    }

    [Fact]
    public async Task QueuedRefresh_CoalescesPackageRequestsAndUsesLatestNavigation()
    {
        var gate = new TaskCompletionSource<ServerStatus>();
        var calls = 0;
        var packageCalls = 0;
        var views = new List<MainView>();
        var core = Core(status: () => ++calls == 1 ? gate.Task : Task.FromResult(Healthy),
            packages: () => { packageCalls++; return Task.CompletedTask; },
            showView: (view, _) => { views.Add(view); return Task.CompletedTask; });
        var refresh = core.RefreshAsync(false);
        await core.RefreshAsync(false);
        await core.RefreshAsync(true);
        core.Navigate(MainView.Library);
        gate.SetResult(Healthy);
        await refresh;

        Assert.Equal(2, calls);
        Assert.Equal(1, packageCalls);
        Assert.Equal([MainView.Library, MainView.Library], views);
        Assert.Equal(TimeSpan.FromSeconds(2), MainWindowCore.RefreshInterval);
    }

    [Theory]
    [InlineData("packages")]
    [InlineData("deployments")]
    [InlineData("view")]
    public async Task LaterApiFailure_PreservesConnectionAndStillUpdatesButtons(string stage)
    {
        var events = new List<string>();
        Task Step(string name)
        {
            events.Add(name);
            return stage == name ? Task.FromException(new ApiException("Unavailable")) : Task.CompletedTask;
        }
        var core = Core(packages: () => Step("packages"), deployments: async () => { await Step("deployments"); return []; },
            showView: (_, _) => Step("view"), completed: () => events.Add("buttons"));

        await core.RefreshAsync(true);

        Assert.True(core.Connected);
        Assert.Empty(core.BannerText);
        Assert.Equal("buttons", events.Last());
        Assert.Equal(stage == "packages" ? 2 : stage == "deployments" ? 3 : 4, events.Count);
    }

    [Fact]
    public async Task UnexpectedFailure_PropagatesAndQueueCanRefreshAgain()
    {
        var error = new InvalidOperationException("Unexpected failure");
        var fail = true;
        var completed = 0;
        var core = Core(status: () => fail ? Task.FromException<ServerStatus>(error) : Task.FromResult(Healthy),
            completed: () => completed++);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => core.RefreshAsync(false)));
        fail = false;
        await core.RefreshAsync(false);
        Assert.True(core.Connected);
        Assert.Equal(2, completed);
    }
}
