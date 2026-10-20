using System.ComponentModel;
using System.Windows;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Un poste cible et l'étape du premier paquet à laquelle il commence.</summary>
public class TargetItem(string computer, int startStep, DeployWindow window) : INotifyPropertyChanged
{
    private int _startStep = startStep;

    public string Computer { get; } = computer;

    public int StartStep
    {
        get => _startStep;
        set
        {
            _startStep = value;
            PropertyChanged?.Invoke(this, new(nameof(StartStep)));
            PropertyChanged?.Invoke(this, new(nameof(StepText)));
        }
    }

    public string StepText => window.StepChoices.FirstOrDefault(c => c.Number == StartStep)?.Text ?? StartStep.ToString();

    // Vérification du nom dans le DNS du serveur : en cours (null), trouvé ou introuvable.
    private NameCheckResult? _check;

    public NameCheckResult? Check
    {
        get => _check;
        set
        {
            _check = value;
            foreach (var p in new[] { nameof(CheckIcon), nameof(CheckBrush), nameof(CheckTip) }) PropertyChanged?.Invoke(this, new(p));
        }
    }

    public string CheckIcon => _check == null ? "" : Ui.StatusIcon(_check.Found ? "Successful" : "Failed"); // sablier / coche / croix

    public System.Windows.Media.Brush CheckBrush =>
        (System.Windows.Media.Brush)Application.Current.Resources[_check == null ? "MutedBrush" : _check.Found ? "OkBrush" : "KoBrush"];

    public string CheckTip => _check == null ? "Recherche dans le DNS…"
        : _check.Found ? $"Trouvé : {_check.DnsName}"
        : (_check.Error ?? "Introuvable") + " Le déploiement reste possible.";

    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(StepText)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
