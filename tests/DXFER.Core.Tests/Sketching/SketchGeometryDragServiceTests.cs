using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchGeometryDragServiceTests
{
    [Fact]
    public void DraggingLineEndpointMovesOnlyThatEndpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|start|0|0",
            new Point2(0, 0),
            new Point2(2, 3),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(2, 3), new Point2(10, 0)));
    }

    [Fact]
    public void DraggingLineMidpointTranslatesLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(7, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(2, 3), new Point2(12, 3)));
    }

    [Fact]
    public void DraggingLineMidpointTranslatesDimensionsReferencingThatLine()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[]
            {
                new SketchDimension(
                    "dim-edge",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    10,
                    new Point2(5, 2))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(7, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(7, 5));
    }

    [Fact]
    public void ShiftDraggingLineEndpointProjectsOntoCurrentLineVector()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|end|10|0",
            new Point2(10, 0),
            new Point2(14, 5),
            true,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(14, 0)));
    }

    [Fact]
    public void DraggingCircleCenterMovesCircle()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(4, 5), 3)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|center|4|5",
            new Point2(4, 5),
            new Point2(6, 8),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>()
            .Which.Should().Be(new CircleEntity(EntityId.Create("circle"), new Point2(6, 8), 3));
    }

    [Fact]
    public void DraggingCirclePerimeterChangesRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 3)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|quadrant-0|3|0",
            new Point2(3, 0),
            new Point2(5, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(5);
    }

    [Fact]
    public void DraggingArcPerimeterChangesRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|mid|2.12132|2.12132",
            new Point2(2.12132, 2.12132),
            new Point2(4, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>()
            .Which.Radius.Should().Be(4);
    }

    [Fact]
    public void DraggingArcEndpointChangesSweepAndRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|end|0|3",
            new Point2(0, 3),
            new Point2(4, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        var arc = next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>().Which;
        arc.Radius.Should().BeApproximately(Math.Sqrt(32), 0.000001);
        arc.EndAngleDegrees.Should().BeApproximately(45, 0.000001);
    }

    [Fact]
    public void ShiftDraggingArcEndpointOnlyChangesSweep()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|end|0|3",
            new Point2(0, 3),
            new Point2(4, 4),
            true,
            out var next,
            out _).Should().BeTrue();

        var arc = next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>().Which;
        arc.Radius.Should().Be(3);
        arc.EndAngleDegrees.Should().BeApproximately(45, 0.000001);
    }

    [Fact]
    public void DraggingPolylineVertexMovesOnlyThatVertex()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("poly"),
                new[] { new Point2(0, 0), new Point2(10, 0), new Point2(20, 0) })
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "poly|point|vertex-1|10|0",
            new Point2(10, 0),
            new Point2(10, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().Equal(new Point2(0, 0), new Point2(10, 4), new Point2(20, 0));
    }

    [Fact]
    public void FixedLineDoesNotDrag()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("fix-edge", SketchConstraintKind.Fix, new[] { "edge" })
            });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(5, 4),
            false,
            out var next,
            out var status);

        changed.Should().BeFalse();
        next.Should().BeSameAs(document);
        status.Should().Contain("constrained");
    }
}
