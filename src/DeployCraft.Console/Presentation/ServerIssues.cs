namespace DeployCraft.Console;

/// <summary>Problems of the service shown in the banner of the main window.</summary>
public static class ServerIssues
{
    public static string For(ServerStatus s)
    {
        var issues = new List<string>();
        if (!s.Elevated) issues.Add("Le service DeployCraft ne tourne pas en administrateur : les déploiements échoueront.");
        if (s.DefaultCredential == null) issues.Add("Aucun identifiant enregistré (Options › Identifiants). Nécessaire pour les postes du domaine.");
        if (s.UnreadableCredentials > 0) issues.Add("Un ou plusieurs mots de passe d'identifiants ne peuvent pas être relus : ressaisissez-les (Options › Identifiants).");
        return string.Join(Environment.NewLine, issues);
    }
}
