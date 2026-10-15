using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

public record CredentialRow(Credential Credential)
{
    public string Account => Credential.Account;
    public string? Description => Credential.Description;
    public string DefaultText => Credential.IsDefault ? "(par défaut)" : "";
    public string UnreadableText => Credential.PasswordUnreadable ? "mot de passe à ressaisir" : "";
    public override string ToString() => Credential.Display; // nom lu par les outils d'accessibilité
}

/// <summary>Liste des identifiants : ajouter, modifier, par défaut, supprimer.</summary>
public partial class CredentialsWindow : Window
{
    public CredentialsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync(null);
        Ui.SelectOnRightClick(List);
        Ui.ContextMenu(List, () =>
        [
            new("Modifier…", () => Edit_Click(this, new()), Selected != null),
            new("Ajouter…", () => Add_Click(this, new())),
            new("Définir par défaut", () => Default_Click(this, new()), Selected is { IsDefault: false }),
            Ui.Separator,
            new("Supprimer", () => Remove_Click(this, new()), Selected != null, "Suppr"),
        ]);
        List.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Delete) Remove_Click(this, e);
        };
    }

    private Credential? Selected => (List.SelectedItem as CredentialRow)?.Credential;

    private async Task LoadAsync(long? select)
    {
        try
        {
            var all = await Api.Credentials();
            var keep = select ?? Selected?.Id;
            List.ItemsSource = all.Select(c => new CredentialRow(c)).ToList();
            List.SelectedItem = List.Items.Cast<CredentialRow>().FirstOrDefault(r => r.Credential.Id == keep)
                                ?? List.Items.Cast<CredentialRow>().FirstOrDefault();
            Warning.Text = all.Any(c => c.PasswordUnreadable)
                ? "Un ou plusieurs mots de passe ne peuvent pas être relus (base venue d'un autre serveur ?) : ressaisissez-les avec « Modifier »."
                : all.Count == 0 ? "Aucun identifiant : les déploiements utiliseront le compte du service DeployCraft." : "";
            UpdateButtons();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private void UpdateButtons()
    {
        EditButton.IsEnabled = RemoveButton.IsEnabled = Selected != null;
        DefaultButton.IsEnabled = Selected is { IsDefault: false };
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected != null) Edit_Click(sender, e);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CredentialEditWindow(null) { Owner = this };
        if (dialog.ShowDialog() == true) await LoadAsync(dialog.Saved?.Id);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } c) return;
        var dialog = new CredentialEditWindow(c) { Owner = this };
        if (dialog.ShowDialog() == true) await LoadAsync(c.Id);
    }

    private async void Default_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } c) return;
        try
        {
            await Api.SetDefaultCredential(c.Id);
            await LoadAsync(c.Id);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } c) return;
        if (MessageDialog.Show(this, $"Supprimer l'identifiant {c.Display} ?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await Api.DeleteCredential(c.Id);
            await LoadAsync(null);
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    // ---------- Liste de choix des autres fenêtres ----------

    /// <summary>
    /// Remplit une liste de choix d'identifiant (Déployer une fois, planification) et sélectionne
    /// <paramref name="selectedId"/>, ou celui par défaut. Renvoie les identifiants chargés.
    /// </summary>
    public static async Task<List<Credential>> Fill(ComboBox box, long? selectedId)
    {
        var all = await Api.Credentials();
        box.Items.Clear();
        foreach (var c in all)
            box.Items.Add(new ComboBoxItem
            {
                Content = c.Display + (c.IsDefault ? " (par défaut)" : "") + (c.PasswordUnreadable ? " — mot de passe à ressaisir" : ""),
                Tag = c.Id,
            });
        if (all.Count == 0) box.Items.Add(new ComboBoxItem { Content = "Aucun : compte du service DeployCraft", Tag = null });
        var wanted = selectedId ?? all.FirstOrDefault(c => c.IsDefault)?.Id;
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, wanted)) ?? box.Items[0];
        box.IsEnabled = all.Count > 0;
        return all;
    }

    /// <summary>Identifiant choisi dans une liste remplie par <see cref="Fill"/>.</summary>
    public static long? Chosen(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as long?;
}
