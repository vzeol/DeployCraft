using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Textes de l'onglet Détails de la page d'un paquet.</summary>
public class PackageDetailsTests
{
    [Fact]
    public void FolderPath_FollowsTheTree()
    {
        List<PackageFolder> folders = [new() { Id = 1, Name = "Bureautique" }, new() { Id = 2, Name = "PDF", ParentId = 1 }];
        Assert.Equal("Paquets › Bureautique › PDF", PackageDetails.FolderPath(2, folders));
        Assert.Equal("Paquets", PackageDetails.FolderPath(null, folders));
        Assert.Equal("Paquets", PackageDetails.FolderPath(99, folders));
    }

    [Fact]
    public void FolderPath_StopsOnALoop()
    {
        List<PackageFolder> folders = [new() { Id = 1, Name = "A", ParentId = 2 }, new() { Id = 2, Name = "B", ParentId = 1 }];
        Assert.StartsWith("Paquets › ", PackageDetails.FolderPath(1, folders));
    }

    [Fact]
    public void Conditions_AreSummedUp()
    {
        Assert.StartsWith("Aucune", PackageDetails.ConditionsText(new Conditions()));
        var text = PackageDetails.ConditionsText(new Conditions
        {
            Windows = WindowsVersions.Windows10 | WindowsVersions.Windows11,
            Architecture = ArchitectureCondition.X64,
            Registry = PresenceCondition.NotExists,
            RegistryKey = @"HKLM\SOFTWARE\Exemple",
            RegistryValue = "Version",
        });
        Assert.Equal(@"Windows 11 ou Windows 10 ; 64 bits ; registre HKLM\SOFTWARE\Exemple\Version absent", text);
    }

    [Fact]
    public void Offline_SaysWhereItComesFrom()
    {
        var settings = new OfflineSettings { WakeOnLan = true, WakeWaitMinutes = 3, Retry = false };
        Assert.Equal("Préférences (réveil du poste (attente 3 min), pas de nouvel essai)", PackageDetails.OfflineText(settings, fromDefaults: true));
        Assert.Equal("Réveil du poste (attente 3 min), pas de nouvel essai", PackageDetails.OfflineText(settings, fromDefaults: false));
    }

    [Fact]
    public void UsedBy_ListsThePackagesThatNestIt()
    {
        List<Package> packages =
        [
            new() { Id = 1, Name = "Runtime" },
            new() { Id = 2, Name = "Poste élève", Steps = [new() { Type = StepType.Nested, NestedPackageId = 1 }] },
            new() { Id = 3, Name = "Autre" },
        ];
        Assert.Equal("Poste élève", PackageDetails.UsedByText(1, packages));
        Assert.Equal("Aucun paquet", PackageDetails.UsedByText(3, packages));
    }
}
