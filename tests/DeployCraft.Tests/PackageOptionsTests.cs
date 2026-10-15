using System.Net;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Valeurs par défaut (étape → paquet → Préférences), conditions transmises au runner, Wake-on-LAN, stockage.</summary>
public sealed class PackageOptionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly Store _store;

    public PackageOptionsTests() => _store = new Store(Path.Combine(_dir, "test.db"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static Package TwoSteps() => new()
    {
        Name = "Outil",
        Steps = [new() { Name = "a" }, new() { Name = "b", TimeoutMinutes = 5 }],
    };

    [Fact]
    public void Resolve_TakesPreferencesForEveryDefault()
    {
        var defaults = new DeploymentDefaults
        {
            RunAs = RunAsMode.LocalSystem,
            CopyMode = CopyMode.Pull,
            TimeoutMinutes = 45,
            Offline = new OfflineSettings { Retry = true, RetryAttempts = 3 },
        };

        var resolved = TwoSteps().Resolve(defaults);

        Assert.Equal(RunAsMode.LocalSystem, resolved.RunAs);
        Assert.Equal(CopyMode.Pull, resolved.CopyMode);
        Assert.Equal(45, resolved.Steps[0].TimeoutMinutes);
        Assert.Equal(5, resolved.Steps[1].TimeoutMinutes);
        Assert.True(resolved.Offline!.Retry);
        Assert.Equal(3, resolved.Offline.RetryAttempts);
    }

    [Fact]
    public void Resolve_KeepsPackageValues_AndLeavesTheOriginalUntouched()
    {
        var package = TwoSteps();
        package.RunAs = RunAsMode.LoggedOnUser;
        package.TimeoutMinutes = 20;
        package.Offline = new OfflineSettings { WakeOnLan = true };

        var resolved = package.Resolve(new DeploymentDefaults { Offline = new OfflineSettings { Retry = true } });

        Assert.Equal(RunAsMode.LoggedOnUser, resolved.RunAs);
        Assert.Equal(20, resolved.Steps[0].TimeoutMinutes);
        Assert.True(resolved.Offline!.WakeOnLan);
        Assert.False(resolved.Offline.Retry);
        Assert.Null(package.CopyMode);
        Assert.Null(package.Steps[0].TimeoutMinutes);
    }

    [Fact]
    public void RunnerConditions_NullWhenEmpty_AndFlattened()
    {
        Assert.Null(RunnerConditions.From(new Conditions()));

        var r = RunnerConditions.From(new Conditions
        {
            Windows = WindowsVersions.Windows10 | WindowsVersions.Windows11,
            Architecture = ArchitectureCondition.X64,
            LoggedOn = LoggedOnCondition.NotLoggedOn,
            File = PresenceCondition.NotExists,
            FilePath = @"C:\Outil\outil.exe",
            Registry = PresenceCondition.Exists,
            RegistryKey = @"HKLM\SOFTWARE\Outil",
            RegistryValue = " ",
        })!;

        Assert.Equal(["10", "11"], r.Windows);
        Assert.Equal("x64", r.Architecture);
        Assert.False(r.LoggedOn);
        Assert.False(r.FileExists);
        Assert.True(r.RegistryExists);
        Assert.Null(r.RegistryValue);
    }

    [Fact]
    public void MagicPacket_SixFFThenSixteenTimesTheMac()
    {
        var packet = WakeOnLan.MagicPacket("00-11-22-AA-BB-CC");

        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++)
            Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC }, packet[(6 + i * 6)..(12 + i * 6)]);
        Assert.Equal(packet, WakeOnLan.MagicPacket("00:11:22:aa:bb:cc"));
        Assert.Throws<ArgumentException>(() => WakeOnLan.MagicPacket("00-11-22"));
    }

    [Fact]
    public void WakeDestinations_GeneralThenSubnetBroadcasts()
    {
        var list = WakeOnLan.Destinations([new("00-11-22-AA-BB-CC", "192.168.10.255"), new("00-11-22-AA-BB-CD", null), new("00-11-22-AA-BB-CE", "192.168.10.255")]);
        Assert.Equal([IPAddress.Broadcast, IPAddress.Parse("192.168.10.255")], list);
    }

    [Fact]
    public void Package_RoundTrip_WithDefaultsConditionsAndOffline()
    {
        var package = TwoSteps();
        package.Version = "2.4";
        package.Conditions = new Conditions { Architecture = ArchitectureCondition.X64, File = PresenceCondition.NotExists, FilePath = @"C:\Outil\outil.exe" };
        package.Steps[1].Conditions = new Conditions { MinPowerShell = 5 };
        package.Offline = new OfflineSettings { Retry = true, RetryAttempts = 4, RetryIntervalMinutes = 10 };

        var saved = _store.GetPackage(_store.SavePackage(package).Id)!;

        Assert.Null(saved.RunAs);
        Assert.Null(saved.CopyMode);
        Assert.Null(saved.TimeoutMinutes);
        Assert.Equal("2.4", saved.Version);
        Assert.Equal(ArchitectureCondition.X64, saved.Conditions.Architecture);
        Assert.Equal(PresenceCondition.NotExists, saved.Conditions.File);
        Assert.Equal(5, saved.Steps[1].Conditions.MinPowerShell);
        Assert.Equal(4, saved.Offline!.RetryAttempts);

        saved.RunAs = RunAsMode.LocalSystem;
        saved.Offline = null;
        var again = _store.GetPackage(_store.SavePackage(saved).Id)!;
        Assert.Equal(RunAsMode.LocalSystem, again.RunAs);
        Assert.Null(again.Offline);
    }

    [Fact]
    public void Defaults_AndComputerNetwork_ArePersisted()
    {
        Assert.Equal(60, _store.GetDefaults().TimeoutMinutes);
        _store.SaveDefaults(new DeploymentDefaults { TimeoutMinutes = 30, Offline = new OfflineSettings { WakeOnLan = true } });
        Assert.Equal(30, _store.GetDefaults().TimeoutMinutes);
        Assert.True(_store.GetDefaults().Offline.WakeOnLan);

        Assert.Empty(_store.GetComputerNetwork("PC-TEST-01"));
        _store.SaveComputerNetwork("PC-TEST-01", [new("00-11-22-AA-BB-CC", "192.168.10.255")]);
        Assert.Equal("00-11-22-AA-BB-CC", Assert.Single(_store.GetComputerNetwork("pc-test-01")).Mac);
    }
}
