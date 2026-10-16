using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class DragMatrixHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PerpendicularLengthDragKeepsDrivingIntent(bool fixedStart)
    {
        var document = new DrawingDocument(
            new DrawingEntity[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)) },
            new[] { Dimension("length", SketchDimensionKind.LinearDistance, "edge:start", "edge:end", 10) },
            fixedStart ? new[] { Constraint("fix", SketchConstraintKind.Fix, "edge:start") } : Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(document, "edge|point|end|10|0", new(10, 0), new(10, 3), false,
            out var next, out var status).Should().BeTrue(status);
        AssertSatisfied(next);
        var line = (LineEntity)next.Entities[0];
        line.End.Y.Should().BeGreaterThan(0);
        if (fixedStart) line.Start.Should().Be(new Point2(0, 0));
        next.Dimensions[0].Value.Should().Be(10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartiallyDimensionedPolylineKeepsFixedPeerAndFreeVertexComponent(bool reverse)
    {
        var constraints = new[] {
            Constraint("fix", SketchConstraintKind.Fix, "poly|segment|0:start"),
            Constraint("fix-tail", SketchConstraintKind.Fix, "poly|segment|1:end")
        };
        var document = new DrawingDocument(
            new DrawingEntity[] { new PolylineEntity(EntityId.Create("poly"), new[] { new Point2(0, 0), new(10, 0), new(14, 5) }) },
            new[] { Dimension("width", SketchDimensionKind.HorizontalDistance, "poly|segment|0:start", "poly|segment|0:end", 10) },
            reverse ? constraints.Reverse().ToArray() : constraints);

        SketchGeometryDragService.TryApplyDrag(document, "poly|point|vertex-1|10|0", new(10, 0), new(13, 3), false,
            out var next, out var status).Should().BeTrue(status);
        AssertSatisfied(next);
        ((PolylineEntity)next.Entities[0]).Vertices.Should().Equal(new Point2(0, 0), new(10, 3), new(14, 5));
        next.Dimensions[0].Value.Should().Be(10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DimensionPreservingTranslationPropagatesMixedEntityWelds(bool reverse)
    {
        var constraints = new[] {
            Constraint("join-poly", SketchConstraintKind.Coincident, "edge:end", "poly|segment|0:start"),
            Constraint("join-point", SketchConstraintKind.Coincident, "poly|segment|0:start", "point")
        };
        var document = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)),
            new PolylineEntity(EntityId.Create("poly"), new[] { new Point2(10, 0), new(12, 2), new(14, 2) }),
            new PointEntity(EntityId.Create("point"), new(10, 0))
        }, new[] { Dimension("length", SketchDimensionKind.LinearDistance, "edge:start", "edge:end", 10) },
            reverse ? constraints.Reverse().ToArray() : constraints);

        SketchGeometryDragService.TryApplyDrag(document, "edge|point|end|10|0", new(10, 0), new(13, 0), false,
            out var next, out var status).Should().BeTrue(status);
        AssertSatisfied(next);
        ((LineEntity)next.Entities[0]).Start.Should().Be(new Point2(3, 0));
        ((LineEntity)next.Entities[0]).End.Should().Be(new Point2(13, 0));
        ((PolylineEntity)next.Entities[1]).Vertices[0].Should().Be(new Point2(13, 0));
        ((PointEntity)next.Entities[2]).Location.Should().Be(new Point2(13, 0));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0.000000001, 0, false)]
    [InlineData(0.0001, 0.0001, true)]
    [InlineData(-10, 0.0001, true)]
    [InlineData(10, -3, true)]
    public void AnchoredLengthNearDegenerateAndRecovery(double x, double y, bool accepted)
    {
        var original = new DrawingDocument(
            new DrawingEntity[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)) },
            new[] { Dimension("length", SketchDimensionKind.LinearDistance, "edge:start", "edge:end", 10) },
            new[] { Constraint("fix", SketchConstraintKind.Fix, "edge:start") });
        SketchGeometryDragService.TryApplyDrag(original, "edge|point|end|10|0", new(10, 0), new(x, y), false,
            out var next, out var status).Should().Be(accepted, status);
        if (!accepted) next.Should().BeSameAs(original);
        else
        {
            ((LineEntity)next.Entities[0]).Start.Should().Be(new Point2(0, 0));
            AssertSatisfied(next);
        }
        SketchGeometryDragService.TryApplyDrag(original, "edge|point|end|10|0", new(10, 0), new(10, 3), false,
            out var recovery, out status).Should().BeTrue(status);
        AssertSatisfied(recovery);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedMixedWeldRefusesWithoutChangingIntentAndRecoversWithoutFix(bool reverse)
    {
        var constraints = new[] {
            Constraint("join", SketchConstraintKind.Coincident, "edge:end", "point"),
            Constraint("fix", SketchConstraintKind.Fix, "point")
        };
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)),
            new PointEntity(EntityId.Create("point"), new(10, 0))
        }, new[] { Dimension("length", SketchDimensionKind.LinearDistance, "edge:start", "edge:end", 10) },
            reverse ? constraints.Reverse().ToArray() : constraints);
        SketchGeometryDragService.TryApplyDrag(original, "edge|point|end|10|0", new(10, 0), new(13, 3), false,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
        var free = new DrawingDocument(original.Entities, original.Dimensions, constraints.Take(1));
        SketchGeometryDragService.TryApplyDrag(free, "edge|point|end|10|0", new(10, 0), new(13, 3), false,
            out next, out var status).Should().BeTrue(status);
        AssertSatisfied(next);
    }

    [Fact]
    public void FixedPolylineVertexCannotBeMovedByWholePolylineFallback()
    {
        var original = new DrawingDocument(
            new DrawingEntity[] { new PolylineEntity(EntityId.Create("poly"), new[] { new Point2(0, 0), new(10, 0), new(14, 5) }) },
            new[] { Dimension("width", SketchDimensionKind.HorizontalDistance, "poly|segment|0:start", "poly|segment|0:end", 10) },
            new[] { Constraint("fix", SketchConstraintKind.Fix, "poly|segment|0:start"),
                Constraint("fix-middle", SketchConstraintKind.Fix, "poly|segment|0:end") });
        SketchGeometryDragService.TryApplyDrag(original, "poly|point|vertex-1|10|0", new(10, 0), new(13, 3), false,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
    }

    private static SketchDimension Dimension(string id, SketchDimensionKind kind, string first, string second, double value) =>
        new(id, kind, new[] { first, second }, value, new Point2(5, -2), isDriving: true);

    private static SketchConstraint Constraint(string id, SketchConstraintKind kind, params string[] keys) => new(id, kind, keys);

    private static void AssertSatisfied(DrawingDocument document)
    {
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(document, d));
        document.Constraints.Should().NotContain(c => c.State == SketchConstraintState.Unsatisfied);
    }
}
