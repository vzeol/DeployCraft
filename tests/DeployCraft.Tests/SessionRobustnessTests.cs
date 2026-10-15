using System.ComponentModel;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Suivi d'un poste : coupure réseau tolérée, verrou de slot laissé par un serveur arrêté.</summary>
public sealed class SessionRobustnessTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private const string Current = "SERVEUR-01|courant";

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
        Assert.True(TargetSession.IsNetworkError(new Win32Exception(1722))); // serveur RPC indisponible
        Assert.False(TargetSession.IsNetworkError(new DeployException("Délai global dépassé.")));
        Assert.False(TargetSession.IsNetworkError(new UnauthorizedAccessException()));
    }

    [Fact]
    public void Lock_LeftByAnEarlierRunOfThisServer_IsReclaimed()
    {
        Assert.True(TargetSession.CanReclaim(Lock("SERVEUR-01|ancien"), Slot, Current, DateTime.UtcNow));
    }

    [Fact]
    public void Lock_OfThisRunOrOfAnotherServer_IsKept()
    {
        Assert.False(TargetSession.CanReclaim(Lock(Current), Slot, Current, DateTime.UtcNow));
        Assert.False(TargetSession.CanReclaim(Lock("SERVEUR-02|autre"), Slot, Current, DateTime.UtcNow));
    }

    [Fact]
    public void Lock_WithoutOwner_IsReclaimedOnlyAfterADay()
    {
        var now = DateTime.UtcNow;
        Assert.False(TargetSession.CanReclaim(Lock("", now.AddHours(-2)), Slot, Current, now));
        Assert.True(TargetSession.CanReclaim(Lock("", now.AddDays(-2)), Slot, Current, now));
    }

    [Fact]
    public void Lock_IsKept_WhileARunnerStillRunsInTheSlot()
    {
        Directory.CreateDirectory(Slot);
        var exe = Path.Combine(Slot, "DeployCraftRunner-1.exe");
        File.WriteAllText(exe, "");
        var path = Lock("SERVEUR-01|ancien");

        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read)) // comme un programme lancé
            Assert.False(TargetSession.CanReclaim(path, Slot, Current, DateTime.UtcNow));
        Assert.True(TargetSession.CanReclaim(path, Slot, Current, DateTime.UtcNow));
    }
}
