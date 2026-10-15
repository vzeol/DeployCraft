using DeployCraft.Server.Library;
using Microsoft.Data.Sqlite;

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
    public void Suggestions_HaveUniqueIds() =>
        Assert.Equal(LibraryCatalog.Suggested.Count, LibraryCatalog.Suggested.Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void Index_SearchesNamesPublishersAndTags_BestNamesFirst()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dc-index-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE packages(rowid INTEGER PRIMARY KEY, id TEXT NOT NULL, name TEXT NOT NULL, moniker TEXT, latest_version TEXT NOT NULL);
                    CREATE TABLE tags2(rowid INTEGER PRIMARY KEY, tag TEXT NOT NULL);
                    CREATE TABLE tags2_map(tag INT64 NOT NULL, package INT64 NOT NULL);
                    CREATE TABLE norm_publishers2(norm_publisher TEXT NOT NULL, package INT64 NOT NULL);
                    INSERT INTO packages VALUES (1, 'Exemple.LecteurPlus', 'Lecteur Plus Media', NULL, '2.0'),
                                                (2, 'Exemple.Lecteur', 'Lecteur', 'lecteur', '1.5'),
                                                (3, 'Autre.Editeur', 'Bloc-notes', NULL, '8.1');
                    INSERT INTO tags2 VALUES (1, 'video');
                    INSERT INTO tags2_map VALUES (1, 1);
                    INSERT INTO norm_publishers2 VALUES ('studio exemple', 3);
                    """;
                cmd.ExecuteNonQuery();
            }
            var index = new WingetIndex(path);

            Assert.Equal(3, index.Count());
            Assert.Equal(["Exemple.Lecteur", "Exemple.LecteurPlus"], index.Search("lecteur", 10).Select(e => e.Id));
            Assert.Equal("Exemple.LecteurPlus", index.Search("VIDEO", 10).Single().Id);
            Assert.Equal("Autre.Editeur", index.Search("studio bloc", 10).Single().Id);
            Assert.Empty(index.Search("lecteur bloc", 10));
            Assert.Equal("1.5", index.Get(["exemple.lecteur"])["Exemple.Lecteur"].Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SafeName_RemovesForbiddenCharacters() =>
        Assert.Equal("Notepad++ 8_9", PackageLibrary.SafeName("Notepad++ 8:9"));
}
