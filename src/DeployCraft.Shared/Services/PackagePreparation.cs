namespace DeployCraft.Server.Data;

/// <summary>Pure package preparation shared by deployment and console previews.</summary>
public static class PackagePreparation
{
    /// <summary>
    /// Ready-to-deploy copy: each setting left on "default" takes its value (step → package → Preferences).
    /// The deployment history thus keeps the values actually used.
    /// </summary>
    public static Package Resolve(this Package package, DeploymentDefaults defaults, Func<long, Package?>? lookup = null)
    {
        var copy = package.Flatten(lookup ?? (_ => null), defaults.TimeoutMinutes);
        copy.RunAs ??= defaults.RunAs;
        copy.CopyMode ??= defaults.CopyMode;
        copy.TimeoutMinutes ??= defaults.TimeoutMinutes;
        copy.Offline ??= Clone(defaults.Offline);
        foreach (var step in copy.Steps) step.TimeoutMinutes ??= copy.TimeoutMinutes;
        return copy;
    }

    /// <summary>Accepted nested package levels (a package containing one that contains one...).</summary>
    public const int MaxNestingDepth = 5;

    /// <summary>
    /// Copy where each "nested package" step is replaced by the steps of that package: named "Step › Sub-step",
    /// in place of the replaced step (in their order), disabled if it is, with its conditions and those of the nested package
    /// as additional conditions. Timeout: the sub-step's, otherwise the nested package's, otherwise
    /// <paramref name="defaultTimeout"/>. Package not found, loop or too many levels: <see cref="ArgumentException"/>.
    /// </summary>
    public static Package Flatten(this Package package, Func<long, Package?> lookup, int? defaultTimeout = null)
    {
        var copy = Clone(package);
        copy.Steps = Expand(copy.Steps, lookup, defaultTimeout, [package.Id], 0);
        return copy;
    }

    private static List<PackageStep> Expand(List<PackageStep> steps, Func<long, Package?> lookup, int? defaultTimeout, List<long> path, int depth)
    {
        var result = new List<PackageStep>();
        foreach (var step in steps)
        {
            if (step.Type != StepType.Nested)
            {
                result.Add(step);
                continue;
            }

            var nested = NestedPackage(step, lookup, path, depth);
            var label = string.IsNullOrWhiteSpace(step.Name) ? nested.Name : step.Name;
            path.Add(nested.Id);
            var inner = Expand(Clone(nested.Steps), lookup, defaultTimeout, path, depth + 1);
            path.RemoveAt(path.Count - 1);

            foreach (var s in inner)
            {
                s.Name = $"{label} › {(string.IsNullOrWhiteSpace(s.Name) ? s.Type.ToString() : s.Name)}";
                s.Enabled = step.Enabled && s.Enabled;
                s.RunAs ??= step.RunAs;
                s.TimeoutMinutes ??= nested.TimeoutMinutes ?? defaultTimeout;
                var extras = new List<ConditionGroup>();
                if (!step.Conditions.IsEmpty) extras.Add(new() { Label = Msg.Format("Package_StepConditions", label), Conditions = step.Conditions });
                if (!nested.Conditions.IsEmpty) extras.Add(new() { Label = Msg.Format("Package_NestedConditions", nested.Name), Conditions = nested.Conditions });
                extras.AddRange(s.ExtraConditions ?? []);
                s.ExtraConditions = extras.Count > 0 ? extras : null;
                result.Add(s);
            }
        }
        return result;
    }

    private static Package NestedPackage(PackageStep step, Func<long, Package?> lookup, List<long> path, int depth)
    {
        var title = string.IsNullOrWhiteSpace(step.Name) ? Msg.Get("Package_NestedUntitled") : Msg.Format("Package_QuotedName", step.Name);
        if (step.NestedPackageId is not { } id) throw new ArgumentException(Msg.Format("Package_NestedChoose", title));
        var nested = lookup(id) ?? throw new ArgumentException(Msg.Format("Package_NestedNotFound", title, id));
        if (path.Contains(id))
        {
            var loop = path.Skip(path.IndexOf(id)).Append(id).Select(i => lookup(i)?.Name ?? Msg.Format("Package_NumberedId", i));
            throw new ArgumentException(Msg.Format("Package_NestedLoop", title, string.Join(" › ", loop)));
        }
        if (depth >= MaxNestingDepth) throw new ArgumentException(Msg.Format("Package_NestedTooDeep", title, MaxNestingDepth));
        return nested;
    }

    private static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
}
