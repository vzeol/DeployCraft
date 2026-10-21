using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace DeployCraft.Console;

/// <summary>Node of the AD tree: an OU (sub-OUs then computers) or a computer.</summary>
public class AdNode : INotifyPropertyChanged
{
    private bool? _isChecked = false;
    private bool _isExpanded;

    public string Label { get; init; } = "";
    public string? Detail { get; set; }
    public string Icon { get; init; } = "";
    public string? ComputerName { get; init; }
    public AdNode? Parent { get; init; }
    public ObservableCollection<AdNode> Children { get; } = [];

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            // Checking an OU checks all its computers.
            if (value.HasValue) foreach (var c in Children) c.IsChecked = value;
            Parent?.SyncFromChildren();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    private void SyncFromChildren()
    {
        var all = Children.All(c => c.IsChecked == true);
        var none = Children.All(c => c.IsChecked == false);
        _isChecked = all ? true : none ? false : null;
        OnPropertyChanged(nameof(IsChecked));
        Parent?.SyncFromChildren();
    }

    /// <summary>Number of computers under this node (sub-OUs included).</summary>
    public int CountComputers() => ComputerName != null ? 1 : Children.Sum(c => c.CountComputers());

    public void ExpandAll()
    {
        if (ComputerName != null) return;
        IsExpanded = true;
        foreach (var c in Children) c.ExpandAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public partial class AdPickerWindow : Window
{
    private readonly HashSet<string> _initial;
    private readonly List<AdNode> _computers = [];

    public List<string> Selected { get; } = [];
    /// <summary>Computers shown and unchecked: to remove from the targets.</summary>
    public List<string> Unchecked { get; } = [];

    public AdPickerWindow(IEnumerable<string> current)
    {
        InitializeComponent();
        _initial = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SearchButton.IsEnabled = false;
        Info.Text = "Lecture de l'Active Directory…";
        Tree.ItemsSource = null;
        _computers.Clear();
        try
        {
            var search = SearchBox.Text.Trim();
            var list = await Api.AdComputers(search);

            // Real tree: "Ordinateurs / Postes_Clients / B101" -> Ordinateurs > Postes_Clients > B101.
            var roots = new List<AdNode>();
            var folders = new Dictionary<string, AdNode>(StringComparer.OrdinalIgnoreCase);
            AdNode Folder(string path)
            {
                if (folders.TryGetValue(path, out var existing)) return existing;
                var cut = path.LastIndexOf(" / ", StringComparison.Ordinal);
                var parent = cut < 0 ? null : Folder(path[..cut]);
                var node = new AdNode { Label = cut < 0 ? path : path[(cut + 3)..], Icon = Ui.IconFolder, Parent = parent };
                (parent?.Children ?? (IList<AdNode>)roots).Add(node);
                folders[path] = node;
                return node;
            }

            // Sub-OUs first (alphabetical order), then the computers of the OU.
            foreach (var path in list.Select(c => c.Ou).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                Folder(path);
            foreach (var c in list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var ou = Folder(c.Ou);
                var node = new AdNode
                {
                    Label = c.Name,
                    ComputerName = c.Name,
                    Icon = Ui.IconComputer,
                    Parent = ou,
                    Detail = (c.OperatingSystem ?? "") + (c.Enabled ? "" : " · désactivé"),
                };
                ou.Children.Add(node);
                _computers.Add(node);
            }
            foreach (var f in folders.Values) f.Detail = $"({f.CountComputers()})";
            foreach (var node in _computers.Where(n => _initial.Contains(n.ComputerName!))) node.IsChecked = true;

            // A search opens everything to show the computers found; otherwise everything stays collapsed.
            if (search.Length > 0) foreach (var r in roots) r.ExpandAll();

            Tree.ItemsSource = roots;
            Info.Text = list.Count == 0 ? "Aucun poste trouvé." : $"{list.Count} poste(s) dans {folders.Count} OU. Cochez une OU pour sélectionner tous ses postes.";
        }
        catch (ApiException ex)
        {
            Info.Text = ex.Message;
            Info.Foreground = (System.Windows.Media.Brush)FindResource("KoBrush");
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LoadAsync();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Selected.AddRange(_computers.Where(c => c.IsChecked == true).Select(c => c.ComputerName!));
        Unchecked.AddRange(_computers.Where(c => c.IsChecked != true && _initial.Contains(c.ComputerName!)).Select(c => c.ComputerName!));
        DialogResult = true;
    }
}
