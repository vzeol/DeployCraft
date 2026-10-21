using System.Windows;
using System.Windows.Controls;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Choice of a schedule to add a package to (right-click on a package › Ajouter à une planification...).</summary>
public partial class SchedulePickerWindow : Window
{
    public Schedule? Selected => List.SelectedItem as Schedule;

    public SchedulePickerWindow(string packageName, IEnumerable<Schedule> schedules)
    {
        InitializeComponent();
        Prompt.Text = $"Planification où ajouter « {packageName} » (il sera déployé après les paquets déjà présents) :";
        List.ItemsSource = schedules.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        OkButton.IsEnabled = false;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => OkButton.IsEnabled = Selected != null;

    private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
