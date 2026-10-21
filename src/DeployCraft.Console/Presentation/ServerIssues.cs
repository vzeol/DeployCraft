namespace DeployCraft.Console;

/// <summary>Problems of the service shown in the banner of the main window.</summary>
public static class ServerIssues
{
    public static string For(ServerStatus s)
    {
        var issues = new List<string>();
        if (!s.Elevated) issues.Add(L.Get("ServerIssues_NotElevated"));
        if (s.DefaultCredential == null) issues.Add(L.Get("ServerIssues_NoCredential"));
        if (s.UnreadableCredentials > 0) issues.Add(L.Get("ServerIssues_Unreadable"));
        return string.Join(Environment.NewLine, issues);
    }
}
