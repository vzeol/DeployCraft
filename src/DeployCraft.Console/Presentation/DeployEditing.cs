using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Rules and texts of the "Deploy once" window that do not depend on the window.</summary>
public static class DeployEditing
{
    /// <summary>
    /// Start steps offered for each computer: the steps of the first package, nested packages replaced by their steps
    /// (as at launch). A nested package that cannot be found leaves the package as is: the launch reports it.
    /// </summary>
    public static List<StepChoice> StepChoices(Package? first, Func<long, Package?> lookup)
    {
        try
        {
            first = first?.Flatten(lookup);
        }
        catch (ArgumentException) { /* paquet imbriqué introuvable : le lancement le signalera */ }
        if (first == null) return [];
        return first.Steps.Select((s, i) => new StepChoice(i + 1, string.IsNullOrWhiteSpace(s.Name) ? $"{i + 1}" : $"{i + 1}. {s.Name}")).ToList();
    }

    public static string RunAsHint(IReadOnlyList<RunAsMode> modes, bool hasCredentials) =>
        modes.Count != 1 ? L.Get("DeployEditing_EachPackageOwn") : modes[0] switch
        {
            RunAsMode.DeployUser when !hasCredentials => L.Get("DeployEditing_NoCredentialLocalSystem"),
            RunAsMode.DeployUser => L.Get("DeployEditing_RunsAsCredential"),
            RunAsMode.LocalSystem => L.Get("DeployEditing_RunsAsLocalSystem"),
            _ => L.Get("DeployEditing_RunsInUserSession"),
        };

    public static string CopyModeHint(IReadOnlyList<CopyMode> modes) =>
        modes.Count != 1 ? L.Get("DeployEditing_EachPackageOwn") : modes[0] == CopyMode.Push
            ? L.Get("DeployEditing_CopyPush")
            : L.Get("DeployEditing_CopyPull");

    public static string CountText(int packages, int targets)
    {
        var text = targets == 0 ? L.Get("DeployEditing_NoTarget") : L.Format("DeployEditing_ComputerCount", targets);
        return packages > 1 ? L.Format("DeployEditing_PackagesAndTargets", packages, text) : char.ToUpper(text[0]) + text[1..];
    }
}
