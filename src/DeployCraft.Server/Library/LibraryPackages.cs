using DeployCraft.Server.Data;

namespace DeployCraft.Server.Library;

/// <summary>DeployCraft package matching a library software: created on the first download, updated on the next ones.</summary>
internal static class LibraryPackages
{
    /// <summary>
    /// Creates the package, or updates the one already linked: version, file and parameters of its install step.
    /// The other settings edited by hand (conditions, added steps...) are kept.
    /// </summary>
    public static Package Save(IPackageRepository packageRepository, IndexEntry app, string version, LibraryInstaller installer, string file, LibraryLink? previous)
    {
        if (previous != null && packageRepository.Get(previous.PackageId) is { } existing)
        {
            var step = existing.Steps.FirstOrDefault(s => s.Type == StepType.Install && string.Equals(s.File, previous.File, StringComparison.OrdinalIgnoreCase));
            if (step != null)
            {
                step.File = file;
                step.Arguments = NullIfEmpty(installer.Arguments);
                existing.Version = version;
                return packageRepository.Save(existing);
            }
        }
        return packageRepository.Save(new Package
        {
            Name = app.Name,
            Version = version,
            Description = Msg.Format("PackageLibrary_Description", app.Id),
            Steps =
            [
                new PackageStep
                {
                    Type = StepType.Install,
                    Name = Msg.Format("PackageLibrary_InstallStep", app.Name),
                    File = file,
                    Arguments = NullIfEmpty(installer.Arguments),
                },
            ],
        });
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
