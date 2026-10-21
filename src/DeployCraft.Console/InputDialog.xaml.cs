using System.Windows;

namespace DeployCraft.Console;

/// <summary>Small window to enter a text (rename...).</summary>
public partial class InputDialog : Window
{
    public string Value => Input.Text.Trim();

    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    /// <summary>Shows the window; returns the entered text, or null if cancelled or empty.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var dialog = new InputDialog(title, prompt, initial) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Value.Length > 0 ? dialog.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
