using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Home page: running deployments, recent failures, next schedules, library, shortcuts.</summary>
public partial class MainWindow
{
    private readonly HomePage _home = new(Api.Schedules, Api.LibraryStatus, () => DateTime.UtcNow, Ui.LocalTime);

    private async Task RefreshHomeAsync(List<Deployment> deployments) =>
        HomeLibraryText.Text = await _home.RefreshAsync(deployments, ShowHomeOverview, ShowLibraryUpdates);

    private void ShowHomeOverview(HomeOverview overview)
    {
        HomeRunning.Text = overview.RunningDeployments.ToString();
        HomeRunningText.Text = overview.RunningText;
        HomeTargetsText.Text = overview.TargetsText;
        HomeSucceeded.Text = overview.SucceededToday.ToString();
        HomeFailed.Text = overview.FailedToday.ToString();
        HomeFailures.Children.Clear();
        foreach (var row in overview.RecentFailures)
            HomeFailures.Children.Add(Line(Ui.StatusIcon("Failed"), "KoBrush", row.Title, row.Detail, () => ShowDeployment(row.Id)));

        HomeSchedules.Children.Clear();
        if (overview.NoUpcomingText is { } noUpcoming)
            HomeSchedules.Children.Add(new TextBlock { Text = noUpcoming, Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        foreach (var row in overview.NextSchedules)
            HomeSchedules.Children.Add(Line(Ui.IconSchedule, "Accent", row.Title, row.Detail, () => _ = ShowSchedule(row.Id)));
    }

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
