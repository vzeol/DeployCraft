using System.Security.AccessControl;
using System.Security.Principal;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployCraft.Tests;

/// <summary>Identifiants multiples (0.9.0), sur une base SQLite jetable. Mots de passe fictifs uniquement.</summary>
public sealed class CredentialsTests : IDisposable
{
    private const string FakePassword = "mot-de-passe-fictif-de-test";

    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly Store _store;

    public CredentialsTests() => _store = new Store(Path.Combine(_dir, "test.db"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void FirstCredential_BecomesDefault()
    {
        var credentials = new DeployCredentials(_store);
        var first = credentials.Save(new Credential { Domain = "DOMAINE.LOCAL", User = "admin" }, FakePassword);
        var second = credentials.Save(new Credential { Domain = ".", User = "Administrateur" }, FakePassword);

        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);
        Assert.Equal(first.Id, credentials.Resolve(null)!.Id);
        Assert.Equal(second.Id, credentials.Resolve(second.Id)!.Id);
    }

    [Fact]
    public void DeletingDefault_PromotesTheOldestRemaining()
    {
        var credentials = new DeployCredentials(_store);
        var a = credentials.Save(new Credential { User = "a" }, FakePassword);
        var b = credentials.Save(new Credential { User = "b" }, FakePassword);
        var c = credentials.Save(new Credential { User = "c" }, FakePassword);
        credentials.SetDefault(c.Id);

        credentials.Delete(c.Id);

        Assert.Equal(a.Id, credentials.Default!.Id);
        Assert.Equal([a.Id, b.Id], credentials.List().Select(x => x.Id));
    }

    [Fact]
    public void EmptyPassword_KeepsTheStoredOne()
    {
        var credentials = new DeployCredentials(_store);
        var saved = credentials.Save(new Credential { Domain = "DOMAINE.LOCAL", User = "admin" }, FakePassword);

        credentials.Save(new Credential { Id = saved.Id, Domain = "DOMAINE.LOCAL", User = "admin", Description = "Admin domaine" }, "");

        var reloaded = credentials.Get(saved.Id)!;
        Assert.Equal("Admin domaine", reloaded.Description);
        Assert.Equal(FakePassword, credentials.Password(reloaded));
    }

    [Fact]
    public void PasswordIsStoredEncrypted()
    {
        var credentials = new DeployCredentials(_store);
        var saved = credentials.Save(new Credential { User = "admin" }, FakePassword);

        var stored = _store.GetCredentialSecret(saved.Id)!;
        Assert.DoesNotContain(FakePassword, stored);
        Assert.DoesNotContain(FakePassword, System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(stored)));
    }

    [Fact]
    public void UnreadablePassword_IsFlagged_AndRefusedWithAClearMessage()
    {
        var credentials = new DeployCredentials(_store);
        // Chiffré « ailleurs » : des octets que DPAPI ne sait pas déchiffrer sur ce poste.
        var saved = _store.SaveCredential(new Credential { Domain = "DOMAINE.LOCAL", User = "admin" }, Convert.ToBase64String(new byte[64]));

        var reloaded = credentials.Get(saved.Id)!;
        Assert.True(reloaded.PasswordUnreadable);
        var ex = Assert.Throws<DeployException>(() => credentials.Password(reloaded));
        Assert.Contains("ressaisissez", ex.Message);
    }

    [Fact]
    public void SingleAccountFrom08_BecomesTheDefaultCredential()
    {
        var old = new DeployCredentials(_store).Save(new Credential { User = "tmp" }, FakePassword);
        var cipher = _store.GetCredentialSecret(old.Id);
        _store.DeleteCredential(old.Id);
        _store.SetSetting("cred.domain", "DOMAINE.LOCAL");
        _store.SetSetting("cred.user", "svc-deploy");
        _store.SetSetting("cred.password", cipher);

        var credentials = new DeployCredentials(_store);

        var migrated = Assert.Single(credentials.List());
        Assert.Equal(@"DOMAINE.LOCAL\svc-deploy", migrated.Account);
        Assert.True(migrated.IsDefault);
        Assert.Equal(FakePassword, credentials.Password(migrated));
        Assert.Null(_store.GetSetting("cred.user"));
        Assert.Null(_store.GetSetting("cred.password"));
    }

    [Theory]
    [InlineData(null, "Administrateur", @".\Administrateur", true)]
    [InlineData(".", "Administrateur", @".\Administrateur", true)]
    [InlineData(@".\", "Administrateur", @".\Administrateur", true)]
    [InlineData(" ./ ", "Administrateur", @".\Administrateur", true)]
    [InlineData("DOMAINE.LOCAL", "Administrateur", @"DOMAINE.LOCAL\Administrateur", false)]
    public void Account_ShowsLocalAccountsWithADot(string? domain, string user, string account, bool isLocal)
    {
        var c = new Credential { Domain = domain, User = user };

        Assert.Equal(account, c.Account);
        Assert.Equal(isLocal, c.IsLocal);
    }

    [Theory]
    [InlineData(@".\", "Administrateur", null, "Administrateur")]
    [InlineData(@"DOMAINE.LOCAL\", "admin", "DOMAINE.LOCAL", "admin")]
    [InlineData("", @".\Administrateur", null, "Administrateur")]
    [InlineData(null, @"DOMAINE.LOCAL\admin", "DOMAINE.LOCAL", "admin")]
    [InlineData("DOMAINE.LOCAL", "admin", "DOMAINE.LOCAL", "admin")]
    public void Save_NormalizesTheDomainAsTyped(string? domain, string user, string? savedDomain, string savedUser)
    {
        var saved = new DeployCredentials(_store).Save(new Credential { Domain = domain, User = user }, FakePassword);

        Assert.Equal(savedDomain, saved.Domain);
        Assert.Equal(savedUser, saved.User);
    }

    [Theory]
    [InlineData("PC-TEST-01", "PC-TEST-01")]
    [InlineData("pc-test-01.domaine.local", "pc-test-01")]
    [InlineData("10.1.2.3", "10.1.2.3")]
    public void LocalAccount_IsPresentedUnderTheTargetName(string computer, string expected)
    {
        Assert.Equal(expected, DeployCredentials.LogonDomain(new Credential { User = "Administrateur" }, computer));
    }

    [Fact]
    public void DomainAccount_KeepsItsDomainOnEveryTarget()
    {
        Assert.Equal("DOMAINE.LOCAL", DeployCredentials.LogonDomain(new Credential { Domain = "DOMAINE.LOCAL", User = "x" }, "PC01"));
    }

    [Fact]
    public void Directory_UsesADomainCredential_EvenIfTheDefaultIsLocal()
    {
        var credentials = new DeployCredentials(_store);
        credentials.Save(new Credential { User = "Administrateur" }, FakePassword);
        var domain = credentials.Save(new Credential { Domain = "DOMAINE.LOCAL", User = "admin" }, FakePassword);

        Assert.Equal(domain.Id, credentials.ForDirectory().Id);
    }

    [Fact]
    public void Directory_WithoutDomainCredential_AsksToAddOne()
    {
        var credentials = new DeployCredentials(_store);
        credentials.Save(new Credential { User = "Administrateur" }, FakePassword);

        Assert.Throws<DeployException>(credentials.ForDirectory);
    }

    [Fact]
    public void DataFolder_IsReservedToSystemAndAdministrators()
    {
        // Retirer ses propres droits n'est sans risque qu'en administrateur (sinon le dossier ne pourrait plus être supprimé).
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return;
        var folder = Path.Combine(_dir, "data");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "existant.db"), "");

        DataFolder.Restrict(folder, NullLogger.Instance);

        var allowed = new DirectoryInfo(folder).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => ((SecurityIdentifier)r.IdentityReference).Value).Distinct().Order();
        Assert.Equal(new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
        }.Order(), allowed);
        var fileRules = new FileInfo(Path.Combine(folder, "existant.db")).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        Assert.DoesNotContain(fileRules, r => ((SecurityIdentifier)r.IdentityReference).IsWellKnown(WellKnownSidType.BuiltinUsersSid));
    }
}
