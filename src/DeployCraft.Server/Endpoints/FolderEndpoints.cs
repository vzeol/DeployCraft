using DeployCraft.Server.Data;

namespace DeployCraft.Server.Endpoints;

/// <summary>Package folders and moving packages between them.</summary>
internal static class FolderEndpoints
{
    public static void MapFolderEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/folders", (IFolderRepository folderRepository) => folderRepository.List());

        api.MapPost("/folders", (PackageFolder f, IFolderRepository folderRepository) =>
        {
            f.Id = 0;
            if (PackageFolder.Check(f, folderRepository.List()) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(folderRepository.Save(f));
        });

        api.MapPut("/folders/{id:long}", (long id, PackageFolder f, IFolderRepository folderRepository) =>
        {
            var all = folderRepository.List();
            if (all.All(x => x.Id != id)) return Results.NotFound();
            f.Id = id;
            if (PackageFolder.Check(f, all) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(folderRepository.Save(f));
        });

        api.MapDelete("/folders/{id:long}", (long id, IPackageRepository packageRepository, IFolderRepository folderRepository) =>
        {
            if (PackageFolder.CheckDelete(id, folderRepository.List(), packageRepository.List()) is { } error) return Results.BadRequest(new { error });
            folderRepository.Delete(id);
            return Results.NoContent();
        });

        api.MapPut("/packages/{id:long}/folder", (long id, PackageMove move, IPackageRepository packageRepository, IFolderRepository folderRepository) =>
        {
            if (packageRepository.Get(id) == null) return Results.NotFound();
            if (move.FolderId is { } folder && folderRepository.List().All(f => f.Id != folder))
                return Results.BadRequest(new { error = "Dossier introuvable." });
            packageRepository.Move(id, move.FolderId);
            return Results.NoContent();
        });
    }
}

record PackageMove(long? FolderId);
