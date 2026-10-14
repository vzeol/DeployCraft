using System.Net.NetworkInformation;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Ajout ou modification d'un identifiant. Le mot de passe saisi part au service et n'est jamais relu.</summary>
public partial class CredentialEditWindow : Window
{
    private readonly Credential? _existing;

    /// <summary>Identifiant enregistré (après OK).</summary>
    public Credential? Saved { get; private set; }

    public CredentialEditWindow(Credential? existing)
    {
        InitializeComponent();
        _existing = existing;
        if (existing != null)
        {
            Title = "Modifier l'identifiant";
            DomainBox.Text = existing.IsLocal ? "" : existing.Domain;
            UserBox.Text = existing.User;
            DescriptionBox.Text = existing.Description;
            PasswordHint.Text = existing.PasswordUnreadable
                ? "Le mot de passe enregistré ne peut pas être relu : saisissez-le de nouveau."
                : "Laisser vide pour conserver le mot de passe actuel.";
        }
        else
        {
            // Domaine du poste, comme point de départ.
            DomainBox.Text = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            PasswordHint.Visibility = Visibility.Collapsed;
        }
        Loaded += (_, _) => (existing == null ? UserBox : PasswordBox as UIElement).Focus();
        UpdateTest();
    }

    private bool IsLocal => Credential.NormalizeDomain(DomainBox.Text) == null;

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        Message.Text = "";
        UpdateTest();
    }

    private void UpdateTest()
    {
        TestButton.IsEnabled = UserBox.Text.Trim().Length > 0
                               && (PasswordBox.Password.Length > 0 || _existing is { PasswordUnreadable: false });
        TestButton.ToolTip = IsLocal
            ? "Essaie le compte sur un poste (accès au partage ADMIN$, comme un déploiement)"
            : "Vérifie le compte et le mot de passe auprès du domaine";
    }

    private object Input(string? computer = null) => new
    {
        id = _existing?.Id,
        domain = DomainBox.Text.Trim(),
        user = UserBox.Text.Trim(),
        description = DescriptionBox.Text.Trim(),
        password = PasswordBox.Password,
        computer,
    };

    /// <summary>Dernier poste choisi pour tester un compte local (proposé à nouveau).</summary>
    private static string _testComputer = "";

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        string? computer = null;
        if (IsLocal)
        {
            computer = InputDialog.Ask(this, "Tester l'identifiant",
                $"Poste sur lequel essayer le compte local « {UserBox.Text.Trim()} » :", _testComputer);
            if (computer == null) return;
            _testComputer = computer;
        }
        TestButton.IsEnabled = false;
        Show("Vérification…", "MutedBrush");
        try
        {
            await Api.TestCredential(Input(computer));
            Show(computer == null ? "✓ Connexion au domaine réussie." : $"✓ Compte accepté par {computer}.", "OkBrush");
        }
        catch (ApiException ex)
        {
            Show(ex.Message, "KoBrush");
        }
        finally
        {
            UpdateTest();
        }
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        OkButton.IsEnabled = false;
        try
        {
            Saved = await Api.SaveCredential(_existing?.Id ?? 0, Input());
            PasswordBox.Clear();
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            Show(ex.Message, "KoBrush");
            OkButton.IsEnabled = true;
        }
    }

    private void Show(string text, string brush)
    {
        Message.Text = text;
        Message.Foreground = (System.Windows.Media.Brush)FindResource(brush);
    }
}
