using DeployCraft.Server.Library;

namespace DeployCraft.Tests;

public class LibraryTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Msi_ForTheWholeComputer_IsPreferred_In64Bits()
    {
        var yaml = $"""
            # Fiche d'exemple
            PackageIdentifier: Exemple.Logiciel
            PackageVersion: 2.1.0
            Scope: machine
            Installers:
            - Architecture: x86
              InstallerType: msi
              InstallerUrl: https://exemple.test/logiciel-x86.msi
              InstallerSha256: {Sha}
            - Architecture: x64
              InstallerType: nullsoft
              InstallerUrl: https://exemple.test/logiciel-x64.exe
              InstallerSha256: {Sha}
            - Architecture: x64
              InstallerType: wix
              InstallerUrl: https://exemple.test/logiciel-x64.msi
              InstallerSha256: {Sha}
              InstallerSwitches:
                Custom: ADDLOCAL=ALL
            ManifestType: installer
            """;

        var installer = WingetManifest.Choose(yaml, out var problem);

        Assert.Null(problem);
        Assert.NotNull(installer);
        Assert.Equal("wix", installer.Type);
        Assert.Equal("https://exemple.test/logiciel-x64.msi", installer.Url);
        Assert.Equal(Sha.ToUpperInvariant(), installer.Sha256);
        Assert.Equal(".msi", installer.Extension);
        Assert.Equal("ALLUSERS=1 ADDLOCAL=ALL", installer.Arguments);
    }

    [Fact]
    public void UserOnly_AndExeWithoutSilentSwitch_AreRefused()
    {
        var yaml = $"""
            Installers:
            - Architecture: x64
              InstallerType: inno
              Scope: user
              InstallerUrl: https://exemple.test/a.exe
              InstallerSha256: {Sha}
            - Architecture: x64
              InstallerType: exe
              InstallerUrl: https://exemple.test/b.exe
              InstallerSha256: {Sha}
            - Architecture: arm64
              InstallerType: msi
              InstallerUrl: https://exemple.test/c.msi
              InstallerSha256: {Sha}
            """;

        Assert.Null(WingetManifest.Choose(yaml, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void FrenchInstaller_IsPreferred_AndSilentSwitchesAreAdded()
    {
        var yaml = $"""
            InstallerType: nullsoft
            InstallerSwitches:
              Silent: /S /PreventRebootRequired=true
            Installers:
            - InstallerLocale: en-US
              Architecture: x64
              InstallerUrl: https://exemple.test/en.exe
              InstallerSha256: {Sha}
            - InstallerLocale: fr-FR
              Architecture: x64
              InstallerUrl: "https://exemple.test/fr.exe"
              InstallerSha256: {Sha}
            """;

        var installer = WingetManifest.Choose(yaml, out _);

        Assert.Equal("https://exemple.test/fr.exe", installer!.Url);
        Assert.Equal("/S /PreventRebootRequired=true", installer.Arguments);
        Assert.Equal(".exe", installer.Extension);
    }

    [Theory]
    [InlineData("inno", "machine", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /ALLUSERS")]
    [InlineData("inno", null, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-")]
    [InlineData("nullsoft", "machine", "/S")]
    [InlineData("burn", "machine", "/quiet /norestart")]
    [InlineData("msi", null, "")]
    public void DefaultSilentSwitches_DependOnTheInstallerType(string type, string? scope, string expected)
    {
        var scopeLine = scope == null ? "" : $"\n  Scope: {scope}";
        var yaml = $"""
            Installers:
            - Architecture: x64
              InstallerType: {type}{scopeLine}
              InstallerUrl: https://exemple.test/setup
              InstallerSha256: {Sha}
            """;

        Assert.Equal(expected, WingetManifest.Choose(yaml, out _)!.Arguments);
    }

    [Theory]
    [InlineData("3.0.24", "3.0.100", -1)]
    [InlineData("26.04", "26.04", 0)]
    [InlineData("155.0.8059.40", "154.9.9999.9", 1)]
    [InlineData("1.13", "1.13.3", -1)]
    public void Versions_AreComparedNumerically(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(WingetManifest.CompareVersions(a, b)));

    [Fact]
    public void ManifestFolder_FollowsTheCatalogLayout()
    {
        Assert.Equal("manifests/v/VideoLAN/VLC", LibraryCatalog.ManifestFolder("VideoLAN.VLC"));
        Assert.Equal("manifests/g/GIMP/GIMP/3", LibraryCatalog.ManifestFolder("GIMP.GIMP.3"));
    }

    [Fact]
    public void Catalog_HasUniqueIds() =>
        Assert.Equal(LibraryCatalog.Apps.Count, LibraryCatalog.Apps.Select(a => a.Id).Distinct().Count());

    [Fact]
    public void SafeName_RemovesForbiddenCharacters() =>
        Assert.Equal("Notepad++ 8_9", PackageLibrary.SafeName("Notepad++ 8:9"));
}
