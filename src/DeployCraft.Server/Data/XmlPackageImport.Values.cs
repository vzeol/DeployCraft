using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DeployCraft.Server.Data;

/// <summary>XML import: variables in paths, identity, and reading of element values.</summary>
public static partial class XmlPackageImport
{
    /// <summary>
    /// <c>$(Repository)</c> is replaced by the repository folder given at import; the other variables
    /// (DeployCraft has none) are left as they are and reported.
    /// </summary>
    private static void ReplaceVariables(PackageStep step, ImportStepResult line, string? repo)
    {
        var missingRepository = false;
        var others = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        step.File = Replace(step.File);
        step.Arguments = Replace(step.Arguments);
        step.Command = Replace(step.Command);
        step.CustomCommandLine = Replace(step.CustomCommandLine);
        step.AdditionalFiles = Replace(step.AdditionalFiles);
        step.CopySource = Replace(step.CopySource);
        step.CopyDestination = Replace(step.CopyDestination);
        step.Conditions.FilePath = Replace(step.Conditions.FilePath);
        if (missingRepository) line.Warn(Msg.Get("XmlPackageImport_RepositoryVariable"));
        if (others.Count > 0) line.Warn(Msg.Format("XmlPackageImport_OtherVariables", string.Join(", ", others)));

        string? Replace(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return VariablePattern().Replace(text, m =>
            {
                if (m.Groups[1].Value.Equals("Repository", StringComparison.OrdinalIgnoreCase))
                {
                    if (repo != null) return repo;
                    missingRepository = true;
                }
                else
                {
                    others.Add(m.Value);
                }
                return m.Value;
            });
        }
    }

    [GeneratedRegex(@"\$\((\w+)\)")]
    private static partial Regex VariablePattern();

    private static RunAsMode? RunAs(string value, Action<string> warn, string what)
    {
        switch (value)
        {
            case "Admin":
                return RunAsMode.DeployUser;
            case "LocalSystem":
                return RunAsMode.LocalSystem;
            case "LoggedOnUser":
                return RunAsMode.LoggedOnUser;
            case "AdminOnDesktop":
                warn(Msg.Format("XmlPackageImport_DesktopAccount", what));
                return RunAsMode.DeployUser;
            default:
                return null;
        }
    }

    private static string SuccessCodes(XElement e) =>
        Text(e, "SuccessCodes").Trim() is { Length: > 0 } codes ? codes : Defaults.SuccessCodes;

    /// <summary>Text of a child element; "" if missing.</summary>
    private static string Text(XElement e, string name) => e.Element(name)?.Value ?? "";

    /// <summary>Boolean written as <c>&lt;X value="true" /&gt;</c> (or as text); false if missing.</summary>
    private static bool Bool(XElement e, string name) => bool.TryParse(Raw(e, name), out var b) && b;

    private static int? Int(XElement e, string name) => int.TryParse(Raw(e, name), out var i) ? i : null;

    /// <summary>Value written as a <c>value</c> attribute (numbers, booleans, numerals) or as text.</summary>
    private static string Raw(XElement e, string name)
    {
        var x = e.Element(name);
        return (string?)x?.Attribute("value") ?? x?.Value ?? "";
    }

    /// <summary>Flags written as "A, B, C".</summary>
    private static HashSet<string> Flags(string value) =>
        new(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
