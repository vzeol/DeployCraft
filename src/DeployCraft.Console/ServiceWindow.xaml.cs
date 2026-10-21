using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DeployCraft.Console;

/// <summary>Options › Service en arrière-plan: service state, restart, Windows account it runs under.</summary>
public partial class ServiceWindow : Window
{
    private ServiceState? _state;

    public ServiceWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _state = await Api.Service();
        }
        catch (ApiException ex)
        {
            StateIcon.Text = ""; // cross
            StateIcon.Foreground = (Brush)FindResource("KoBrush");
            StateText.Text = L.Get("Service_Unreachable");
            Hint.Text = ex.Message;
            RestartButton.IsEnabled = false;
            ChangeLink.Visibility = Visibility.Collapsed;
            return;
        }

        var info = _state.Info;
        StateIcon.Text = info.Elevated ? "" : ""; // checkmark, or warning
        StateIcon.Foreground = (Brush)FindResource(info.Elevated ? "OkBrush" : "WarnBrush");
        StateText.Text = L.Format("ServiceWindow_RunningSince", Ui.LocalTime(info.StartedUtc)) + (info.IsService ? "" : " " + L.Get("ServiceWindow_ManualStart"));
        ServicesLink.Visibility = _state.Local ? Visibility.Visible : Visibility.Collapsed;
        AccountText.Text = info.LocalSystem ? L.Format("ServiceWindow_LocalSystemAccount", info.Account) : info.Account;

        var canChange = _state.CanEdit && _state.Local && info.IsService;
        RestartButton.IsEnabled = _state.CanEdit && info.IsService;
        ChangeLink.Visibility = canChange ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = !info.Elevated ? L.Get("ServiceWindow_HintNotAdmin")
            : !_state.CanEdit ? L.Get("ServiceWindow_HintNoEdit")
            : !info.IsService ? L.Get("ServiceWindow_HintManual")
            : !_state.Local ? L.Get("ServiceWindow_HintRemote")
            : "";
    }

    private void OpenServices_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageDialog.Show(this, L.Get("ServiceWindow_RestartConfirm"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Api.RestartService();
            Close();
        }
        catch (ApiException ex) { Ui.Error(this, ex); }
    }

    private void ChangeAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_state == null) return;
        if (new ServiceAccountWindow(_state.Info.Account, _state.Info.LocalSystem) { Owner = this }.ShowDialog() == true) Close();
    }

    private void ConsoleUsers_Click(object sender, RoutedEventArgs e) => new ConsoleUsersWindow { Owner = this }.ShowDialog();
}
