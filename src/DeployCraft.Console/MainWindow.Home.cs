using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Home page: running deployments, recent failures, next schedules, library, shortcuts.</summary>
public partial class MainWindow
{
    private async Task RefreshHomeAsync(List<Deployment> deployments)
    {
        var summary = HomeSummary.From(deployments, await Api.Schedules(), DateTime.UtcNow);

        HomeRunning.Text = summary.RunningDeployments.ToString();
        HomeRunningText.Text = summary.RunningDeployments switch
        {
            0 => L.Get("MainWindow_HomeNoneRunning"),
            1 => L.Get("MainWindow_HomeOneRunning"),
            _ => L.Get("MainWindow_HomeManyRunning"),
        };
        HomeTargetsText.Text = summary.RunningDeployments == 0 ? L.Get("MainWindow_HomeAllQuiet")
            : L.Format("MainWindow_HomeTargets", Plural(summary.RunningTargets, "MainWindow_ComputerOne", "MainWindow_ComputerMany"), Plural(summary.QueuedTargets, "MainWindow_ComputerOne", "MainWindow_ComputerMany"));

        HomeSucceeded.Text = summary.SucceededToday.ToString();
        HomeFailed.Text = summary.FailedToday.ToString();
        HomeFailures.Children.Clear();
        foreach (var d in summary.RecentFailures)
            HomeFailures.Children.Add(Line(Ui.StatusIcon("Failed"), "KoBrush", $"{d.PackageName}",
                L.Format("MainWindow_HomeDeploymentLine", d.Id, Ui.LocalTime(d.EndedUtc)), () => ShowDeployment(d.Id)));

        HomeSchedules.Children.Clear();
        if (summary.NextSchedules.Count == 0)
            HomeSchedules.Children.Add(new TextBlock { Text = L.Get("MainWindow_HomeNoUpcoming"), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        foreach (var s in summary.NextSchedules)
            HomeSchedules.Children.Add(Line(Ui.IconSchedule, "Accent", s.Name, Ui.LocalTime(s.NextRunUtc), () => _ = ShowSchedule(s.Id)));

        try
        {
            var library = await Api.LibraryStatus();
            ShowLibraryUpdates(library);
            var updates = library.Items.Count(i => i.UpdateAvailable);
            var downloaded = library.Items.Count(i => i.PackageId != null);
            HomeLibraryText.Text = downloaded == 0 ? L.Get("MainWindow_HomeLibraryNone")
                : updates == 0 ? L.Format("MainWindow_HomeLibraryUpToDate", Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"))
                : updates == 1 ? L.Format("MainWindow_HomeLibraryUpdateOne", updates, Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"))
                : L.Format("MainWindow_HomeLibraryUpdateMany", updates, Plural(downloaded, "MainWindow_PackageOne", "MainWindow_PackageMany"));
        }
        catch (ApiException)
        {
            HomeLibraryText.Text = L.Get("MainWindow_HomeLibraryUnavailable");
        }
    }

    private static string Plural(int count, string oneKey, string manyKey) => L.Format(count > 1 ? manyKey : oneKey, count);

    /// <summary>Clickable row of a card: icon, title (link), dimmed detail.</summary>
    private TextBlock Line(string icon, string brush, string title, string detail, Action open)
    {
        var link = new Hyperlink(new Run(title));
        link.Click += (_, _) => open();
        var line = new TextBlock { Margin = new Thickness(0, 2, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        line.Inlines.Add(new Run(icon + "  ") { FontFamily = Ui.IconFont, Foreground = (System.Windows.Media.Brush)FindResource(brush) });
        line.Inlines.Add(link);
        line.Inlines.Add(new Run("   " + detail) { Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        return line;
    }

    private void ShowDeployment(long id)
    {
        _deploymentsNode.IsSelected = true;
        AllDeployments.Select(id);
    }

    private void HomeDeployments_Click(object sender, RoutedEventArgs e) => _deploymentsNode.IsSelected = true;

    private void HomeLibrary_Click(object sender, RoutedEventArgs e) => _libraryNode.IsSelected = true;
}
