using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>"À propos" window: displayed texts and copied information.</summary>
public class AboutInfoTests
{
    [Fact]
    public void ServiceText_ShowsVersionAndMachine()
    {
        Assert.Equal("0.14.0 sur SERVEUR-01", AboutInfo.ServiceText("0.14.0", "SERVEUR-01", null));
    }

    [Fact]
    public void ServiceText_ShowsErrorWhenServiceUnreachable()
    {
        Assert.Equal("Service injoignable", AboutInfo.ServiceText("0.14.0", "SERVEUR-01", "Service injoignable"));
    }

    [Fact]
    public void Version_FallsBackWhenUnknown()
    {
        Assert.Equal("version inconnue", AboutInfo.Version(null));
        Assert.Equal("version inconnue sur SERVEUR-01", AboutInfo.ServiceText(" ", "SERVEUR-01", null));
    }

    [Fact]
    public void Summary_ListsVersionsAndEnvironment_SkipsEmptyAccount()
    {
        var text = AboutInfo.Summary("0.14.0", "0.14.0 sur SERVEUR-01", "", "Windows 10.0.26100", ".NET 10.0.0");

        Assert.Equal(string.Join(Environment.NewLine,
            "DeployCraft", "Console : 0.14.0", "Service : 0.14.0 sur SERVEUR-01", "Windows : Windows 10.0.26100", ".NET : .NET 10.0.0"), text);
    }

    [Fact]
    public void Summary_IncludesConnectedAccount()
    {
        var text = AboutInfo.Summary("0.14.0", "Accès refusé", "DOMAINE\\admin", "w", "n");

        Assert.Contains("Connecté en tant que : DOMAINE\\admin", text);
    }
}
