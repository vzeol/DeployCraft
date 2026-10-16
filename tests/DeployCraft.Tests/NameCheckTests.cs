using System.Net.Sockets;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Vérification des noms de postes saisis dans « Déployer une fois ».</summary>
public class NameCheckTests
{
    private static readonly Dictionary<string, string> Dns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PC-TEST-01"] = "PC-TEST-01.DOMAINE.LOCAL",
        ["localhost"] = "SERVEUR-01.DOMAINE.LOCAL",
    };

    private static NameCheck Checker(List<string>? asked = null) => new((host, _) =>
    {
        asked?.Add(host);
        return Dns.TryGetValue(host, out var full) ? Task.FromResult(full) : throw new SocketException((int)SocketError.HostNotFound);
    });

    [Fact]
    public async Task KnownName_GivesTheFullDnsName()
    {
        var result = await Checker().CheckAsync(" pc-test-01 ", default);
        Assert.True(result.Found);
        Assert.Equal("pc-test-01", result.Name);
        Assert.Equal("PC-TEST-01.DOMAINE.LOCAL", result.DnsName);
    }

    [Fact]
    public async Task FullNameInWrongDomain_IsFlagged_LikeTheDeployment()
    {
        var asked = new List<string>();
        var result = await Checker(asked).CheckAsync("PC-TEST-01.AUTRE.LOCAL", default);
        Assert.False(result.Found);
        Assert.Equal(["PC-TEST-01.AUTRE.LOCAL"], asked);
    }

    [Fact]
    public async Task UnknownName_IsFlagged_WithoutBlocking()
    {
        var result = await Checker().CheckAsync("PC-ABSENT", default);
        Assert.False(result.Found);
        Assert.Contains("Introuvable dans le DNS", result.Error);
    }

    [Fact]
    public async Task IpAddress_Dot_AndEmpty()
    {
        var checker = Checker();
        Assert.Equal("10.0.0.5", (await checker.CheckAsync("10.0.0.5", default)).DnsName);
        Assert.Equal("SERVEUR-01.DOMAINE.LOCAL", (await checker.CheckAsync(".", default)).DnsName);
        Assert.False((await checker.CheckAsync("  ", default)).Found);
    }

    [Fact]
    public async Task Answers_AreKeptAMinute()
    {
        var asked = new List<string>();
        var checker = Checker(asked);
        await checker.CheckAsync("PC-TEST-01", default);
        await checker.CheckAsync("pc-test-01", default);
        Assert.Single(asked);
    }
}
