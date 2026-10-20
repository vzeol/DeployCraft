namespace DeployCraft.Console;

/// <summary>Line of the step list of a package.</summary>
public record StepRow(int Number, string Name, string Type, string Conditions, string Details);
