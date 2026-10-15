using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Préférences › Variables : variables personnalisées du serveur, enregistrées d'un bloc.</summary>
public partial class VariablesPage : UserControl
{
    private readonly ObservableCollection<Variable> _variables = [];
    private string _saved = "[]";

    public VariablesPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _variables;
        _variables.CollectionChanged += (_, _) => UpdateState();
        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>Des modifications ne sont pas encore enregistrées.</summary>
    public bool HasChanges => Snapshot() != _saved;

    private string Snapshot() => JsonSerializer.Serialize(
        _variables.Where(v => !string.IsNullOrWhiteSpace(v.Name) || !string.IsNullOrEmpty(v.Value)), Api.Json);

    private async Task LoadAsync()
    {
        if (Grid.IsEnabled) return; // déjà chargée (la page est recréée à chaque affichage de l'onglet)
        try
        {
            Fill(await Api.Variables());
            // Activée seulement après un chargement réussi : sinon « Enregistrer » effacerait les variables du serveur.
            Grid.IsEnabled = true;
            UpdateState();
        }
        catch (ApiException ex)
        {
            Status.Text = ex.Message;
        }
    }

    private void Fill(List<Variable> variables)
    {
        _variables.Clear();
        foreach (var v in variables) _variables.Add(v);
        _saved = Snapshot();
        UpdateState();
    }

    private void UpdateState()
    {
        AddButton.IsEnabled = Grid.IsEnabled;
        RemoveButton.IsEnabled = Grid.IsEnabled && Grid.SelectedItem is Variable;
        SaveButton.IsEnabled = Grid.IsEnabled && HasChanges;
        Status.Text = HasChanges ? "Modifications non enregistrées." : "";
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(UpdateState);

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) Dispatcher.BeginInvoke(UpdateState);
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    /// <summary>Nouvelle ligne, le nom en cours de saisie.</summary>
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var variable = new Variable();
        _variables.Add(variable);
        Grid.SelectedItem = variable;
        Grid.ScrollIntoView(variable);
        Grid.CurrentCell = new DataGridCellInfo(variable, Grid.Columns[0]);
        Grid.Focus();
        Grid.BeginEdit();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Variable variable) return;
        Grid.CancelEdit(DataGridEditingUnit.Row);
        _variables.Remove(variable);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Enregistre ; en cas de refus (nom invalide…), affiche la raison et renvoie false.</summary>
    public async Task<bool> SaveAsync()
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var list = _variables.Where(v => !string.IsNullOrWhiteSpace(v.Name) || !string.IsNullOrEmpty(v.Value)).ToList();
        try
        {
            Fill(await Api.SaveVariables(list));
            Status.Text = "Variables enregistrées.";
            return true;
        }
        catch (ApiException ex)
        {
            Ui.Error(Window.GetWindow(this), ex);
            return false;
        }
    }
}
