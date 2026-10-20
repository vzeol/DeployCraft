namespace DeployCraft.Console;

/// <summary>Paths the service could not find, and paths already checked (so a path is not asked again at each key press).</summary>
public sealed class PathCheck
{
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Missing => _missing;

    /// <summary>Paths to send to the service: the new ones, or all of them when <paramref name="again"/>.</summary>
    public List<string> ToCheck(IEnumerable<string> paths, bool again = false) =>
        paths.Where(p => again || !_checked.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void Record(IEnumerable<string> checkedPaths, IReadOnlyCollection<string> missing)
    {
        foreach (var path in checkedPaths)
        {
            _checked.Add(path);
            if (missing.Contains(path, StringComparer.OrdinalIgnoreCase)) _missing.Add(path);
            else _missing.Remove(path);
        }
    }
}
