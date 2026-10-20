using DeployCraft.Server.Security;

namespace DeployCraft.Tests;

/// <summary>Compte du service saisi dans Options › Service en arrière-plan.</summary>
public class ServiceAccountTests
{
    [Fact]
    public void DomainAccount()
    {
        var (domain, user, name, error) = ServiceAccount.Parse(@" DOMAINE\admin ");
        Assert.Null(error);
        Assert.Equal("DOMAINE", domain);
        Assert.Equal("admin", user);
        Assert.Equal(@"DOMAINE\admin", name);
    }

    [Fact]
    public void LocalAccount_UsesTheServerName()
    {
        var (domain, user, name, error) = ServiceAccount.Parse(@".\admin");
        Assert.Null(error);
        Assert.Equal(Environment.MachineName, domain);
        Assert.Equal("admin", user);
        Assert.Equal(@".\admin", name);
    }

    [Fact]
    public void PrincipalName_IsKeptWhole()
    {
        var (domain, user, _, error) = ServiceAccount.Parse("admin@domaine.local");
        Assert.Null(error);
        Assert.Null(domain);
        Assert.Equal("admin@domaine.local", user);
    }

    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData(@"\admin")]
    [InlineData(@"DOMAINE\")]
    [InlineData(@"A\B\C")]
    public void BadAccount_IsExplained(string account) => Assert.NotNull(ServiceAccount.Parse(account).Error);
}
