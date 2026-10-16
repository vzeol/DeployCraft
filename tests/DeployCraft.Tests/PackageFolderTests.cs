using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Dossiers de l'arbre des paquets : enregistrement, déplacements, règles, export.</summary>
public sealed class PackageFolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly Store _store;

    public PackageFolderTests() => _store = new Store(Path.Combine(_dir, "test.db"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static readonly List<PackageFolder> Tree =
    [
        new() { Id = 1, Name = "Bureautique" },
        new() { Id = 2, Name = "Navigateurs", ParentId = 1 },
        new() { Id = 3, Name = "Anciens", ParentId = 2 },
        new() { Id = 4, Name = "Pédagogie" },
    ];

    [Fact]
    public void IsWithin_FollowsTheParents()
    {
        Assert.True(PackageFolder.IsWithin(Tree, 3, 1));
        Assert.True(PackageFolder.IsWithin(Tree, 1, 1));
        Assert.False(PackageFolder.IsWithin(Tree, 4, 1));
        Assert.False(PackageFolder.IsWithin(Tree, null, 1));
    }

    [Fact]
    public void Check_RefusesEmptyName_UnknownParent_AndLoops()
    {
        Assert.NotNull(PackageFolder.Check(new() { Name = " " }, Tree));
        Assert.NotNull(PackageFolder.Check(new() { Name = "X", ParentId = 99 }, Tree));
        Assert.NotNull(PackageFolder.Check(new() { Id = 1, Name = "Bureautique", ParentId = 3 }, Tree));
        Assert.Null(PackageFolder.Check(new() { Id = 4, Name = "Pédagogie", ParentId = 3 }, Tree));
        Assert.Null(PackageFolder.Check(new() { Name = "Nouveau" }, Tree));
    }

    [Fact]
    public void CheckDelete_OnlyEmptyFolders()
    {
        Assert.NotNull(PackageFolder.CheckDelete(1, Tree, []));
        Assert.NotNull(PackageFolder.CheckDelete(4, Tree, [new Package { FolderId = 4 }]));
        Assert.Null(PackageFolder.CheckDelete(4, Tree, [new Package { FolderId = 1 }]));
    }

    [Fact]
    public void CopyName_KeepsTheName_WhenFree_ThenNumbersTheCopies()
    {
        Assert.Equal("Lecteur PDF", PackageFolder.CopyName("Lecteur PDF", ["Navigateur"]));
        Assert.Equal("Lecteur PDF (copie)", PackageFolder.CopyName("Lecteur PDF", ["lecteur pdf"]));
        Assert.Equal("Lecteur PDF (copie 2)", PackageFolder.CopyName("Lecteur PDF", ["Lecteur PDF", "Lecteur PDF (copie)"]));
    }

    [Fact]
    public void Folders_AreSavedRenamedAndDeleted()
    {
        var parent = _store.SaveFolder(new PackageFolder { Name = " Bureautique " });
        var child = _store.SaveFolder(new PackageFolder { Name = "Navigateurs", ParentId = parent.Id });
        _store.SaveFolder(new PackageFolder { Id = child.Id, Name = "Web", ParentId = null });

        var all = _store.ListFolders();
        Assert.Equal(["Bureautique", "Web"], all.Select(f => f.Name));
        Assert.All(all, f => Assert.Null(f.ParentId));

        _store.DeleteFolder(child.Id);
        Assert.Single(_store.ListFolders());
    }

    [Fact]
    public void Package_KeepsItsFolder_WhenSaved_AndChangesOnlyByAMove()
    {
        var a = _store.SaveFolder(new PackageFolder { Name = "A" });
        var b = _store.SaveFolder(new PackageFolder { Name = "B" });
        var package = _store.SavePackage(new Package { Name = "Lecteur PDF", FolderId = a.Id });
        Assert.Equal(a.Id, _store.GetPackage(package.Id)!.FolderId);

        // Fenêtre ouverte avant un déplacement : l'enregistrer ne remet pas le paquet dans son ancien dossier.
        _store.MovePackage(package.Id, b.Id);
        package.Description = "modifié";
        _store.SavePackage(package);
        Assert.Equal(b.Id, _store.GetPackage(package.Id)!.FolderId);

        _store.MovePackage(package.Id, null);
        Assert.Null(_store.GetPackage(package.Id)!.FolderId);
    }

    [Fact]
    public void Export_LeavesFoldersOut()
    {
        var export = PackageExchange.Export([1], _ => new Package { Id = 1, Name = "Lecteur PDF", FolderId = 5 });
        Assert.Null(export.Packages[0].FolderId);
    }
}
