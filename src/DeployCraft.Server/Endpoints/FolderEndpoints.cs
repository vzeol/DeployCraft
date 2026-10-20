using DeployCraft.Server.Data;

namespace DeployCraft.Server.Endpoints;

/// <summary>Package folders and moving packages between them.</summary>
internal static class FolderEndpoints
{
    public static void MapFolderEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/folders", (Store store) => store.ListFolders());

        api.MapPost("/folders", (PackageFolder f, Store store) =>
        {
            f.Id = 0;
            if (PackageFolder.Check(f, store.ListFolders()) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(store.SaveFolder(f));
        });

        api.MapPut("/folders/{id:long}", (long id, PackageFolder f, Store store) =>
        {
            var all = store.ListFolders();
            if (all.All(x => x.Id != id)) return Results.NotFound();
            f.Id = id;
            if (PackageFolder.Check(f, all) is { } error) return Results.BadRequest(new { error });
            return Results.Ok(store.SaveFolder(f));
        });

        api.MapDelete("/folders/{id:long}", (long id, Store store) =>
        {
            if (PackageFolder.CheckDelete(id, store.ListFolders(), store.ListPackages()) is { } error) return Results.BadRequest(new { error });
            store.DeleteFolder(id);
            return Results.NoContent();
        });

        api.MapPut("/packages/{id:long}/folder", (long id, PackageMove move, Store store) =>
        {
            if (store.GetPackage(id) == null) return Results.NotFound();
            if (move.FolderId is { } folder && store.ListFolders().All(f => f.Id != folder))
                return Results.BadRequest(new { error = "Dossier introuvable." });
            store.MovePackage(id, move.FolderId);
            return Results.NoContent();
        });
    }
}

record PackageMove(long? FolderId);
