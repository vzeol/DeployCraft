namespace DeployCraft.Server.Validation;

/// <summary>Checks a credential typed in the console before it is saved.</summary>
internal static class CredentialValidation
{
    internal static string? Validate(string? user, string? password, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(user)) return Msg.Get("CredentialValidation_UserRequired");
        if (user.Contains('\\') || user.Contains('@')) return Msg.Get("CredentialValidation_DomainSeparate");
        if (isNew && string.IsNullOrEmpty(password)) return Msg.Get("CredentialValidation_PasswordRequired");
        return null;
    }
}
