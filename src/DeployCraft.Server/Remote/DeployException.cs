namespace DeployCraft.Server.Remote;

/// <summary>Deployment error with a message readable by a human.</summary>
public class DeployException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The computer could not be reached: candidate for the retry queue.</summary>
    public bool Offline { get; init; }

    public static DeployException FromShareAccess(string computer, Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            new(Msg.Format("TargetSession_AdminShareDenied", computer), ex),
        IOException io when io.HResult is unchecked((int)0x80070035) or unchecked((int)0x80070079) or unchecked((int)0x80070040) =>
            new(Msg.Format("TargetSession_Unreachable", computer), ex) { Offline = true },
        IOException io when io.HResult == unchecked((int)0x8007052E) =>
            new(Msg.Get("TargetSession_BadCredential"), ex),
        _ => new(Msg.Format("TargetSession_CannotAccess", computer, ex.Message), ex),
    };
}
