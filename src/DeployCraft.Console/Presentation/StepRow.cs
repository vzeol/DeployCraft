using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>Line of the step list of a package.</summary>
public record StepRow(int Number, string Name, string Type, string Conditions, string Details)
{
    public static List<StepRow> For(IEnumerable<PackageStep> steps, Func<long, Package?> lookup) =>
        steps.Select((s, i) => new StepRow(i + 1, StepSummary.Name(s), StepSummary.TypeText(s.Type),
            s.Conditions.IsEmpty ? "" : L.Get("StepRow_Yes"), StepSummary.Details(s, lookup))).ToList();
}
