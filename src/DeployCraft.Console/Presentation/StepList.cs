using System.Text.Json;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Editing of the step list of a package: insert, move, duplicate, remove.</summary>
public static class StepList
{
    /// <summary>Inserts the step after the selected one, or at the end.</summary>
    public static void InsertAfter(List<PackageStep> steps, PackageStep? selected, PackageStep step)
    {
        var index = selected != null ? steps.IndexOf(selected) + 1 : steps.Count;
        steps.Insert(index, step);
    }

    public static bool CanMove(List<PackageStep> steps, PackageStep? step, int delta)
    {
        var j = step == null ? -1 : steps.IndexOf(step) + delta;
        return step != null && j >= 0 && j < steps.Count;
    }

    public static void Move(List<PackageStep> steps, PackageStep step, int delta)
    {
        if (!CanMove(steps, step, delta)) return;
        var i = steps.IndexOf(step);
        var j = i + delta;
        (steps[i], steps[j]) = (steps[j], steps[i]);
    }

    /// <summary>Copy of the step, named "… (copie)" when it has a name.</summary>
    public static PackageStep Duplicate(PackageStep step)
    {
        var copy = Copy(step);
        copy.Name = string.IsNullOrWhiteSpace(step.Name) ? "" : step.Name + " (copie)";
        return copy;
    }

    /// <summary>Removes the step; returns the step to select next (the one now at its place, or the last one), or null.</summary>
    public static PackageStep? Remove(List<PackageStep> steps, PackageStep step)
    {
        var i = steps.IndexOf(step);
        steps.Remove(step);
        return steps.Count > 0 ? steps[Math.Min(Math.Max(i, 0), steps.Count - 1)] : null;
    }

    /// <summary>Deep copy through JSON, as the service stores it.</summary>
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Api.Json), Api.Json)!;
}
