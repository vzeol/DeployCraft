namespace DeployCraft.Console;

/// <summary>Initial window dimensions constrained by the available desktop area.</summary>
public record WindowDimensions(double Width, double Height, double MinWidth, double MinHeight)
{
    public WindowDimensions FitWorkArea(double availableWidth, double availableHeight) => new(
        Math.Min(Width, availableWidth),
        Math.Min(Height, availableHeight),
        Math.Min(MinWidth, availableWidth),
        Math.Min(MinHeight, availableHeight));
}
