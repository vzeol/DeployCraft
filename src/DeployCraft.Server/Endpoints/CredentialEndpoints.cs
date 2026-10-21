using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;
using DeployCraft.Server.Validation;

namespace DeployCraft.Server.Endpoints;

/// <summary>Deployment credentials.</summary>
internal static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/credentials", (DeployCredentials cred) => cred.List());

        api.MapPost("/credentials", (CredentialInput input, DeployCredentials cred) =>
        {
            if (CredentialValidation.Validate(input.User, input.Password, isNew: true) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(cred.Save(input.ToCredential(0), input.Password));
        });

        api.MapPut("/credentials/{id:long}", (long id, CredentialInput input, DeployCredentials cred) =>
        {
            if (cred.Get(id) == null) return Results.NotFound();
            if (CredentialValidation.Validate(input.User, input.Password, isNew: false) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(cred.Save(input.ToCredential(id), input.Password));
        });

        api.MapDelete("/credentials/{id:long}", (long id, DeployCredentials cred, IScheduleRepository scheduleRepository) =>
        {
            var users = scheduleRepository.List().Where(s => s.CredentialId == id).Select(s => $"« {s.Name} »").ToList();
            if (users.Count > 0)
                return Results.BadRequest(new { error = $"Cet identifiant est utilisé par la planification {string.Join(", ", users)} : choisissez-en un autre dans ses options avant de le supprimer." });
            cred.Delete(id);
            return Results.NoContent();
        });

        api.MapPost("/credentials/{id:long}/default", (long id, DeployCredentials cred) =>
        {
            if (cred.Get(id) == null) return Results.NotFound();
            cred.SetDefault(id);
            return Results.NoContent();
        });

        // Tests a domain account against the directory (empty password + id = the saved one).
        // A local account is specific to each computer: it is tried on the given computer.
        api.MapPost("/credentials/test", (CredentialInput input, DeployCredentials cred) =>
        {
            var account = input.ToCredential(input.Id ?? 0);
            var computer = input.Computer?.Trim();
            if (account.IsLocal && string.IsNullOrEmpty(computer))
                return Results.BadRequest(new { error = "Indiquez le poste sur lequel essayer ce compte local." });
            try
            {
                var password = input.Password;
                if (string.IsNullOrEmpty(password) && input.Id is { } id && cred.Get(id) is { } stored) password = cred.Password(stored);
                if (string.IsNullOrEmpty(password)) return Results.BadRequest(new { error = "Saisissez le mot de passe." });
                if (account.IsLocal) DeployCredentials.TestOnComputer(account, password, computer!);
                else ActiveDirectory.TestConnection(account, password);
                return Results.Ok(new { ok = true });
            }
            catch (DeployException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

record CredentialInput(long? Id, string? Domain, string User, string? Description, string? Password, string? Computer = null)
{
    public Credential ToCredential(long id) => new Credential { Id = id, Domain = Domain, User = User ?? "", Description = Description }.Normalize();
}
