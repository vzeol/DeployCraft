namespace DeployCraft.Server.Validation;

/// <summary>Checks a credential typed in the console before it is saved.</summary>
internal static class CredentialValidation
{
    internal static string? Validate(string? user, string? password, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(user)) return "Saisissez le nom d'utilisateur.";
        if (user.Contains('\\') || user.Contains('@')) return "Saisissez le domaine dans sa propre case, et l'utilisateur seul.";
        if (isNew && string.IsNullOrEmpty(password)) return "Saisissez le mot de passe.";
        return null;
    }
}
