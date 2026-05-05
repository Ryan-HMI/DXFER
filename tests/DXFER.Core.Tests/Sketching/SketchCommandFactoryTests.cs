using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCommandFactoryTests
{
    [Fact]
    public void BuildsLinearDimensionFromSelectedLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "edge" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.ReferenceKeys.Should().Equal("edge:start", "edge:end");
        dimension.Value.Should().Be(5);
        dimension.Anchor.Should().Be(new Point2(1.5, 2));
        dimension.IsDriving.Should().BeTrue();
    }

    [Fact]
    public void BuildsDimensionWithExplicitAnchor()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "edge" },
            "dim-1",
            out var dimension,
            out _,
            anchorOverride: new Point2(8, 9));

        result.Should().BeTrue();
        dimension.Anchor.Should().Be(new Point2(8, 9));
    }

    [Fact]
    public void BuildsLinearDimensionFromSelectedPolylineSegment()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("poly"),
                new[] { new Point2(0, 0), new Point2(3, 0), new Point2(3, 4) })
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "poly|segment|1" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.ReferenceKeys.Should().Equal("poly|segment|1:start", "poly|segment|1:end");
        dimension.Value.Should().Be(4);
        dimension.Anchor.Should().Be(new Point2(3, 2));
        dimension.IsDriving.Should().BeTrue();
    }

    [Fact]
    public void BuildsPointToLineDimensionFromPointAndLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new PointEntity(EntityId.Create("marker"), new Point2(2, 3))
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "edge", "marker" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
        dimension.ReferenceKeys.Should().Equal("edge", "marker");
        dimension.Value.Should().Be(3);
        dimension.Anchor.Should().Be(new Point2(2, 1.5));
    }

    [Fact]
    public void BuildsPointToPointDimensionFromCanvasSnapPoints()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("side"), new Point2(10, 0), new Point2(10, 6))
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "bottom|point|mid|5|0", "side|point|mid|10|3" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.ReferenceKeys.Should().Equal("bottom|point|mid|5|0", "side|point|mid|10|3");
        dimension.Value.Should().Be(Math.Sqrt(34));
        dimension.Anchor.Should().Be(new Point2(7.5, 1.5));
    }

    [Fact]
    public void BuildsPointToLineDimensionFromCanvasSnapPointAndLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("hole"), new Point2(4, 3), 2)
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "edge", "hole|point|quadrant-90|4|5" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
        dimension.ReferenceKeys.Should().Equal("edge", "hole|point|quadrant-90|4|5");
        dimension.Value.Should().Be(5);
        dimension.Anchor.Should().Be(new Point2(4, 2.5));
    }

    [Fact]
    public void BuildsDiameterDimensionFromCircle()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("hole"), new Point2(10, 20), 4)
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "hole" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Diameter);
        dimension.ReferenceKeys.Should().Equal("hole");
        dimension.Value.Should().Be(8);
        dimension.Anchor.Should().Be(new Point2(14, 20));
    }

    [Fact]
    public void BuildsRadiusDimensionFromArcByDefault()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(10, 20), 4, 0, 90)
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "arc" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Radius);
        dimension.ReferenceKeys.Should().Equal("arc");
        dimension.Value.Should().Be(4);
    }

    [Fact]
    public void BuildsRadiusDimensionFromArcEndpointWhenItIsTheOnlySelection()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(10, 20), 4, 0, 90)
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "arc|point|end|10|24" },
            "dim-1",
            out var dimension,
            out _);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Radius);
        dimension.ReferenceKeys.Should().Equal("arc");
        dimension.Value.Should().Be(4);
    }

    [Fact]
    public void BuildsDiameterDimensionFromArcWhenRequested()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(10, 20), 4, 0, 90)
        });

        var result = SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "arc" },
            "dim-1",
            out var dimension,
            out _,
            radialDiameter: true);

        result.Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Diameter);
        dimension.Value.Should().Be(8);
    }

    [Fact]
    public void BuildsHorizontalConstraintFromSelectedLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
        });

        var result = SketchCommandFactory.TryBuildConstraint(
            document,
            new[] { "edge" },
            SketchConstraintKind.Horizontal,
            "constraint-1",
            out var constraint,
            out _);

        result.Should().BeTrue();
        constraint.Kind.Should().Be(SketchConstraintKind.Horizontal);
        constraint.ReferenceKeys.Should().Equal("edge");
    }

    [Fact]
    public void BuildsCoincidentConstraintFromPointEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("a"), new Point2(1, 2)),
            new PointEntity(EntityId.Create("b"), new Point2(3, 4))
        });

        var result = SketchCommandFactory.TryBuildConstraint(
            document,
            new[] { "a", "b" },
            SketchConstraintKind.Coincident,
            "constraint-1",
            out var constraint,
            out _);

        result.Should().BeTrue();
        constraint.Kind.Should().Be(SketchConstraintKind.Coincident);
        constraint.ReferenceKeys.Should().Equal("a", "b");
    }

    [Fact]
    public void BuildsTangentConstraintFromCircleAndLineInEitherOrder()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 2), 2),
            new LineEntity(EntityId.Create("edge"), new Point2(-5, 0), new Point2(5, 0))
        });

        SketchCommandFactory.TryBuildConstraint(
                document,
                new[] { "edge", "circle" },
                SketchConstraintKind.Tangent,
                "constraint-1",
                out var first,
                out _)
            .Should().BeTrue();
        first.ReferenceKeys.Should().Equal("edge", "circle");

        SketchCommandFactory.TryBuildConstraint(
                document,
                new[] { "circle", "edge" },
                SketchConstraintKind.Tangent,
                "constraint-2",
                out var second,
                out _)
            .Should().BeTrue();
        second.ReferenceKeys.Should().Equal("circle", "edge");
    }
}
