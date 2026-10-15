using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Principal;
using DeployCraft.Server.Data;
using DeployCraft.Server.Security;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeployCraft.Tests;

/// <summary>Accès à l'API du service (8.10) : règle d'autorisation, liste des utilisateurs, authentification Windows.</summary>
public sealed class ConsoleAccessTests : IDisposable
{
    private const string UsersSid = "S-1-5-32-545"; // groupe local intégré « Utilisateurs »
    private const string SomeUserSid = "S-1-5-21-1-2-3-1001";
    private const string SomeGroupSid = "S-1-5-21-1-2-3-2001";

    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly ConsoleAccess _access;

    public ConsoleAccessTests() => _access = new ConsoleAccess(new Store(Path.Combine(_dir, "test.db")));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static HashSet<string> Set(params string[] sids) => new(sids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ServerAdmins_AlwaysAllowed_EvenWithAnEmptyList()
    {
        Assert.True(ConsoleAccess.IsAllowed(Set(SomeUserSid, ConsoleAccess.AdministratorsSid), []));
        Assert.False(ConsoleAccess.IsAllowed(Set(SomeUserSid, UsersSid), []));
    }

    [Fact]
    public void ListedAccountOrGroup_IsAllowed()
    {
        Assert.True(ConsoleAccess.IsAllowed(Set(SomeUserSid), [new ConsoleUser(SomeUserSid, @"DOMAINE\admin")]));
        Assert.True(ConsoleAccess.IsAllowed(Set(SomeUserSid, SomeGroupSid), [new ConsoleUser(SomeGroupSid, @"DOMAINE\Techniciens")]));
        Assert.False(ConsoleAccess.IsAllowed(Set(SomeUserSid), [new ConsoleUser(SomeGroupSid, @"DOMAINE\Techniciens")]));
    }

    [Fact]
    public void Sids_IncludeGroupsKeptDenyOnlyByUac()
    {
        // Console ouverte sans élévation : le groupe Administrateurs est présent, mais « en refus seul ».
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.PrimarySid, SomeUserSid),
            new Claim(ClaimTypes.GroupSid, UsersSid),
            new Claim(ClaimTypes.DenyOnlySid, ConsoleAccess.AdministratorsSid),
            new Claim(ClaimTypes.Name, @"DOMAINE\admin"),
        ], "test"));

        var sids = ConsoleAccess.Sids(user);

        Assert.Equal(Set(SomeUserSid, UsersSid, ConsoleAccess.AdministratorsSid), sids);
        Assert.True(ConsoleAccess.IsServerAdmin(sids));
    }

    [Fact]
    public void AddUser_StoresTheSid_AndRefusesUnknownOrDuplicates()
    {
        // Nom traduit selon la langue de Windows : on part du SID.
        var usersName = new SecurityIdentifier(UsersSid).Translate(typeof(NTAccount)).Value;

        var (added, error) = _access.AddUser(usersName);
        Assert.Null(error);
        Assert.Equal(UsersSid, added!.Sid);
        Assert.Equal(UsersSid, Assert.Single(_access.ListUsers()).Sid);

        Assert.NotNull(_access.AddUser(usersName).Error);
        Assert.NotNull(_access.AddUser(new SecurityIdentifier(ConsoleAccess.AdministratorsSid).Translate(typeof(NTAccount)).Value).Error);
        Assert.NotNull(_access.AddUser(@"DOMAINE-INEXISTANT\compte-inexistant").Error);
        Assert.NotNull(_access.AddUser("  ").Error);

        _access.RemoveUser(UsersSid);
        Assert.Empty(_access.ListUsers());
    }

    [Fact]
    public void ActiveSessions_ListRecentAccounts()
    {
        _access.Seen(@"DOMAINE\admin");
        _access.Seen(@"domaine\ADMIN");

        Assert.Equal(@"DOMAINE\admin", Assert.Single(_access.ActiveSessions()).User);
    }

    /// <summary>Vraie authentification Windows en boucle locale, comme entre la console et le service.</summary>
    [Fact]
    public async Task Api_RequiresWindowsAuthentication_AndAnAllowedAccount()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.Use(_access.Check);
        app.MapGet("/api/whoami", (HttpContext c) => new { name = c.User.Identity?.Name, admin = ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(c.User)).ToString() });
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/api/whoami";

        using var anonymous = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        using var current = WindowsIdentity.GetCurrent();
        Assert.Null(_access.AddUser(current.Name).Error); // indépendant des droits du compte qui lance les tests
        using var console = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true });
        var answer = await console.GetFromJsonAsync<Dictionary<string, string>>(url);
        Assert.Equal(current.Name, answer!["name"], ignoreCase: true);
        // Le service voit le même statut d'administrateur que la session (même sans élévation, groupe en refus seul).
        Assert.Equal(ConsoleAccess.IsServerAdmin(ConsoleAccess.Sids(new WindowsPrincipal(current))).ToString(), answer["admin"]);
        Assert.Contains(_access.ActiveSessions(), s => string.Equals(s.User, current.Name, StringComparison.OrdinalIgnoreCase));

        await app.StopAsync();
    }
}
