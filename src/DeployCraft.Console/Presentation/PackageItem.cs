using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>A package chosen in the "Deploy once" or schedule window.</summary>
public record PackageItem(Package Package, string Display);

/// <summary>Start step offered for a computer.</summary>
public record StepChoice(int Number, string Text);
