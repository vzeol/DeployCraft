using static DeployCraft.Server.Remote.Win32;

namespace DeployCraft.Server.Remote;

/// <summary>Creates, starts and deletes the runner's temporary service on a target.</summary>
internal sealed class ServiceControl : IDisposable
{
    private readonly IntPtr _scm;

    /// <param name="machine">null for the local machine.</param>
    public ServiceControl(string? machine)
    {
        _scm = OpenSCManager(machine == null ? null : @"\\" + machine, null, SC_MANAGER_ALL_ACCESS);
        if (_scm == IntPtr.Zero) throw LastError(Msg.Get("ServiceControl_OpenManager"));
    }

    /// <param name="account">null = local System; otherwise DOMAIN\user (or .\user) with its password.</param>
    public void CreateAndStart(string name, string binaryPath, string? account = null, string? password = null)
    {
        // A service with the same name may remain from an interrupted deployment: remove it first.
        Remove(name);

        var service = CreateService(_scm, name, name, SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL, $"\"{binaryPath}\"", null, IntPtr.Zero, null, account, password);
        if (service == IntPtr.Zero)
        {
            var error = LastError(Msg.Get("ServiceControl_Create"));
            if (error.NativeErrorCode == ERROR_INVALID_SERVICE_ACCOUNT)
                throw new DeployException(Msg.Format("ServiceControl_AccountRefused", account), error);
            throw error;
        }
        try
        {
            if (!StartService(service, 0, IntPtr.Zero))
            {
                var error = LastError(Msg.Get("ServiceControl_Start"));
                if (error.NativeErrorCode == ERROR_SERVICE_LOGON_FAILED)
                    throw new DeployException(
                        Msg.Format("ServiceControl_LogonFailed", account), error);
                throw error;
            }
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }

    public bool IsRunning(string name)
    {
        var service = OpenService(_scm, name, SERVICE_ALL_ACCESS);
        if (service == IntPtr.Zero)
        {
            // Only "service not found" means stopped; any other error (computer not answering) bubbles up.
            var error = LastError(Msg.Get("ServiceControl_ReadState"));
            if (error.NativeErrorCode == ERROR_SERVICE_DOES_NOT_EXIST) return false;
            throw error;
        }
        try
        {
            if (!QueryServiceStatus(service, out var status)) throw LastError(Msg.Get("ServiceControl_ReadState"));
            return status.dwCurrentState != SERVICE_STOPPED;
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }

    /// <summary>Stops then deletes the service. Silent if it does not exist.</summary>
    public void Remove(string name)
    {
        var service = OpenService(_scm, name, SERVICE_ALL_ACCESS);
        if (service == IntPtr.Zero) return;
        try
        {
            ControlService(service, SERVICE_CONTROL_STOP, out _);
            // Up to 20 s: on cancellation, the runner must first kill the running installer.
            for (var i = 0; i < 80 && QueryServiceStatus(service, out var s) && s.dwCurrentState != SERVICE_STOPPED; i++)
                Thread.Sleep(250);
            if (!DeleteService(service))
            {
                var error = LastError(Msg.Get("ServiceControl_Delete"));
                if (error.NativeErrorCode != ERROR_SERVICE_MARKED_FOR_DELETE) throw error;
            }
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }

    public void Dispose() => CloseServiceHandle(_scm);
}
