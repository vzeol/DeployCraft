using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeployCraft.Console;

/// <summary>
/// Boîte de message aux couleurs du thème, à la place de MessageBox (même signature) : centrée sur la fenêtre
/// appelante, Entrée = premier bouton, Échap = Annuler (sinon Non, sinon OK), texte copiable.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageBoxResult _result;
    private readonly MessageBoxResult _cancelResult;

    private MessageDialog(string text, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = title;
        Message.Text = text;

        Glyph.FontFamily = Ui.IconFont;
        (Glyph.Text, var brush) = image switch
        {
            MessageBoxImage.Question => ("", "Accent"),       // aide
            MessageBoxImage.Warning => ("", "Accent"),        // avertissement
            MessageBoxImage.Error => ("", "KoBrush"),         // erreur
            MessageBoxImage.Information => ("", "RunBrush"),  // information
            _ => ("", "TextBrush"),
        };
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Glyph.Visibility = Glyph.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        (string Text, MessageBoxResult Result)[] choices = buttons switch
        {
            MessageBoxButton.YesNo => [("Oui", MessageBoxResult.Yes), ("Non", MessageBoxResult.No)],
            MessageBoxButton.YesNoCancel => [("Oui", MessageBoxResult.Yes), ("Non", MessageBoxResult.No), ("Annuler", MessageBoxResult.Cancel)],
            MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK), ("Annuler", MessageBoxResult.Cancel)],
            _ => [("OK", MessageBoxResult.OK)],
        };
        _cancelResult = choices.Any(c => c.Result == MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : choices.Any(c => c.Result == MessageBoxResult.No) ? MessageBoxResult.No
            : MessageBoxResult.OK;
        _result = _cancelResult; // fermeture par la croix

        Button? first = null;
        foreach (var (label, result) in choices)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("DialogButton"),
                IsDefault = first == null,
                IsCancel = result == _cancelResult,
            };
            if (first == null) button.FontWeight = FontWeights.SemiBold;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                _result = result;
                DialogResult = result != _cancelResult || choices.Length == 1;
            };
            Buttons.Children.Add(button);
            first ??= button;
        }
        Loaded += (_, _) => first?.Focus();

        // Ctrl+C sans sélection : copie tout le message.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && Message.SelectionLength == 0)
            {
                Clipboard.SetText(Message.Text);
                e.Handled = true;
            }
        };
    }

    public static MessageBoxResult Show(Window? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new MessageDialog(text, title, buttons, image);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._result;
    }
}
