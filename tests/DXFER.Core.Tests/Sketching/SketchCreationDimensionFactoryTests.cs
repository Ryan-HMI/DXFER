using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationDimensionFactoryTests
{
    [Fact]
    public void CreatesDrivingLengthDimensionForKeyedLine()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(3, 4))
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "line",
            entities,
            new Dictionary<string, double> { ["length"] = 5 },
            CreateDimensionId);

        dimensions.Should().ContainSingle()
            .Which.Should().Match<SketchDimension>(dimension =>
                dimension.Kind == SketchDimensionKind.LinearDistance
                && dimension.ReferenceKeys.SequenceEqual(new[] { "line-a:start", "line-a:end" })
                && dimension.Value == 5
                && dimension.IsDriving);
    }

    [Fact]
    public void CreatesWidthAndHeightDimensionsForKeyedRectangle()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new Point2(0, 0), new Point2(6, 0)),
            new LineEntity(EntityId.Create("right"), new Point2(6, 0), new Point2(6, 4)),
            new LineEntity(EntityId.Create("top"), new Point2(6, 4), new Point2(0, 4)),
            new LineEntity(EntityId.Create("left"), new Point2(0, 4), new Point2(0, 0))
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 6, ["height"] = 4 },
            CreateDimensionId);

        dimensions.Should().HaveCount(2);
        dimensions.Should().Contain(dimension =>
            dimension.Kind == SketchDimensionKind.LinearDistance
            && dimension.ReferenceKeys.SequenceEqual(new[] { "bottom:start", "bottom:end" })
            && dimension.Value == 6);
        dimensions.Should().Contain(dimension =>
            dimension.Kind == SketchDimensionKind.LinearDistance
            && dimension.ReferenceKeys.SequenceEqual(new[] { "right:start", "right:end" })
            && dimension.Value == 4);
    }

    [Fact]
    public void CreatesRadiusDimensionForKeyedCircle()
    {
        var entities = new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle-a"), new Point2(10, 20), 3)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "centercircle",
            entities,
            new Dictionary<string, double> { ["radius"] = 3 },
            CreateDimensionId);

        dimensions.Should().ContainSingle()
            .Which.Should().Match<SketchDimension>(dimension =>
                dimension.Kind == SketchDimensionKind.Radius
                && dimension.ReferenceKeys.SequenceEqual(new[] { "circle-a" })
                && dimension.Value == 3
                && dimension.Anchor == new Point2(13, 20)
                && dimension.IsDriving);
    }

    [Theory]
    [InlineData("threepointarc")]
    [InlineData("centerpointarc")]
    public void CreatesRadiusDimensionForKeyedArc(string toolName)
    {
        var entities = new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc-a"), new Point2(10, 20), 3, 0, 90)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            toolName,
            entities,
            new Dictionary<string, double> { ["radius"] = 3 },
            CreateDimensionId);

        dimensions.Should().ContainSingle()
            .Which.Should().Match<SketchDimension>(dimension =>
                dimension.Kind == SketchDimensionKind.Radius
                && dimension.ReferenceKeys.SequenceEqual(new[] { "arc-a" })
                && dimension.Value == 3
                && dimension.Anchor == new Point2(13, 20)
                && dimension.IsDriving);
    }

    [Fact]
    public void CreatesSweepAngleDimensionForKeyedCenterPointArc()
    {
        var entities = new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc-a"), new Point2(0, 0), 5, 0, 120)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "centerpointarc",
            entities,
            new Dictionary<string, double> { ["sweep"] = 120 },
            CreateDimensionId);

        dimensions.Should().ContainSingle()
            .Which.Should().Match<SketchDimension>(dimension =>
                dimension.Kind == SketchDimensionKind.Angle
                && dimension.ReferenceKeys.SequenceEqual(new[] { "arc-a" })
                && dimension.Value == 120
                && dimension.Anchor.HasValue
                && dimension.IsDriving);
    }

    [Fact]
    public void CreatesEllipseAxisDiameterDimensionsFromTypedValues()
    {
        var entities = new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse-a"), new Point2(0, 0), new Point2(4, 0), 0.5)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "ellipse",
            entities,
            new Dictionary<string, double> { ["major"] = 8, ["minor"] = 4 },
            CreateDimensionId);

        dimensions.Should().HaveCount(2);
        dimensions.Should().OnlyContain(dimension =>
            dimension.Kind == SketchDimensionKind.LinearDistance
            && dimension.ReferenceKeys.All(key => key.Contains("|point|", StringComparison.Ordinal))
            && dimension.IsDriving);
        dimensions.Should().Contain(dimension =>
            dimension.Value == 8
            && dimension.ReferenceKeys.Any(key => key.Contains("major-start|-4|0", StringComparison.Ordinal))
            && dimension.ReferenceKeys.Any(key => key.Contains("major-end|4|0", StringComparison.Ordinal)));
        dimensions.Should().Contain(dimension =>
            dimension.Value == 4
            && dimension.ReferenceKeys.Any(key => key.Contains("minor-start|0|-2", StringComparison.Ordinal))
            && dimension.ReferenceKeys.Any(key => key.Contains("minor-end|0|2", StringComparison.Ordinal)));
    }

    [Fact]
    public void CreatesPermanentSideCountDimensionForPolygon()
    {
        var entities = new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("poly-a"), new Point2(0, 0), 10, 0, 7)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "inscribedpolygon",
            entities,
            new Dictionary<string, double> { ["sides"] = 7 },
            CreateDimensionId);

        dimensions.Should().ContainSingle()
            .Which.Should().Match<SketchDimension>(dimension =>
                dimension.Kind == SketchDimensionKind.Count
                && dimension.ReferenceKeys.SequenceEqual(new[] { "poly-a" })
                && dimension.Value == 7
                && dimension.IsDriving);
    }

    [Fact]
    public void CreatesPolygonRadiusDimensionAgainstPolygonEntity()
    {
        var entities = new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("poly-a"), new Point2(0, 0), 10, 0, 6)
        };

        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "inscribedpolygon",
            entities,
            new Dictionary<string, double> { ["sides"] = 6, ["radius"] = 10 },
            CreateDimensionId);

        dimensions.Should().Contain(dimension =>
            dimension.Kind == SketchDimensionKind.Radius
            && dimension.ReferenceKeys.SequenceEqual(new[] { "poly-a" })
            && dimension.Value == 10);
        dimensions.Should().Contain(dimension =>
            dimension.Kind == SketchDimensionKind.Count
            && dimension.ReferenceKeys.SequenceEqual(new[] { "poly-a" })
            && dimension.Value == 6);
    }

    private static string CreateDimensionId() => Guid.NewGuid().ToString("N");
}
