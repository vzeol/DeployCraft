using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public enum MainView { Home, Deployments, Package, Schedules, Library }

/// <summary>Main navigation, access state and serialized refresh, without window controls.</summary>
public sealed class MainWindowCore
{
    private readonly Func<Task<ServerStatus>> _loadStatus;
    private readonly Func<Task<List<Deployment>>> _loadDeployments;
    private readonly Func<Task> _refreshPackages;
    private readonly Action _showStatus;
    private readonly Func<MainView, List<Deployment>, Task> _showView;
    private readonly Action _completed;
    private readonly RefreshQueue _refresh;

    public MainWindowCore(Func<Task<ServerStatus>> loadStatus, Func<Task<List<Deployment>>> loadDeployments,
        Func<Task> refreshPackages, Action showStatus, Func<MainView, List<Deployment>, Task> showView, Action completed)
    {
        _loadStatus = loadStatus;
        _loadDeployments = loadDeployments;
        _refreshPackages = refreshPackages;
        _showStatus = showStatus;
        _showView = showView;
        _completed = completed;
        _refresh = new(RefreshOnceAsync);
    }

    public MainView View { get; private set; } = MainView.Home;
    public bool Connected { get; private set; }
    public string? ServerMachine { get; private set; }
    public string ServerText { get; private set; } = "";
    public string AccountText { get; private set; } = "";
    public string BannerText { get; private set; } = "";
    public string RunningText { get; private set; } = "";
    public static TimeSpan RefreshInterval => TimeSpan.FromSeconds(2);

    public void Navigate(MainView view) => View = view;
    public bool CanUsePackage(bool selected) => Connected && selected && View == MainView.Package;
    public string HelpTopic => View switch
    {
        MainView.Deployments => "suivi",
        MainView.Package => "paquets",
        MainView.Schedules => "planifications",
        MainView.Library => "bibliotheque",
        _ => "accueil",
    };

    public Task RefreshAsync(bool packages) => _refresh.RunAsync(packages);

    private async Task RefreshOnceAsync(bool packages)
    {
        try
        {
            await RefreshStatusAsync();
            if (packages) await _refreshPackages();
            var list = await _loadDeployments();
            var running = RunningCount(list);
            RunningText = running == 0 ? L.Get("MainWindow_StatusNoneRunning") : L.Format("MainWindow_StatusRunning", running);
            await _showView(View, list);
        }
        catch (ApiException) { /* Access failures are shown in the status banner. */ }
        finally
        {
            _completed();
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _loadStatus();
            Connected = true;
            ServerMachine = status.Machine;
            ServerText = L.Format("MainWindow_StatusService", status.Machine, status.RunningAs, status.ConnectedAs);
            AccountText = status.DefaultCredential is { } credential
                ? L.Format("MainWindow_StatusDefaultCredential", credential.Account) : L.Get("MainWindow_StatusNoCredential");
            BannerText = ServerIssues.For(status);
        }
        catch (ApiException ex)
        {
            Connected = false;
            ServerText = L.Get(ex.AccessDenied ? "MainWindow_StatusAccessDenied" : "MainWindow_StatusUnreachable");
            AccountText = "";
            BannerText = ex.Message;
            _showStatus();
            throw;
        }
        _showStatus();
    }

    public static int RunningCount(IEnumerable<Deployment> list) =>
        list.Count(d => d.Status is DeploymentStatus.Running or DeploymentStatus.Queued);
}
