using System.ComponentModel;
using static DeployCraft.Server.Remote.Win32;

namespace DeployCraft.Server.Remote;

/// <summary>Crée, démarre et supprime le service temporaire du runner sur une cible.</summary>
internal sealed class ServiceControl : IDisposable
{
    private readonly IntPtr _scm;

    /// <param name="machine">null pour la machine locale.</param>
    public ServiceControl(string? machine)
    {
        _scm = OpenSCManager(machine == null ? null : @"\\" + machine, null, SC_MANAGER_ALL_ACCESS);
        if (_scm == IntPtr.Zero) throw LastError("Ouverture du gestionnaire de services");
    }

    /// <param name="account">null = Système local ; sinon DOMAINE\utilisateur (ou .\utilisateur) avec son mot de passe.</param>
    public void CreateAndStart(string name, string binaryPath, string? account = null, string? password = null)
    {
        // Un service du même nom peut rester d'un déploiement interrompu : on le retire d'abord.
        Remove(name);

        var service = CreateService(_scm, name, name, SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL, $"\"{binaryPath}\"", null, IntPtr.Zero, null, account, password);
        if (service == IntPtr.Zero)
        {
            var error = LastError("Création du service");
            if (error.NativeErrorCode == ERROR_INVALID_SERVICE_ACCOUNT)
                throw new DeployException($"Le poste refuse le compte de déploiement « {account} » pour le service temporaire (compte inconnu ?).", error);
            throw error;
        }
        try
        {
            if (!StartService(service, 0, IntPtr.Zero))
            {
                var error = LastError("Démarrage du service");
                if (error.NativeErrorCode == ERROR_SERVICE_LOGON_FAILED)
                    throw new DeployException(
                        $"Le service temporaire n'a pas pu ouvrir de session avec le compte « {account} » sur ce poste " +
                        "(mot de passe modifié depuis l'enregistrement, ou droit « Ouvrir une session en tant que service » " +
                        "retiré par une stratégie de groupe). Essayez « Exécuter en tant que : Système local ».", error);
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
            // Seul « service introuvable » veut dire arrêté ; une autre erreur (poste qui ne répond pas) remonte.
            var error = LastError("Lecture de l'état du service");
            if (error.NativeErrorCode == ERROR_SERVICE_DOES_NOT_EXIST) return false;
            throw error;
        }
        try
        {
            if (!QueryServiceStatus(service, out var status)) throw LastError("Lecture de l'état du service");
            return status.dwCurrentState != SERVICE_STOPPED;
        }
        finally
        {
            CloseServiceHandle(service);
        }
    }

    /// <summary>Arrête puis supprime le service. Silencieux s'il n'existe pas.</summary>
    public void Remove(string name)
    {
        var service = OpenService(_scm, name, SERVICE_ALL_ACCESS);
        if (service == IntPtr.Zero) return;
        try
        {
            ControlService(service, SERVICE_CONTROL_STOP, out _);
            // Jusqu'à 20 s : en cas d'annulation, le runner doit d'abord tuer l'installeur en cours.
            for (var i = 0; i < 80 && QueryServiceStatus(service, out var s) && s.dwCurrentState != SERVICE_STOPPED; i++)
                Thread.Sleep(250);
            if (!DeleteService(service))
            {
                var error = LastError("Suppression du service");
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
