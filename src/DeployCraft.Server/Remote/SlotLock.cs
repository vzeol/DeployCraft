namespace DeployCraft.Server.Remote;

/// <summary>
/// Slots on a computer: each deployment reserves one with a lock file (slot-N.lock) created exclusively. A lock left by
/// a server stopped mid-deployment is taken back once it is sure no runner is still using its slot.
/// </summary>
internal static class SlotLock
{
    private const int MaxSlots = 32;

    /// <summary>
    /// This server (machine, process and start time), written in slot locks: a lock left by a
    /// server stopped mid-deployment can then be taken over instead of blocking its slot forever.
    /// </summary>
    private static readonly string Instance = OwnerOf(System.Diagnostics.Process.GetCurrentProcess());

    private static string OwnerOf(System.Diagnostics.Process process) =>
        $"{Environment.MachineName}|{process.Id}|{process.StartTime.ToUniversalTime().Ticks}";

    /// <summary>Is the server that placed this lock still running on this machine?</summary>
    private static bool OwnerAlive(string owner)
    {
        var parts = owner.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[1], out var pid)) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return OwnerOf(process) == owner; // same number but another start: number reused
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false; // no more process with this number
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true; // cannot check: when in doubt, the lock is kept
        }
    }

    /// <summary>Reserves a free slot of <paramref name="root"/> using a lock file created exclusively; returns its number.</summary>
    public static int Acquire(string root)
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            var lockPath = Path.Combine(root, $"slot-{i}.lock");
            if (TryCreateLock(lockPath) || TryReclaim(lockPath, Path.Combine(root, $"slot-{i}"))) return i;
        }
        throw new DeployException(Msg.Format("TargetSession_TooManySlots", MaxSlots));
    }

    /// <summary>Two deployments of this server never take the same lock at the same time.</summary>
    private static readonly Lock ReclaimGate = new();

    private static bool TryReclaim(string lockPath, string slotDir)
    {
        lock (ReclaimGate)
        {
            if (!CanReclaim(lockPath, slotDir, Instance, DateTime.UtcNow, OwnerAlive)) return false;
            try { File.Delete(lockPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            return TryCreateLock(lockPath);
        }
    }

    private static bool TryCreateLock(string lockPath)
    {
        try
        {
            using var stream = new FileStream(lockPath, FileMode.CreateNew);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(Instance));
            return true;
        }
        catch (IOException)
        {
            return false; // slot taken
        }
    }

    /// <summary>
    /// A lock can be taken over if it was placed, on this server's machine, by a server that is no longer running (or,
    /// with no author, by an old version more than a day ago) and no runner is still running in its slot.
    /// Another server still running on the same machine (test instance) keeps its locks.
    /// </summary>
    internal static bool CanReclaim(string lockPath, string slotDir, string instance, DateTime nowUtc, Func<string, bool> ownerAlive)
    {
        try
        {
            var owner = File.ReadAllText(lockPath).Trim();
            var stale = owner.Length == 0
                ? nowUtc - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromDays(1)
                : owner != instance && owner.Split('|')[0].Equals(instance.Split('|')[0], StringComparison.OrdinalIgnoreCase)
                    && !ownerAlive(owner);
            return stale && !(Directory.Exists(slotDir) && Directory.EnumerateFiles(slotDir, "*.exe").Any(IsInUse));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A running program cannot be opened for exclusive writing.</summary>
    private static bool IsInUse(string file)
    {
        try
        {
            using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
