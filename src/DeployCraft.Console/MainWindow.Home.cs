using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Page d'accueil : déploiements en cours, échecs récents, prochaines planifications, bibliothèque, raccourcis.</summary>
public partial class MainWindow
{
    private async Task RefreshHomeAsync(List<Deployment> deployments)
    {
        var summary = HomeSummary.From(deployments, await Api.Schedules(), DateTime.UtcNow);

        HomeRunning.Text = summary.RunningDeployments.ToString();
        HomeRunningText.Text = summary.RunningDeployments switch
        {
            0 => "aucun déploiement en cours",
            1 => "déploiement en cours",
            _ => "déploiements en cours",
        };
        HomeTargetsText.Text = summary.RunningDeployments == 0 ? "Tout est calme."
            : $"{Plural(summary.RunningTargets, "poste")} en cours, {Plural(summary.QueuedTargets, "poste")} en attente";

        HomeSucceeded.Text = summary.SucceededToday.ToString();
        HomeFailed.Text = summary.FailedToday.ToString();
        HomeFailures.Children.Clear();
        foreach (var d in summary.RecentFailures)
            HomeFailures.Children.Add(Line(Ui.StatusIcon("Failed"), "KoBrush", $"{d.PackageName}",
                $"n° {d.Id} · {Ui.LocalTime(d.EndedUtc)}", () => ShowDeployment(d.Id)));

        HomeSchedules.Children.Clear();
        if (summary.NextSchedules.Count == 0)
            HomeSchedules.Children.Add(new TextBlock { Text = "Aucune planification à venir.", Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        foreach (var s in summary.NextSchedules)
            HomeSchedules.Children.Add(Line(Ui.IconSchedule, "Accent", s.Name, Ui.LocalTime(s.NextRunUtc), () => _ = ShowSchedule(s.Id)));

        try
        {
            var library = await Api.LibraryStatus();
            var updates = library.Items.Count(i => i.UpdateAvailable);
            var downloaded = library.Items.Count(i => i.PackageId != null);
            HomeLibraryText.Text = downloaded == 0 ? "Aucun paquet téléchargé depuis la bibliothèque pour l'instant."
                : updates == 0 ? $"{Plural(downloaded, "paquet")} de la bibliothèque, tous à jour."
                : $"{Plural(updates, "mise")} à jour disponible{(updates > 1 ? "s" : "")} sur {Plural(downloaded, "paquet")} de la bibliothèque.";
        }
        catch (ApiException)
        {
            HomeLibraryText.Text = "État de la bibliothèque indisponible.";
        }
    }

    private static string Plural(int count, string word) => $"{count} {word}{(count > 1 ? "s" : "")}";

    /// <summary>Ligne cliquable d'une carte : icône, titre (lien), détail atténué.</summary>
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
