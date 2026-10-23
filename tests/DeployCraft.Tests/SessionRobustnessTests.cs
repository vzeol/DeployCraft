using System.ComponentModel;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Monitoring of a computer: network outage tolerated, slot lock left by a stopped server.</summary>
public sealed class SessionRobustnessTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private const string Current = "SERVEUR-01|100|1";
    private static readonly Func<string, bool> NoneAlive = _ => false;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Lock(string owner, DateTime? written = null)
    {
        var path = Path.Combine(_dir, "slot-1.lock");
        File.WriteAllText(path, owner);
        if (written != null) File.SetLastWriteTimeUtc(path, written.Value);
        return path;
    }

    private string Slot => Path.Combine(_dir, "slot-1");

    [Fact]
    public void NetworkErrors_AreRetried_OthersAreNot()
    {
        Assert.True(TargetSession.IsNetworkError(new IOException("Le chemin réseau n'a pas été trouvé.")));
        Assert.True(TargetSession.IsNetworkError(new Win32Exception(1722))); // RPC server unavailable
        Assert.False(TargetSession.IsNetworkError(new DeployException("Délai global dépassé.")));
        Assert.False(TargetSession.IsNetworkError(new UnauthorizedAccessException()));
    }

    [Fact]
    public void Lock_LeftByAnEarlierRunOfThisServer_IsReclaimed()
    {
        Assert.True(SlotLock.CanReclaim(Lock("SERVEUR-01|90|1"), Slot, Current, DateTime.UtcNow, NoneAlive));
    }

    [Fact]
    public void Lock_OfAnotherServerStillRunningOnThisMachine_IsKept()
    {
        // For example a test instance started next to the service.
        Assert.False(SlotLock.CanReclaim(Lock("SERVEUR-01|90|1"), Slot, Current, DateTime.UtcNow, owner => owner == "SERVEUR-01|90|1"));
    }

    [Fact]
    public void Lock_OfThisRunOrOfAnotherServer_IsKept()
    {
        Assert.False(SlotLock.CanReclaim(Lock(Current), Slot, Current, DateTime.UtcNow, NoneAlive));
        Assert.False(SlotLock.CanReclaim(Lock("SERVEUR-02|90|1"), Slot, Current, DateTime.UtcNow, NoneAlive));
    }

    [Fact]
    public void Lock_WithoutOwner_IsReclaimedOnlyAfterADay()
    {
        var now = DateTime.UtcNow;
        Assert.False(SlotLock.CanReclaim(Lock("", now.AddHours(-2)), Slot, Current, now, NoneAlive));
        Assert.True(SlotLock.CanReclaim(Lock("", now.AddDays(-2)), Slot, Current, now, NoneAlive));
    }

    [Fact]
    public void Lock_IsKept_WhileARunnerStillRunsInTheSlot()
    {
        Directory.CreateDirectory(Slot);
        var exe = Path.Combine(Slot, "DeployCraftRunner-1.exe");
        File.WriteAllText(exe, "");
        var path = Lock("SERVEUR-01|90|1");

        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read)) // like a running program
            Assert.False(SlotLock.CanReclaim(path, Slot, Current, DateTime.UtcNow, NoneAlive));
        Assert.True(SlotLock.CanReclaim(path, Slot, Current, DateTime.UtcNow, NoneAlive));
    }
}
