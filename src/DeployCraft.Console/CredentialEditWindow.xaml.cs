using System.Net.NetworkInformation;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Add or edit a credential. The password typed goes to the service and is never read back.</summary>
public partial class CredentialEditWindow : Window
{
    private readonly Credential? _existing;

    /// <summary>Saved credential (after OK).</summary>
    public Credential? Saved { get; private set; }

    public CredentialEditWindow(Credential? existing)
    {
        InitializeComponent();
        _existing = existing;
        if (existing != null)
        {
            Title = L.Get("CredentialEdit_EditTitle");
            DomainBox.Text = existing.IsLocal ? "" : existing.Domain;
            UserBox.Text = existing.User;
            DescriptionBox.Text = existing.Description;
            PasswordHint.Text = existing.PasswordUnreadable
                ? L.Get("CredentialEdit_PasswordUnreadable")
                : L.Get("CredentialEdit_PasswordKeepHint");
        }
        else
        {
            // Computer's domain, as a starting point.
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
            ? L.Get("CredentialEdit_TestLocalTip")
            : L.Get("CredentialEdit_TestDomainTip");
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

    /// <summary>Last computer chosen to test a local account (offered again).</summary>
    private static string _testComputer = "";

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        string? computer = null;
        if (IsLocal)
        {
            computer = InputDialog.Ask(this, L.Get("CredentialEdit_TestTitle"),
                L.Format("CredentialEdit_TestComputerPrompt", UserBox.Text.Trim()), _testComputer);
            if (computer == null) return;
            _testComputer = computer;
        }
        TestButton.IsEnabled = false;
        Show(L.Get("CredentialEdit_Checking"), "MutedBrush");
        try
        {
            await Api.TestCredential(Input(computer));
            Show(computer == null ? L.Get("CredentialEdit_DomainOk") : L.Format("CredentialEdit_AccountAccepted", computer), "OkBrush");
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
