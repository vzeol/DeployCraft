using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public class SharedModelsTests
{
    [Fact]
    public void ConsoleAndService_UseTheSamePackageType()
    {
        Assert.Equal(typeof(Task<Package>), typeof(Api).GetMethod(nameof(Api.Package))!.ReturnType);
        Assert.Equal(typeof(Package), typeof(IPackageRepository).GetMethod(nameof(IPackageRepository.Get))!.ReturnType);
        Assert.Equal("DeployCraft.Shared", typeof(Package).Assembly.GetName().Name);
        Assert.DoesNotContain(typeof(Api).Assembly.GetTypes(), t => t.FullName == typeof(Package).FullName);
        Assert.DoesNotContain(typeof(IPackageRepository).Assembly.GetTypes(), t => t.FullName == typeof(Package).FullName);
    }

    [Fact]
    public void SharedAssembly_HasNoConsoleServerOrStorageDependency()
    {
        var references = typeof(Package).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("DeployCraft.Server", references);
        Assert.DoesNotContain("DeployCraft", references);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", references);
        Assert.DoesNotContain("PresentationFramework", references);
    }
}
