using System.Windows;
using System.Windows.Controls;

namespace DeployCraft.Console;

/// <summary>Accounts and groups allowed to use the console, in addition to the server administrators; active sessions.</summary>
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
            var status = await Api.Status();
            _canEdit = info.CanEdit;
            List.ItemsSource = info.Users;
            AccessHint.Text = $"Les administrateurs de {status.Machine} ont toujours accès à DeployCraft. Ajoutez ici d'autres comptes ou groupes " +
                              $"Windows (DOMAINE\\nom) qui pourront ouvrir la console, sans les rendre administrateurs du serveur. Vous êtes connecté en tant que {status.ConnectedAs}.";
            Sessions.ItemsSource = info.Sessions
                .Select(s => new { s.User, s.Computer, LastSeen = s.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss") }).ToList();
            SessionsCount.Text = $"{info.Sessions.Count} console(s) ouverte(s) en ce moment (une console fermée disparaît de la liste après 2 minutes).";
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
