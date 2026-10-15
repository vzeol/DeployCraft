using System.Text.RegularExpressions;

namespace DeployCraft.Server.Data;

/// <summary>
/// Variables $(Nom) : système (date, heure, serveur, poste) et personnalisées (Préférences › Variables).
/// Seuls les noms connus sont remplacés : un $(…) inconnu reste tel quel, car c'est aussi une sous-expression
/// PowerShell ($(Get-Date)…). Remplacement en une passe : la valeur d'une variable n'est jamais relue.
/// </summary>
public static partial class VariableText
{
    public const string Date = "Date";
    public const string Time = "Heure";
    public const string Server = "Serveur";
    public const string Computer = "Poste";

    /// <summary>Variables système, réservées : leur nom ne peut pas servir à une variable personnalisée.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> System =
    [
        (Date, "Date du lancement sur le serveur (aaaa-MM-jj)"),
        (Time, "Heure du lancement sur le serveur (HH-mm)"),
        (Server, "Nom du serveur DeployCraft"),
        (Computer, "Nom du poste cible"),
    ];

    [GeneratedRegex(@"\$\(([A-Za-z0-9_]+)\)")]
    private static partial Regex Reference();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ValidName();

    public static string? Replace(string? text, IReadOnlyDictionary<string, string> values) =>
        string.IsNullOrEmpty(text) ? text
            : Reference().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    public static void Replace(PackageStep step, IReadOnlyDictionary<string, string> values)
    {
        step.File = Replace(step.File, values);
        step.Arguments = Replace(step.Arguments, values);
        step.Command = Replace(step.Command, values);
        Replace(step.Conditions, values);
        foreach (var group in step.ExtraConditions ?? []) Replace(group.Conditions, values);
    }

    public static void Replace(Conditions conditions, IReadOnlyDictionary<string, string> values)
    {
        conditions.FilePath = Replace(conditions.FilePath, values);
        conditions.RegistryKey = Replace(conditions.RegistryKey, values);
    }

    /// <summary>Valeurs au lancement : variables personnalisées puis système (date, heure, serveur), sans $(Poste).</summary>
    public static Dictionary<string, string> AtLaunch(IEnumerable<Variable> custom, DateTime now, string server)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in custom) values[v.Name] = v.Value;
        values[Date] = now.ToString("yyyy-MM-dd");
        values[Time] = now.ToString("HH-mm");
        values[Server] = server;
        return values;
    }

    /// <summary>Erreur de saisie des variables personnalisées (null = correctes).</summary>
    public static string? Validate(IReadOnlyList<Variable> variables)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name)) return "Chaque variable doit avoir un nom.";
            if (!ValidName().IsMatch(v.Name))
                return $"« {v.Name} » : le nom ne peut contenir que des lettres sans accent, des chiffres et « _ » (pas d'espace ni de « - »).";
            if (System.Any(s => string.Equals(s.Name, v.Name, StringComparison.OrdinalIgnoreCase)))
                return $"« {v.Name} » est une variable système : choisissez un autre nom.";
            if (!seen.Add(v.Name)) return $"La variable « {v.Name} » est définie deux fois.";
        }
        return null;
    }
}
