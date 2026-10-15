using System.Windows;
using System.Windows.Controls;

namespace DeployCraft.Console;

/// <summary>Comptes et groupes autorisés à utiliser la console, en plus des administrateurs du serveur ; sessions actives.</summary>
public partial class ConsoleUsersWindow : Window
{
    private bool _canEdit;

    public ConsoleUsersWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
        Ui.SelectOnRightClick(List);
        Ui.ContextMenu(List, () =>
        [
            new("Ajouter…", () => Add_Click(this, new()), _canEdit),
            Ui.Separator,
            new("Supprimer", () => Remove_Click(this, new()), _canEdit && Selected != null, "Suppr"),
        ]);
        List.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Delete) Remove_Click(this, e);
        };
    }

    private ConsoleUser? Selected => List.SelectedItem as ConsoleUser;

    private async Task LoadAsync()
    {
        try
        {
            var info = await Api.Access();
            _canEdit = info.CanEdit;
            List.ItemsSource = info.Users;
            Sessions.ItemsSource = info.Sessions
                .Select(s => $"{s.User} — dernière activité à {s.LastSeenUtc.ToLocalTime():HH:mm:ss}").ToList();
            Warning.Text = _canEdit ? "" : "Seuls les administrateurs de ce serveur peuvent modifier cette liste.";
            UpdateButtons();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private void UpdateButtons()
    {
        AddButton.IsEnabled = _canEdit;
        RemoveButton.IsEnabled = _canEdit && Selected != null;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit) return;
        if (InputDialog.Ask(this, Title, "Compte ou groupe Windows (DOMAINE\\nom) :", "") is not { } name) return;
        try
        {
            await Api.AddConsoleUser(name);
            await LoadAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit || Selected is not { } user) return;
        if (MessageDialog.Show(this, $"Retirer l'accès de {user.Name} ?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await Api.RemoveConsoleUser(user.Sid);
            await LoadAsync();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }
}
