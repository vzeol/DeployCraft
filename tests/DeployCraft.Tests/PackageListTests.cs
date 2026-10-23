using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class PackageListTests
{
    internal static PackageList List(List<Package>? packages = null, List<PackageFolder>? folders = null) =>
        new(() => Task.FromResult(folders ?? []), () => Task.FromResult(packages ?? []),
            () => Task.FromResult(new DeploymentDefaults()), () => Task.FromResult(new LibraryInfo()));

    [Fact]
    public async Task Refresh_ReloadsInOrderAndKeepsOptionalDataOnApiFailure()
    {
        var calls = new List<string>();
        var fail = false;
        var defaults = new DeploymentDefaults();
        var list = new PackageList(
            () => { calls.Add("folders"); return Task.FromResult(new List<PackageFolder>()); },
            () => { calls.Add("packages"); return Task.FromResult(new List<Package> { new() { Id = fail ? 2 : 1 } }); },
            () => { calls.Add("defaults"); return fail ? Task.FromException<DeploymentDefaults>(new ApiException("Unavailable")) : Task.FromResult(defaults); },
            () =>
            {
                calls.Add("library");
                return fail ? Task.FromException<LibraryInfo>(new ApiException("Unavailable"))
                    : Task.FromResult(new LibraryInfo { Items = [new() { PackageId = 2, UpdateAvailable = true }] });
            });

        await list.RefreshAsync();
        fail = true;
        await list.RefreshAsync();

        Assert.Equal(["folders", "packages", "defaults", "library", "folders", "packages", "defaults", "library"], calls);
        Assert.Same(defaults, list.Defaults);
        Assert.Equal(2, Assert.Single(list.Packages).Id);
        Assert.True(Assert.Single(list.Tree("")).UpdateAvailable);
    }

    [Theory]
    [InlineData("folders")]
    [InlineData("packages")]
    [InlineData("defaults")]
    [InlineData("library")]
    public async Task Refresh_StopsAtRequiredOrUnexpectedFailure(string stage)
    {
        var calls = new List<string>();
        var error = new InvalidOperationException("Unexpected failure");
        Task<T> Load<T>(string name, T value)
        {
            calls.Add(name);
            return name == stage ? Task.FromException<T>(error) : Task.FromResult(value);
        }
        var list = new PackageList(() => Load("folders", new List<PackageFolder>()),
            () => Load("packages", new List<Package>()), () => Load("defaults", new DeploymentDefaults()),
            () => Load("library", new LibraryInfo()));

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(list.RefreshAsync));
        Assert.Equal(stage, calls.Last());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequiredApiFailure_PropagatesWithoutLoadingOptionalData(bool folderFailure)
    {
        var error = new ApiException("Unavailable");
        var list = new PackageList(
            () => folderFailure ? Task.FromException<List<PackageFolder>>(error) : Task.FromResult(new List<PackageFolder>()),
            () => Task.FromException<List<Package>>(error),
            () => throw new InvalidOperationException("Defaults must not load."),
            () => throw new InvalidOperationException("Library must not load."));

        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(list.RefreshAsync));
    }

    [Fact]
    public async Task FirstOptionalFailure_LeavesFactoryDefaultsAndNoMarkers()
    {
        var list = new PackageList(() => Task.FromResult(new List<PackageFolder>()),
            () => Task.FromResult(new List<Package> { new() { Id = 1 } }),
            () => Task.FromException<DeploymentDefaults>(new ApiException("Unavailable")),
            () => Task.FromException<LibraryInfo>(new ApiException("Unavailable")));

        await list.RefreshAsync();

        Assert.Null(list.Defaults);
        Assert.False(Assert.Single(list.Tree("")).UpdateAvailable);
    }

    [Fact]
    public async Task Tree_PreservesHierarchyOrderEmptyFoldersAndMissingFolderPackages()
    {
        var list = List([new() { Id = 1 }, new() { Id = 2, FolderId = 99 }, new() { Id = 3, FolderId = 20 }],
            [new() { Id = 10 }, new() { Id = 20, ParentId = 10 }, new() { Id = 30 }]);
        await list.RefreshFoldersAsync();
        list.ExpandedFolders.Add(10);

        var tree = list.Tree("", 3, 20);

        Assert.Equal(4, tree.Count);
        Assert.Equal(10, Assert.IsType<PackageFolder>(tree[0].Item).Id);
        Assert.True(tree[0].Expanded);
        var nested = Assert.Single(tree[0].Children);
        Assert.True(nested.Selected);
        Assert.False(nested.Expanded);
        Assert.True(Assert.Single(nested.Children).Selected);
        Assert.Empty(tree[1].Children);
        Assert.Equal([1L, 2L], tree.Skip(2).Select(n => ((Package)n.Item).Id));
    }

    [Fact]
    public async Task Filter_UsesTrimmedPackageNameOnlyAndExpandsAncestorsWithoutRememberingThem()
    {
        var list = List([new() { Id = 1, Name = "Tools", FolderId = 20 }, new() { Id = 2, Name = "Other", Description = "Tools" }],
            [new() { Id = 10 }, new() { Id = 20, ParentId = 10 }, new() { Id = 30, Name = "Tools" }]);
        await list.RefreshFoldersAsync();

        var root = Assert.Single(list.Tree("  tOoL  ", 1));

        Assert.True(root.Expanded);
        var nested = Assert.Single(root.Children);
        Assert.True(nested.Expanded);
        Assert.True(Assert.Single(nested.Children).Selected);
        Assert.Empty(list.ExpandedFolders);
        Assert.False(list.Tree("")[0].Expanded);
        Assert.Empty(list.Tree("missing", 1));
    }

    [Fact]
    public async Task LibraryMarkers_ChangeOnlyWhenTheSetChanges()
    {
        var list = List([new() { Id = 1 }, new() { Id = 2 }]);
        await list.RefreshFoldersAsync();
        var status = new LibraryInfo { Items = [new() { PackageId = 1, UpdateAvailable = true }, new() { PackageId = 2 }] };

        Assert.True(list.UpdateLibrary(status));
        Assert.False(list.UpdateLibrary(status));
        Assert.True(list.Tree("")[0].UpdateAvailable);
        Assert.False(list.Tree("")[1].UpdateAvailable);
        Assert.True(list.UpdateLibrary(new LibraryInfo()));
        Assert.All(list.Tree(""), n => Assert.False(n.UpdateAvailable));
    }

    [Fact]
    public async Task AfterSaved_ReloadsPackagesOnlyAndFindsFreshSelection()
    {
        var fresh = new Package { Id = 7, Name = "Fresh", FolderId = 2 };
        var list = new PackageList(() => throw new InvalidOperationException("Folders must not load."),
            () => Task.FromResult(new List<Package> { fresh }),
            () => throw new InvalidOperationException("Defaults must not load."),
            () => throw new InvalidOperationException("Library must not load."));

        Assert.Same(fresh, await list.AfterSavedAsync(new Package { Id = 7, FolderId = 2 }));
        Assert.Contains(2, list.ExpandedFolders);
        Assert.True(Assert.Single(list.Tree("", 7)).Selected);
        Assert.Null(await list.AfterSavedAsync(new Package { Id = 99 }));
        Assert.Null(list.Find(99));
    }

    [Fact]
    public async Task Drop_RejectsSameFolderCyclesAndNonPackageTargets()
    {
        var parent = new PackageFolder { Id = 10 };
        var child = new PackageFolder { Id = 20, ParentId = 10 };
        var package = new Package { FolderId = 20 };
        var list = List(folders: [parent, child]);
        await list.RefreshFoldersAsync();

        Assert.False(list.DropFolder(parent, parent, false, out _));
        Assert.False(list.DropFolder(parent, child, false, out _));
        Assert.False(list.DropFolder(parent, package, false, out _));
        Assert.False(list.DropFolder(package, child, false, out _));
        Assert.False(list.DropFolder(package, "home", false, out _));
        Assert.False(list.DropFolder(null, child, false, out _));
        Assert.False(list.DropFolder(parent, null, true, out _));
        Assert.True(list.DropFolder(child, null, true, out var root));
        Assert.Null(root);
        Assert.True(list.DropFolder(package, parent, false, out var target));
        Assert.Equal(10, target);
        Assert.Equal(20, PackageList.CurrentFolder(package));
        Assert.Equal(20, PackageList.CurrentFolder(child));
        Assert.Null(PackageList.CurrentFolder(null));
    }
}
