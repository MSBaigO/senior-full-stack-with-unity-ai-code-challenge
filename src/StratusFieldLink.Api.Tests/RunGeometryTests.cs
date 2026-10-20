using StratusFieldLink.Api.Domain;
using Xunit;

namespace StratusFieldLink.Api.Tests;

public class RunGeometryTests
{
    private static SketchNode N(double x, double y, double z) => new() { X = x, Y = y, Z = z };

    [Fact]
    public void TotalLengthFeet_NullOrSingleNode_IsZero()
    {
        Assert.Equal(0d, RunGeometry.TotalLengthFeet(null));
        Assert.Equal(0d, RunGeometry.TotalLengthFeet(new List<SketchNode>()));
        Assert.Equal(0d, RunGeometry.TotalLengthFeet(new List<SketchNode> { N(1, 2, 3) }));
    }

    [Fact]
    public void TotalLengthFeet_StraightHorizontalRun_IsTheDistance()
    {
        var nodes = new List<SketchNode> { N(0, 10, 0), N(30, 10, 0) };
        Assert.Equal(30d, RunGeometry.TotalLengthFeet(nodes), 6);
    }

    [Fact]
    public void TotalLengthFeet_MultiSegment_SumsEverySegment()
    {
        var nodes = new List<SketchNode> { N(0, 10, 0), N(30, 10, 0), N(30, 10, 40) };
        Assert.Equal(70d, RunGeometry.TotalLengthFeet(nodes), 6);
    }

    [Fact]
    public void TotalLengthFeet_VerticalRiser_CountsTheRise()
    {
        var nodes = new List<SketchNode> { N(0, 10, 0), N(0, 22, 0) };
        Assert.Equal(12d, RunGeometry.TotalLengthFeet(nodes), 6);
    }

    [Fact]
    public void ElevationChangeFeet_FlatRun_IsZero()
    {
        var nodes = new List<SketchNode> { N(0, 10, 0), N(30, 10, 0) };
        Assert.Equal(0d, RunGeometry.ElevationChangeFeet(nodes), 6);
    }

    [Fact]
    public void SegmentCount_IsNodesMinusOne()
    {
        var nodes = new List<SketchNode> { N(0, 0, 0), N(1, 0, 0), N(1, 0, 1) };
        Assert.Equal(2, RunGeometry.SegmentCount(nodes));
    }

    // Parity vectors shared with the Unity client's SketchGeometryTests.
    [Fact]
    public void TotalLengthFeet_Diagonal_UsesAllThreeAxes()
    {
        var nodes = new List<SketchNode> { N(0, 0, 0), N(3, 4, 12) };
        Assert.Equal(13d, RunGeometry.TotalLengthFeet(nodes), 6);
    }

    [Fact]
    public void TotalLengthFeet_RunWithARiser_SumsHorizontalAndVertical()
    {
        var nodes = new List<SketchNode> { N(0, 10, 0), N(30, 10, 0), N(30, 22, 0) };
        Assert.Equal(42d, RunGeometry.TotalLengthFeet(nodes), 6);
    }

    [Fact]
    public void Validate_VerticalDropOffTheHeader_Passes()
    {
        var sketch = new FieldSketch
        {
            Id = "SK-DROP", PackageId = "PKG-CHW-L03", CrewId = "CREW-04", Discipline = "Piping", DiameterInches = 4,
            Nodes = new List<SketchNode> { N(0, 22, 0), N(0, 10, 0) }
        };
        Assert.True(SketchValidator.Validate(sketch).IsValid);
    }
}
