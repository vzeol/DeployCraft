using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DeployCraft.Console;

/// <summary>Petits utilitaires d'affichage partagés par les fenêtres.</summary>
public static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string IconHistory = "";
    public const string IconFolder = "";
    public const string IconPackage = "";
    public const string IconSettings = "";
    public const string IconInstall = "";
    public const string IconCommand = "";
    public const string IconScript = "";
    public const string IconPause = ""; // chronomètre
    public const string IconComputer = "";
    public const string IconProperties = "";
    public const string IconSteps = "";
    public const string IconSchedule = ""; // calendrier

    public static string StatusText(string status) => status switch
    {
        "Queued" => "En attente",
        "Running" or "running" => "En cours",
        "Successful" or "success" => "Réussi",
        "Failed" or "failed" => "Échec",
        "Partial" => "Partiel",
        "skipped" => "Ignoré",
        "stopped" => "Arrêt en succès",
        _ => status,
    };

    public static string StatusIcon(string status) => status switch
    {
        "Successful" or "success" => "", // coche dans un cercle
        "Failed" or "failed" => "",      // croix dans un cercle
        "Partial" => "",                 // avertissement
        "Running" or "running" => "",    // synchro
        "skipped" => "",                 // ignoré
        "stopped" => "",                 // coche (arrêt compté réussi)
        _ => "",                         // horloge (en attente)
    };

    public static Brush StatusBrush(string status) => status switch
    {
        "Successful" or "success" or "stopped" => (Brush)Application.Current.Resources["OkBrush"],
        "Failed" or "failed" => (Brush)Application.Current.Resources["KoBrush"],
        "Partial" => (Brush)Application.Current.Resources["Accent"],
        "Running" or "running" => (Brush)Application.Current.Resources["RunBrush"],
        _ => (Brush)Application.Current.Resources["MutedBrush"],
    };

    public static string RunAsText(Server.Data.RunAsMode mode) => mode switch
    {
        Server.Data.RunAsMode.DeployUser => "Compte de déploiement",
        Server.Data.RunAsMode.LocalSystem => "Système local",
        _ => "Utilisateur connecté",
    };

    public static string RunAsHint(Server.Data.RunAsMode mode) => mode switch
    {
        Server.Data.RunAsMode.DeployUser => "Le service temporaire tourne sous l'identifiant choisi (accès réseau avec ce compte).",
        Server.Data.RunAsMode.LocalSystem => "Le service temporaire tourne en Système local (accès réseau avec le compte ordinateur du poste).",
        _ => "Chaque étape s'ouvre dans la session de l'utilisateur connecté ; échec si personne n'est connecté.",
    };

    public static string CopyModeText(Server.Data.CopyMode mode) =>
        mode == Server.Data.CopyMode.Push ? "Push (le serveur copie)" : "Pull (le poste lit le partage)";

    public static string Duration(DateTime? start, DateTime? end)
    {
        if (start == null) return "";
        var s = (int)Math.Round(((end ?? DateTime.UtcNow) - start.Value).TotalSeconds);
        return s < 60 ? $"{s} s" : $"{s / 60} min {s % 60:00} s";
    }

    public static string LocalTime(DateTime? utc) => utc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "";

    public static string StepIcon(Server.Data.StepType type) => type switch
    {
        Server.Data.StepType.Install => IconInstall,
        Server.Data.StepType.Command => IconCommand,
        Server.Data.StepType.Pause => IconPause,
        Server.Data.StepType.Nested => IconPackage,
        _ => IconScript,
    };

    /// <summary>En-tête d'élément d'arbre : icône + texte.</summary>
    public static StackPanel Header(string icon, string text, Brush? iconBrush = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = IconFont,
            FontSize = 15,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = iconBrush ?? (Brush)Application.Current.Resources["Accent"],
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    public static void Error(Window? owner, Exception ex) =>
        MessageDialog.Show(owner!, ex.Message, "DeployCraft", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---------- Menus clic droit ----------

    /// <summary>Entrée de menu contextuel ; <see cref="Separator"/> pour un trait.</summary>
    public record MenuEntry(string Header, Action Action, bool Enabled = true, string? Gesture = null);

    public static readonly MenuEntry Separator = new("-", () => { });

    /// <summary>
    /// Menu clic droit reconstruit à chaque ouverture selon la sélection du moment.
    /// Une liste vide n'ouvre rien.
    /// </summary>
    public static void ContextMenu(FrameworkElement element, Func<IEnumerable<MenuEntry>> build)
    {
        element.ContextMenu = new ContextMenu();
        element.ContextMenuOpening += (_, e) =>
        {
            var entries = build().ToList();
            if (entries.Count == 0) { e.Handled = true; return; }
            var menu = element.ContextMenu;
            menu.Items.Clear();
            foreach (var entry in entries)
            {
                if (entry == Separator) { menu.Items.Add(new System.Windows.Controls.Separator()); continue; }
                var item = new MenuItem { Header = entry.Header, IsEnabled = entry.Enabled, InputGestureText = entry.Gesture ?? "" };
                item.Click += (_, _) => entry.Action();
                menu.Items.Add(item);
            }
        };
    }

    /// <summary>Le clic droit sélectionne d'abord l'élément sous la souris (ligne de grille, nœud d'arbre, élément de liste).</summary>
    public static void SelectOnRightClick(Control control)
    {
        control.PreviewMouseRightButtonDown += (_, e) =>
        {
            switch (FindAncestor(e.OriginalSource as DependencyObject))
            {
                case DataGridRow row:
                    if (!row.IsSelected) { ((DataGrid)control).SelectedItem = row.Item; }
                    break;
                case TreeViewItem node:
                    node.IsSelected = true;
                    node.Focus();
                    break;
                case ListBoxItem item:
                    if (!item.IsSelected) { ((ListBox)control).SelectedItem = item.DataContext; }
                    break;
            }
        };
    }

    private static DependencyObject? FindAncestor(DependencyObject? d)
    {
        while (d != null && d is not (DataGridRow or TreeViewItem or ListBoxItem))
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d;
    }
}

/// <summary>Statut technique → libellé français (colonnes de grille).</summary>
public class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Ui.StatusText(value?.ToString() ?? "");
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Statut technique → icône.</summary>
public class StatusIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Ui.StatusIcon(value?.ToString() ?? "");
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Statut technique → couleur.</summary>
public class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Ui.StatusBrush(value?.ToString() ?? "");
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
