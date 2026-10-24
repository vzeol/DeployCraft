using DeployCraft.Console;

namespace DeployCraft.Tests;

public class WindowDimensionsTests
{
    private static readonly WindowDimensions Preferred = new(1360, 820, 980, 520);

    [Theory]
    [InlineData(1920, 1040, 1360, 820, 980, 520)]
    [InlineData(1280, 760, 1280, 760, 980, 520)]
    [InlineData(1280, 800, 1280, 800, 980, 520)]
    [InlineData(1920, 760, 1360, 760, 980, 520)]
    [InlineData(1200, 1000, 1200, 820, 980, 520)]
    [InlineData(800, 480, 800, 480, 800, 480)]
    [InlineData(980, 520, 980, 520, 980, 520)]
    [InlineData(1360, 820, 1360, 820, 980, 520)]
    [InlineData(1024.5, 600.5, 1024.5, 600.5, 980, 520)]
    public void InitialSize_FitsTheWorkAreaWithoutEnlargingTheWindow(double workWidth, double workHeight,
        double width, double height, double minWidth, double minHeight)
    {
        var actual = Preferred.FitWorkArea(workWidth, workHeight);

        Assert.Equal(new WindowDimensions(width, height, minWidth, minHeight), actual);
        Assert.InRange(actual.Width, actual.MinWidth, workWidth);
        Assert.InRange(actual.Height, actual.MinHeight, workHeight);
        Assert.Equal(new WindowDimensions(1360, 820, 980, 520), Preferred);
    }
}
