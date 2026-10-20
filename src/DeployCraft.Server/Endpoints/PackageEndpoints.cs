using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;
using DeployCraft.Server.Validation;

namespace DeployCraft.Server.Endpoints;

/// <summary>Packages: create, edit, delete, export / import, remembered targets.</summary>
internal static class PackageEndpoints
{
    public static void MapPackageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/packages", (IPackageRepository packageRepository) => packageRepository.List());

        api.MapGet("/packages/{id:long}", (long id, IPackageRepository packageRepository) =>
            packageRepository.Get(id) is { } p ? Results.Ok(p) : Results.NotFound());

        api.MapPost("/packages", (Package p, IPackageRepository packageRepository, IFolderRepository folderRepository) =>
        {
            p.Id = 0;
            if (p.FolderId is { } folder && folderRepository.List().All(f => f.Id != folder)) p.FolderId = null;
            if (PackageValidation.Validate(p, packageRepository.Get) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(packageRepository.Save(p));
        });

        api.MapPut("/packages/{id:long}", (long id, Package p, IPackageRepository packageRepository) =>
        {
            if (packageRepository.Get(id) == null) return Results.NotFound();
            p.Id = id;
            if (PackageValidation.Validate(p, packageRepository.Get) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(packageRepository.Save(p));
        });

        api.MapDelete("/packages/{id:long}", (long id, IPackageRepository packageRepository) =>
        {
            if (Package.UsersOf(packageRepository.List(), id) is { Count: > 0 } users)
                return Results.BadRequest(new
                {
                    error = "Ce paquet est utilisé comme paquet imbriqué par : " + string.Join(", ", users.Select(u => $"« {u.Name} »")) +
                            ". Retirez-le de ces paquets avant de le supprimer.",
                });
            packageRepository.Delete(id);
            return Results.NoContent();
        });

        // Avertissements de la fenêtre du paquet : chemins introuvables depuis le serveur, lus comme pour un déploiement
        // (partage ouvert avec l'identifiant par défaut). Une vérification impossible (identifiant refusé…) ne signale rien.
        api.MapPost("/files/missing", (List<string> paths, DeployCredentials cred) =>
        {
            var account = cred.Default;
            return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Where(path =>
            {
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
                    var host = expanded.StartsWith(@"\\") ? expanded[2..].Split('\\')[0] : null;
                    return !cred.RunAs(account, host, () => File.Exists(expanded) || Directory.Exists(expanded));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    return false;
                }
            }).ToList();
        });

        // Export : les paquets choisis et les paquets imbriqués qu'ils utilisent, dans un fichier qui se suffit à lui-même.
        api.MapPost("/packages/export", (List<long> ids, IPackageRepository packageRepository) =>
        {
            try
            {
                return Results.Ok(PackageExchange.Export(ids, packageRepository.Get));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Import d'un fichier de paquets (.json ou .xml) : aperçu (Apply = false) ou création des paquets.
        api.MapPost("/packages/import", (ImportInput input, IPackageRepository packageRepository) =>
        {
            ImportReport report;
            try
            {
                report = PackageExchange.Read(input.Content ?? "", input.Repository);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            PackageExchange.Complete(report, packageRepository.List(), PackageValidation.Validate, input.Apply ? packageRepository.Save : null);
            return Results.Ok(report);
        });

        // « Mémoriser ces cibles » : la fenêtre Déployer une fois les propose à nouveau pour ce paquet.
        api.MapGet("/packages/{id:long}/targets", (long id, IPackageRepository packageRepository) => packageRepository.GetTargets(id));

        api.MapPut("/packages/{id:long}/targets", (long id, List<string> computers, IPackageRepository packageRepository) =>
        {
            if (packageRepository.Get(id) == null) return Results.NotFound();
            packageRepository.SetTargets(id, computers.Select(c => c.Trim()).Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            return Results.NoContent();
        });
    }
}

record ImportInput(string? Content, string? Repository, bool Apply);
