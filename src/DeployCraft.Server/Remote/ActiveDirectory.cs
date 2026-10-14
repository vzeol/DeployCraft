using System.DirectoryServices;
using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

public record AdComputer(string Name, string? DnsName, string? OperatingSystem, string Ou, DateTime? LastLogon, bool Enabled);

/// <summary>Lecture des ordinateurs de l'Active Directory avec un identifiant du domaine.</summary>
public class ActiveDirectory(DeployCredentials credentials)
{
    private const int UacAccountDisable = 0x2;

    public List<AdComputer> ListComputers(string? search)
    {
        var account = credentials.ForDirectory();
        var domain = account.Domain!;
        using var root = new DirectoryEntry($"LDAP://{domain}", QualifiedUser(account), credentials.Password(account));
        var filter = "(objectCategory=computer)";
        if (!string.IsNullOrWhiteSpace(search)) filter = $"(&{filter}(name=*{Escape(search.Trim())}*))";

        using var searcher = new DirectorySearcher(root, filter,
            ["name", "dNSHostName", "operatingSystem", "distinguishedName", "lastLogonTimestamp", "userAccountControl"])
        {
            PageSize = 500,
            SizeLimit = 5000,
        };

        var list = new List<AdComputer>();
        try
        {
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                var dn = Get(r, "distinguishedName") ?? "";
                var uac = r.Properties["userAccountControl"].Count > 0 ? Convert.ToInt32(r.Properties["userAccountControl"][0]) : 0;
                DateTime? lastLogon = r.Properties["lastLogonTimestamp"].Count > 0
                    ? DateTime.FromFileTimeUtc((long)r.Properties["lastLogonTimestamp"][0])
                    : null;
                list.Add(new AdComputer(Get(r, "name") ?? "", Get(r, "dNSHostName"), Get(r, "operatingSystem"),
                    OuPath(dn), lastLogon, (uac & UacAccountDisable) == 0));
            }
        }
        catch (DirectoryServicesCOMException ex)
        {
            throw new DeployException("Lecture de l'Active Directory impossible : " + ex.Message.Trim(), ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new DeployException($"Domaine « {domain} » injoignable ou identifiants refusés : {ex.Message.Trim()}", ex);
        }

        return list.OrderBy(c => c.Ou).ThenBy(c => c.Name).ToList();
    }

    /// <summary>Vérifie un compte du domaine en se connectant à l'annuaire avec ce mot de passe.</summary>
    public static void TestConnection(Credential account, string password)
    {
        var domain = account.Domain!;
        try
        {
            using var root = new DirectoryEntry($"LDAP://{domain}", QualifiedUser(account), password);
            _ = root.NativeObject; // force la connexion
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new DeployException($"Connexion au domaine « {domain} » refusée : {ex.Message.Trim()}", ex);
        }
    }

    private static string QualifiedUser(Credential c) =>
        c.User.Contains('\\') || c.User.Contains('@') ? c.User : $@"{c.Domain}\{c.User}";

    /// <summary>« CN=PC01,OU=Comptabilite,OU=Postes,DC=domaine,DC=local » → « Postes / Comptabilite ».</summary>
    private static string OuPath(string dn)
    {
        var ous = dn.Split(',').Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)).Select(p => p[3..]).Reverse();
        var path = string.Join(" / ", ous);
        return path.Length > 0 ? path : "(racine)";
    }

    private static string? Get(SearchResult r, string prop) => r.Properties[prop].Count > 0 ? r.Properties[prop][0]?.ToString() : null;

    private static string Escape(string s) =>
        s.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
