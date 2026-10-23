using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class PackageTreeCommandsTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private static PackageTreeCommands Commands(Func<long, long?, Task>? move = null,
        Func<PackageFolder, Task<PackageFolder>>? saveFolder = null) => new(
        _ => throw new InvalidOperationException("Package must not load."),
        _ => throw new InvalidOperationException("Package must not save."),
        move ?? ((_, _) => throw new InvalidOperationException("Package must not move.")),
        saveFolder ?? (_ => throw new InvalidOperationException("Folder must not save.")));

    [Fact]
    public async Task Clipboard_RequiresConnectionAndPackageTreeTargetAndIgnoresFolderCopy()
    {
        var commands = Commands();
        Assert.False(commands.CanPaste(true, null, true));
        commands.CutOrCopy(new PackageFolder { Id = 1 }, false);
        Assert.False(commands.CanPaste(true, null, true));
        Assert.Null(await commands.PasteAsync(PackageListTests.List(), null));

        commands.CutOrCopy(new Package { Id = 2 }, true);
        commands.CutOrCopy(new PackageFolder { Id = 1 }, false);
        Assert.True(commands.CanPaste(true, null, true));
        Assert.True(commands.CanPaste(true, new PackageFolder(), false));
        Assert.True(commands.CanPaste(true, new Package(), false));
        Assert.False(commands.CanPaste(false, new Package(), false));
        Assert.False(commands.CanPaste(true, "home", false));
    }

    [Theory]
    [InlineData("fr", "Tools (copie)", "Tools (copie 2)")]
    [InlineData("en", "Tools (copy)", "Tools (copy 2)")]
    public async Task Copy_UsesTargetFolderNamesAndKeepsClipboard(string language, string existing, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var list = PackageListTests.List([new() { Name = "Tools", FolderId = 10 }, new() { Name = existing, FolderId = 10 }, new() { Name = expected, FolderId = 20 }]);
        await list.RefreshFoldersAsync();
        var saved = new List<Package>();
        var commands = new PackageTreeCommands(
            id => Task.FromResult(new Package { Id = id, Name = "Tools", FolderId = 20, Steps = [new() { Type = StepType.Command }] }),
            p => { saved.Add(p); return Task.FromResult(new Package { Id = 8 }); },
            (_, _) => throw new InvalidOperationException("Copy must not move."),
            _ => throw new InvalidOperationException("Copy must not save a folder."));
        commands.CutOrCopy(new Package { Id = 3 }, false);

        Assert.Equal(new PackageTreeSelection(8, null), await commands.PasteAsync(list, 10));
        Assert.Equal(new PackageTreeSelection(8, null), await commands.PasteAsync(list, 10));
        Assert.Equal(2, saved.Count);
        Assert.All(saved, p =>
        {
            Assert.Equal(0, p.Id);
            Assert.Equal(10, p.FolderId);
            Assert.Equal(expected, p.Name);
            Assert.Equal(StepType.Command, Assert.Single(p.Steps).Type);
        });
        Assert.Contains(10, list.ExpandedFolders);
        Assert.True(commands.CanPaste(true, null, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cut_MovesAndClearsClipboardOnlyAfterSuccess(bool folder)
    {
        var list = PackageListTests.List(folders: [new() { Id = 3, Name = "Tools" }]);
        await list.RefreshFoldersAsync();
        var fail = true;
        var calls = 0;
        Task Move(long id, long? target)
        {
            Assert.Equal(3, id);
            Assert.Equal(10, target);
            calls++;
            return fail ? Task.FromException(new ApiException("Unavailable")) : Task.CompletedTask;
        }
        var commands = Commands(Move, async f => { await Move(f.Id, f.ParentId); Assert.Equal("Tools", f.Name); return f; });
        commands.CutOrCopy(folder ? new PackageFolder { Id = 3 } : new Package { Id = 3 }, true);

        await Assert.ThrowsAsync<ApiException>(() => commands.PasteAsync(list, 10));
        Assert.True(commands.CanPaste(true, null, true));
        Assert.Empty(list.ExpandedFolders);
        fail = false;
        Assert.Equal(folder ? new PackageTreeSelection(null, 3) : new PackageTreeSelection(3, null), await commands.PasteAsync(list, 10));
        Assert.False(commands.CanPaste(true, null, true));
        Assert.Contains(10, list.ExpandedFolders);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CutFolder_MissingOrUnchangedFolderKeepsClipboardWithoutSaving()
    {
        var list = PackageListTests.List(folders: [new() { Id = 3 }]);
        await list.RefreshFoldersAsync();
        var commands = Commands();
        commands.CutOrCopy(new PackageFolder { Id = 3 }, true);
        Assert.Null(await commands.PasteAsync(list, null));
        commands.CutOrCopy(new PackageFolder { Id = 99 }, true);
        Assert.Null(await commands.PasteAsync(list, 10));
        Assert.True(commands.CanPaste(true, null, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Drop_MovesItemAndReturnsSelectionWithoutClearingClipboard(bool folder)
    {
        var list = PackageListTests.List();
        var calls = 0;
        var commands = Commands((id, target) =>
        {
            Assert.Equal(3, id);
            Assert.Equal(10, target);
            calls++;
            return Task.CompletedTask;
        }, f =>
        {
            Assert.Equal(3, f.Id);
            Assert.Equal("Tools", f.Name);
            Assert.Equal(10, f.ParentId);
            calls++;
            return Task.FromResult(f);
        });
        commands.CutOrCopy(new Package { Id = 4 }, false);

        var selection = await commands.MoveAsync(list,
            folder ? new PackageFolder { Id = 3, Name = "Tools" } : new Package { Id = 3 }, 10);

        Assert.Equal(folder ? new PackageTreeSelection(null, 3) : new PackageTreeSelection(3, null), selection);
        Assert.Equal(1, calls);
        Assert.Contains(10, list.ExpandedFolders);
        Assert.True(commands.CanPaste(true, null, true));
    }
}
