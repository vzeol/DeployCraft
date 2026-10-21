using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Built-in help (Aide › Aide de DeployCraft, F1): topics on the left, search, text on the right.</summary>
public partial class HelpWindow : Window
{
    private static HelpWindow? _open;
    private readonly List<HelpContent.Topic> _topics;

    private HelpWindow()
    {
        InitializeComponent();
        var resource = Application.GetResourceStream(new Uri("Help/aide.txt", UriKind.Relative));
        using (var reader = new StreamReader(resource.Stream)) _topics = HelpContent.Parse(reader.ReadToEnd());
        Topics.ItemsSource = _topics;
        Closed += (_, _) => _open = null;
    }

    /// <summary>Opens the help (one window at a time) on the topic <paramref name="topicId"/>, or the first one.</summary>
    public static void Show(Window owner, string? topicId = null)
    {
        _open ??= new HelpWindow { Owner = owner };
        _open.Search.Text = "";
        _open.Topics.SelectedItem = _open._topics.FirstOrDefault(t => t.Id == topicId) ?? _open._topics.FirstOrDefault();
        _open.Show();
        _open.Activate();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var search = Search.Text.Trim();
        var selected = Topics.SelectedItem;
        var shown = search.Length == 0 ? _topics : [.. _topics.Where(t => t.Matches(search))];
        Topics.ItemsSource = shown;
        Topics.SelectedItem = shown.Contains(selected) ? selected : shown.FirstOrDefault();
    }

    private void Topics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Page.Children.Clear();
        if (Topics.SelectedItem is not HelpContent.Topic topic) return;
        Page.Children.Add(new TextBlock { Text = topic.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var block in topic.Blocks)
            Page.Children.Add(block.Kind switch
            {
                HelpContent.BlockKind.Heading => new TextBlock
                {
                    Text = block.Text,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 4),
                    Foreground = (Brush)FindResource("Accent"),
                },
                HelpContent.BlockKind.Bullet => Bullet(block.Text),
                _ => new TextBlock { Text = block.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) },
            });
        Scroller.ScrollToTop();
    }

    private static UIElement Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(4, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = "•" });
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }
}
