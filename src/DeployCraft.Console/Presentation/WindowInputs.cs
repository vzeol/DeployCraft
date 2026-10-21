using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Inputs and texts shared by the "Deploy once" and schedule windows: computer lists and chosen packages.</summary>
public static class WindowInputs
{
    /// <summary>Computer names typed or pasted: separated by spaces, commas, semicolons, tabs or lines; leading \\ removed.</summary>
    public static IEnumerable<string> ComputerNames(string text) =>
        text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(raw => raw.Trim().TrimStart('\\'))
            .Where(name => name.Length > 0);

    /// <summary>Line of a chosen package: position, name and number of steps.</summary>
    public static string PackageLine(int index, Package p) =>
        L.Format(p.Steps.Count > 1 ? "WindowInputs_PackageLineMany" : "WindowInputs_PackageLineOne", index + 1, p.Name, p.Steps.Count);

    /// <summary>"Package setting" choice: the common value of the packages, or "each package" when they differ.</summary>
    public static string PackageSetting<T>(IEnumerable<T> values, Func<T, string> text)
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count == 1 ? L.Format("WindowInputs_PackageSetting", text(distinct[0])) : L.Get("WindowInputs_EachPackageSetting");
    }
}
