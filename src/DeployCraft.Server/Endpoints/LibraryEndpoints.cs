using DeployCraft.Server.Data;
using DeployCraft.Server.Library;

namespace DeployCraft.Server.Endpoints;

/// <summary>Package library built from the public catalog.</summary>
internal static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/library", (string? search, PackageLibrary library) => library.Info(search));

        api.MapGet("/library/status", (PackageLibrary library) => library.Status());

        api.MapPost("/library/refresh", (PackageLibrary library) =>
        {
            library.StartRefresh();
            return Results.NoContent();
        });

        api.MapPut("/library/folder", (LibraryFolderInput input, PackageLibrary library) =>
            library.SetFolder(input.Folder) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

        api.MapPut("/library/auto-update", (LibraryAutoUpdateInput input, PackageLibrary library) =>
            library.SetAutoUpdate(input.Mode, input.Days) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

        // The software's entry is read right away (refusal explained); the download continues in the background.
        api.MapPost("/library/download", async (LibraryDownloadInput input, PackageLibrary library) =>
            await library.StartDownloadAsync(input.Id ?? "") is { } error ? Results.BadRequest(new { error }) : Results.NoContent());
    }
}

record LibraryFolderInput(string? Folder);
record LibraryAutoUpdateInput(LibraryAutoUpdate Mode, int Days);
record LibraryDownloadInput(string? Id);
