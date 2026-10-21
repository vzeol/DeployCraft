using System.ComponentModel;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Deployment;

/// <summary>Translates technical errors into understandable messages.</summary>
public static class ErrorMessages
{
    // Message keys, translated when read.
    // Message keys, translated when read.
    private static readonly Dictionary<int, string> ExitCodes = new()
    {
        [1602] = "ErrorMessages_Exit1602",
        [1603] = "ErrorMessages_Exit1603",
        [1605] = "ErrorMessages_Exit1605",
        [1618] = "ErrorMessages_Exit1618",
        [1619] = "ErrorMessages_Exit1619",
        [1620] = "ErrorMessages_Exit1620",
        [1625] = "ErrorMessages_Exit1625",
        [1633] = "ErrorMessages_Exit1633",
        [1638] = "ErrorMessages_Exit1638",
        [1639] = "ErrorMessages_Exit1639",
        [unchecked((int)0x80240017)] = "ErrorMessages_ExitWuNotApplicable",
    };

    public static string ForStep(StepOutcome step)
    {
        if (!string.IsNullOrEmpty(step.Error)) return Msg.Format("ErrorMessages_StepError", step.Name, step.Error);
        var detail = step.ExitCode is int code && ExitCodes.TryGetValue(code, out var known) ? " — " + Msg.Get(known) : "";
        return Msg.Format("ErrorMessages_StepExitCode", step.Name, step.ExitCode, detail);
    }

    public static string ForException(Exception ex) => ex switch
    {
        DeployException d => d.Message,
        Win32Exception { NativeErrorCode: 5 } =>
            Msg.Get("ErrorMessages_ScmDenied"),
        Win32Exception { NativeErrorCode: 1722 or 1753 } =>
            Msg.Get("ErrorMessages_ScmUnreachable"),
        Win32Exception { NativeErrorCode: 1326 } =>
            Msg.Get("ErrorMessages_BadLogon"),
        Win32Exception w => w.Message,
        _ => ex.Message,
    };
}
