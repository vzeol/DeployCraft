using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Import d'un fichier de paquets (.json ou .xml) : aperçu de ce qui est repris tel quel,
/// avec des réserves ou pas du tout, puis création des paquets.
/// </summary>
public partial class ImportWindow : Window
{
    private const string IconOk = "";
    private const string IconWarning = "";
    private const string IconSkipped = "";

    private readonly string _content;
    private ImportReport? _report;

    /// <summary>Paquets créés (après « Importer »).</summary>
    public ImportReport? Result { get; private set; }

    public ImportWindow(string fileName, string content)
    {
        InitializeComponent();
        _content = content;
        FileText.Text = $"Fichier : {fileName}";
        Loaded += async (_, _) => await PreviewAsync();
    }

    private async Task PreviewAsync()
    {
        ImportButton.IsEnabled = false;
        try
        {
            _report = await Api.ImportPackages(_content, Repository.Text, apply: false);
            Show(_report);
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            Close();
        }
    }

    private void Show(ImportReport report)
    {
        RepositoryPanel.Visibility = report.Source == ImportReport.SourceXml ? Visibility.Visible : Visibility.Collapsed;

        Tree.Items.Clear();
        foreach (var note in report.Notes)
            Tree.Items.Add(new TreeViewItem { Header = Wrapped(note) });
        foreach (var p in report.Packages)
        {
            var title = p.State == ImportState.Skipped ? p.Name
                : p.FinalName == p.Name ? p.Name : $"{p.Name} → {p.FinalName}";
            var node = new TreeViewItem { Header = Ui.Header(StateIcon(p.State), $"{title} — {StateText(p.State)}", StateBrush(p.State)) };
            foreach (var note in p.Notes)
                node.Items.Add(new TreeViewItem { Header = Wrapped(note) });
            foreach (var s in p.Steps)
            {
                var stepNode = new TreeViewItem { Header = Ui.Header(StateIcon(s.State), s.Name, StateBrush(s.State)) };
                foreach (var note in s.Notes) stepNode.Items.Add(new TreeViewItem { Header = Wrapped(note) });
                stepNode.IsExpanded = s.State != ImportState.Ok;
                node.Items.Add(stepNode);
            }
            node.IsExpanded = p.State != ImportState.Ok;
            Tree.Items.Add(node);
        }

        var imported = report.Packages.Count(p => p.State != ImportState.Skipped);
        var withWarnings = report.Packages.Count(p => p.State == ImportState.Warning);
        var skipped = report.Packages.Count - imported;
        Summary.Text = report.Packages.Count == 0
            ? "Aucun paquet à importer."
            : $"{imported} paquet(s) à créer, dont {withWarnings} avec des réserves ; {skipped} non repris.";
        ImportButton.IsEnabled = imported > 0;
    }

    /// <summary>Remarque sous un paquet ou une étape (texte sur plusieurs lignes).</summary>
    private static TextBlock Wrapped(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 620, Margin = new Thickness(0, 1, 0, 1), Foreground = Brush("MutedBrush") };

    private static string StateIcon(ImportState state) => state switch
    {
        ImportState.Ok => IconOk,
        ImportState.Warning => IconWarning,
        _ => IconSkipped,
    };

    private static string StateText(ImportState state) => state switch
    {
        ImportState.Ok => "repris tel quel",
        ImportState.Warning => "repris avec des réserves",
        _ => "non repris",
    };

    private static Brush StateBrush(ImportState state) => Brush(state switch
    {
        ImportState.Ok => "OkBrush",
        ImportState.Warning => "WarnBrush",
        _ => "KoBrush",
    });

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private async void Preview_Click(object sender, RoutedEventArgs e) => await PreviewAsync();

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        try
        {
            Result = await Api.ImportPackages(_content, Repository.Text, apply: true);
            var created = Result.Packages.Count(p => p.NewId != null);
            MessageDialog.Show(this, $"{created} paquet(s) importé(s).", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            ImportButton.IsEnabled = true;
        }
    }

    /// <summary>Lit le fichier choisi ; null (avec un message) s'il est illisible.</summary>
    public static string? ReadFile(Window owner, string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(owner, $"Impossible de lire le fichier : {ex.Message}", "Importer des paquets", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }
}
