namespace DeployCraft.Server.Data;

/// <summary>
/// Opens the database file and builds one repository per area. Only used to wire things together (the service,
/// the tests): services and endpoints receive the repository interfaces they need.
/// </summary>
public sealed class Store
{
    public Store(string path)
    {
        var database = new Database(path);
        Settings = new SettingsRepository(database);
        Packages = new PackageRepository(database, Settings);
        Folders = new FolderRepository(database);
        Computers = new ComputerRepository(database);
        Deployments = new DeploymentRepository(database);
        Schedules = new ScheduleRepository(database);
        Credentials = new CredentialRepository(database);
    }

    public ISettingsRepository Settings { get; }
    public IPackageRepository Packages { get; }
    public IFolderRepository Folders { get; }
    public IComputerRepository Computers { get; }
    public IDeploymentRepository Deployments { get; }
    public IScheduleRepository Schedules { get; }
    public ICredentialRepository Credentials { get; }
}
