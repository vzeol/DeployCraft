using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Update selection, installation eligibility and proposal texts without dialogs or processes.</summary>
public sealed class MainWindowUpdates(Func<Task<UpdateInfo?>> find, Func<Task<List<Deployment>>> loadDeployments,
    Func<string?> serverMachine, string localMachine, string? currentVersion)
{
    public UpdateInfo? Update { get; private set; }
    public bool Installing { get; private set; }
    public bool Visible { get; private set; }
    public bool CanInstallHere(UpdateInfo update) => update.SetupUrl != null &&
        string.Equals(serverMachine(), localMachine, StringComparison.OrdinalIgnoreCase);
    public bool CanPropose(UpdateInfo update) => CanInstallHere(update) && !Installing;
    public bool ShowInstall => Update is { } update && CanPropose(update);
    public string AvailableText => Update is { } update
        ? L.Format("MainWindow_UpdateAvailable", update.Version, currentVersion) +
          (CanInstallHere(update) ? "" : " " + L.Get("MainWindow_UpdateInstallOnServer")) : "";
    public string NoneFoundText => L.Format("MainWindow_UpdateNoneFound", currentVersion);

    public async Task<UpdateInfo?> CheckAsync()
    {
        Update = await find();
        Visible = Update != null;
        return Update;
    }

    public async Task<string?> ProposalAsync(UpdateInfo update)
    {
        if (!CanPropose(update)) return null;
        var running = 0;
        try
        {
            running = MainWindowCore.RunningCount(await loadDeployments());
        }
        catch (ApiException) { /* The proposal remains available when deployment status cannot be read. */ }
        return L.Format("MainWindow_UpdateProposal", update.Version, currentVersion) +
            (running > 0 ? "\n\n" + L.Format("MainWindow_UpdateRunningWarning", running) : "");
    }

    public void BeginInstall(UpdateInfo update)
    {
        Installing = true;
        Update = update;
        Visible = true;
    }

    public void InstallFailed(UpdateInfo update)
    {
        Installing = false;
        Update = update;
        Visible = true;
    }
    public void Hide() => Visible = false;
    public static string DownloadText(UpdateInfo update, int progress) => L.Format("MainWindow_UpdateDownloading", update.Version, progress);
    public static string DownloadFailedText(Exception error) => L.Format("MainWindow_UpdateDownloadFailed", error.Message);
}
