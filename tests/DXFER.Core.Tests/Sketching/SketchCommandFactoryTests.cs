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
    public void BuildsRadiusDimensionFromCircle()
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
        dimension.Kind.Should().Be(SketchDimensionKind.Radius);
        dimension.ReferenceKeys.Should().Equal("hole");
        dimension.Value.Should().Be(4);
        dimension.Anchor.Should().Be(new Point2(14, 20));
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
}
