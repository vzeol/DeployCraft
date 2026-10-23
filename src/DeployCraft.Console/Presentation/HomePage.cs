using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Home loading and presentation decisions, without window controls or a real API.</summary>
public sealed class HomePage(Func<Task<List<Schedule>>> loadSchedules, Func<Task<LibraryInfo>> loadLibrary,
    Func<DateTime> utcNow, Func<DateTime?, string> localTime)
{
    public async Task<string> RefreshAsync(IReadOnlyCollection<Deployment> deployments,
        Action<HomeOverview> showOverview, Action<LibraryInfo> showLibraryUpdates)
    {
        var summary = HomeSummary.From(deployments, await loadSchedules(), utcNow());
        showOverview(Overview(summary));

        try
        {
            var library = await loadLibrary();
            showLibraryUpdates(library);
            return LibraryText(library);
        }
        catch (ApiException)
        {
            return L.Get("MainWindow_HomeLibraryUnavailable");
        }
    }

    private HomeOverview Overview(HomeSummary summary) => new(
        summary.RunningDeployments,
        summary.SucceededToday,
        summary.FailedToday,
        summary.RunningDeployments switch
        {
            0 => L.Get("MainWindow_HomeNoneRunning"),
            1 => L.Get("MainWindow_HomeOneRunning"),
            _ => L.Get("MainWindow_HomeManyRunning"),
        },
        summary.RunningDeployments == 0 ? L.Get("MainWindow_HomeAllQuiet")
            : L.Format("MainWindow_HomeTargets", Plural(summary.RunningTargets, "MainWindow_ComputerOne", "MainWindow_ComputerMany"),
                Plural(summary.QueuedTargets, "MainWindow_ComputerOne", "MainWindow_ComputerMany")),
        [.. summary.RecentFailures.Select(d => new HomeLink(d.Id, d.PackageName,
            L.Format("MainWindow_HomeDeploymentLine", d.Id, localTime(d.EndedUtc))))],
        [.. summary.NextSchedules.Select(s => new HomeLink(s.Id, s.Name, localTime(s.NextRunUtc)))],
        summary.NextSchedules.Count == 0 ? L.Get("MainWindow_HomeNoUpcoming") : null);

    private static string LibraryText(LibraryInfo library)
    {
        var updates = library.Items.Count(i => i.UpdateAvailable);
        var downloaded = library.Items.Count(i => i.PackageId != null);
        return downloaded == 0 ? L.Get("MainWindow_HomeLibraryNone")
            : updates == 0 ? L.Format("MainWindow_HomeLibraryUpToDate", Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"))
            : updates == 1 ? L.Format("MainWindow_HomeLibraryUpdateOne", updates, Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"))
            : L.Format("MainWindow_HomeLibraryUpdateMany", updates, Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"));
    }

    private static string Plural(int count, string oneKey, string manyKey) => L.Format(count > 1 ? manyKey : oneKey, count);
}

public record HomeOverview(int RunningDeployments, int SucceededToday, int FailedToday, string RunningText,
    string TargetsText, List<HomeLink> RecentFailures, List<HomeLink> NextSchedules, string? NoUpcomingText);

public record HomeLink(long Id, string Title, string Detail);
