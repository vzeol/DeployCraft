namespace DeployCraft.Server.Library;

/// <summary>Installer chosen from a catalog entry, with the parameters that launch it without a window.</summary>
public record LibraryInstaller(string Type, string Architecture, string Url, string Sha256, string Extension, string Arguments);

/// <summary>
/// Reading of the public winget catalog's installation entries (YAML format) and choice of the installer to deploy:
/// for the whole machine, without a window, 64-bit preferred and in French if the publisher offers one.
/// </summary>
public static class CatalogManifest
{
    private static readonly string[] SupportedTypes = ["msi", "wix", "nullsoft", "inno", "burn", "exe"];

    /// <summary>Installer to deploy; <paramref name="problem"/> says why none fits.</summary>
    public static LibraryInstaller? Choose(string yaml, out string? problem)
    {
        var root = Yaml.Parse(yaml) as Dictionary<string, object?> ?? [];
        var installers = (root.GetValueOrDefault("Installers") as List<object?> ?? []).OfType<Dictionary<string, object?>>().ToList();
        if (installers.Count == 0)
        {
            problem = "fiche sans installeur.";
            return null;
        }

        var candidates = new List<(Dictionary<string, object?> Entry, string Type, string Arch, string? Scope)>();
        foreach (var i in installers)
        {
            var type = Text(i, root, "InstallerType")?.ToLowerInvariant() ?? "";
            var arch = Text(i, null, "Architecture")?.ToLowerInvariant() ?? "";
            var scope = Text(i, root, "Scope")?.ToLowerInvariant();
            if (arch is not ("x64" or "x86" or "neutral") || scope == "user" || !SupportedTypes.Contains(type)) continue;
            if (type == "exe" && Switch(i, root, "Silent") == null) continue;
            if (string.IsNullOrWhiteSpace(Text(i, null, "InstallerUrl")) || string.IsNullOrWhiteSpace(Text(i, null, "InstallerSha256"))) continue;
            candidates.Add((i, type, arch, scope));
        }
        if (candidates.Count == 0)
        {
            problem = "aucun installeur silencieux pour tout le poste (Windows 64 ou 32 bits) dans le catalogue.";
            return null;
        }

        var best = candidates
            .OrderBy(c => c.Arch switch { "x64" => 0, "neutral" => 1, _ => 2 })
            .ThenBy(c => LocaleRank(Text(c.Entry, root, "InstallerLocale")))
            .ThenBy(c => c.Type is "msi" or "wix" ? 0 : 1)
            .First();
        problem = null;
        var url = Text(best.Entry, null, "InstallerUrl")!.Trim();
        return new LibraryInstaller(best.Type, best.Arch, url, Text(best.Entry, null, "InstallerSha256")!.Trim().ToUpperInvariant(),
            best.Type is "msi" or "wix" ? ".msi" : ".exe", Arguments(best.Entry, root, best.Type, best.Scope));
    }

    /// <summary>
    /// Parameters added to the file. An .msi is already run without a window and without a restart by DeployCraft:
    /// only the software's own parameters are added to it.
    /// </summary>
    internal static string Arguments(Dictionary<string, object?> installer, Dictionary<string, object?> root, string type, string? scope)
    {
        var parts = new List<string>();
        var silent = Switch(installer, root, "Silent");
        switch (type)
        {
            case "msi" or "wix":
                if (scope == "machine") parts.Add("ALLUSERS=1");
                break;
            case "nullsoft":
                parts.Add(silent ?? "/S");
                break;
            case "inno":
                parts.Add(silent ?? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-");
                if (scope == "machine" && !parts[0].Contains("/ALLUSERS", StringComparison.OrdinalIgnoreCase)) parts.Add("/ALLUSERS");
                break;
            case "burn":
                parts.Add(silent ?? "/quiet /norestart");
                break;
            default:
                parts.Add(silent!);
                break;
        }
        if (Switch(installer, root, "Custom") is { } custom) parts.Add(custom);
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static int LocaleRank(string? locale) => locale?.ToLowerInvariant() switch
    {
        "fr-fr" => 0,
        "fr" => 1,
        null or "" => 2,
        "en-us" => 3,
        _ => 4,
    };

    /// <summary>Installer's value, otherwise the entry's (value common to all its installers).</summary>
    private static string? Text(Dictionary<string, object?> installer, Dictionary<string, object?>? root, string key) =>
        installer.GetValueOrDefault(key) as string ?? root?.GetValueOrDefault(key) as string;

    private static string? Switch(Dictionary<string, object?> installer, Dictionary<string, object?> root, string key)
    {
        var value = (installer.GetValueOrDefault("InstallerSwitches") as Dictionary<string, object?>)?.GetValueOrDefault(key) as string
                    ?? (root.GetValueOrDefault("InstallerSwitches") as Dictionary<string, object?>)?.GetValueOrDefault(key) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Compares two version numbers ("3.0.24" &lt; "3.0.100"), digit by digit.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        var x = (a ?? "").Split('.', '-', '_');
        var y = (b ?? "").Split('.', '-', '_');
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var p = i < x.Length ? x[i] : "0";
            var q = i < y.Length ? y[i] : "0";
            var c = long.TryParse(p, out var m) && long.TryParse(q, out var n) ? m.CompareTo(n) : string.Compare(p, q, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }
}

/// <summary>
/// YAML reader reduced to catalog entries: "Name: value" keys, indented blocks, "- " lists, quoted
/// strings and multi-line texts (| or &gt;). Comments and empty lines are ignored.
/// </summary>
internal static class Yaml
{
    public static object? Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#') && l.Trim() != "---")
            .Select(l => l.TrimEnd())
            .ToList();
        var i = 0;
        return lines.Count == 0 ? null : Block(lines, ref i, Indent(lines[0]));
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static object? Block(List<string> lines, ref int i, int indent)
    {
        if (lines[i].TrimStart().StartsWith("- ") || lines[i].Trim() == "-") return List(lines, ref i, indent);
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (i < lines.Count && Indent(lines[i]) == indent && !lines[i].TrimStart().StartsWith('-'))
        {
            var line = lines[i].Trim();
            var colon = KeySeparator(line);
            if (colon < 0) { i++; continue; }
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            i++;
            map[key] = value switch
            {
                "" => Child(lines, ref i, indent),
                "|" or "|-" or ">" or ">-" => MultiLine(lines, ref i, indent, value.StartsWith('>')),
                _ => Scalar(value),
            };
        }
        return map;
    }

    /// <summary>Content of a key with no value: a more indented block, or a list at the same level as the key.</summary>
    private static object? Child(List<string> lines, ref int i, int indent)
    {
        if (i >= lines.Count) return null;
        var next = Indent(lines[i]);
        if (next > indent || (next == indent && lines[i].TrimStart().StartsWith('-'))) return Block(lines, ref i, next);
        return null;
    }

    private static List<object?> List(List<string> lines, ref int i, int indent)
    {
        var list = new List<object?>();
        while (i < lines.Count && Indent(lines[i]) == indent && lines[i].TrimStart().StartsWith('-'))
        {
            var content = lines[i].TrimStart()[1..];
            var itemIndent = indent + 1 + (content.Length - content.TrimStart().Length);
            content = content.Trim();
            if (content.Length == 0)
            {
                i++;
                list.Add(Child(lines, ref i, indent));
            }
            else if (KeySeparator(content) >= 0)
            {
                // "- Key: value": the line becomes the first key of a block indented under the dash.
                lines[i] = new string(' ', itemIndent) + content;
                list.Add(Block(lines, ref i, itemIndent));
            }
            else
            {
                list.Add(Scalar(content));
                i++;
            }
        }
        return list;
    }

    private static string MultiLine(List<string> lines, ref int i, int indent, bool folded)
    {
        var parts = new List<string>();
        while (i < lines.Count && Indent(lines[i]) > indent) parts.Add(lines[i++].Trim());
        return string.Join(folded ? " " : "\n", parts);
    }

    /// <summary>Position of the ":" that separates the key from its value (outside quotes and "https://" addresses).</summary>
    private static int KeySeparator(string line)
    {
        if (line.StartsWith('"') || line.StartsWith('\'')) return -1;
        for (var k = 0; k < line.Length; k++)
            if (line[k] == ':' && (k + 1 == line.Length || line[k + 1] == ' ')) return k;
        return -1;
    }

    private static string Scalar(string value)
    {
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        if (!value.StartsWith('"') && !value.StartsWith('\'') && hash >= 0) value = value[..hash].TrimEnd();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'");
        return value;
    }
}
