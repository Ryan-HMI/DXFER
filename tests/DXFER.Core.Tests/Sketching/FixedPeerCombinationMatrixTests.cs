using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class FixedPeerCombinationMatrixTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EqualTangentFixedPeerPreservesValidMotionAndRejectsRadiusChange(int order)
    {
        var constraints = new[] {
            C("equal", SketchConstraintKind.Equal, "moving", "fixed"),
            C("tangent", SketchConstraintKind.Tangent, "line", "moving"),
            C("fix", SketchConstraintKind.Fix, "fixed") };
        int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        var original = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("moving"), new(0, 0), 3),
            new CircleEntity(EntityId.Create("fixed"), new(20, 0), 3),
            new LineEntity(EntityId.Create("line"), new(-5, 3), new(5, 3)) },
            Array.Empty<SketchDimension>(), orders[order].Select(i => constraints[i]));
        SketchGeometryDragService.TryApplyDrag(original, "moving|point|center|0|0", new(0, 0), new(2, 0.0001), false,
            out var moved, out var status).Should().BeTrue(status);
        moved.Entities[1].Should().Be(original.Entities[1]);
        moved.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        SketchGeometryDragService.TryApplyDrag(original, "moving|point|quadrant-0|3|0", new(3, 0), new(5, 0), false,
            out var rejected, out _).Should().BeFalse();
        rejected.Should().BeSameAs(original);
        var free = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix));
        SketchGeometryDragService.TryApplyDrag(free, "moving|point|quadrant-0|3|0", new(3, 0), new(5, 0), false,
            out var recovered, out status).Should().BeTrue(status);
        recovered.Entities.OfType<CircleEntity>().Should().OnlyContain(c => c.Radius == 5);
        recovered.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedTangentLineAllowsCenterMotionAndRadiusFreedom(bool reverse)
    {
        var constraints = new[] { C("tangent", SketchConstraintKind.Tangent, "line", "circle"), C("fix", SketchConstraintKind.Fix, "line") };
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("line"), new(-5, 3), new(5, 3)),
            new CircleEntity(EntityId.Create("circle"), new(0, 0), 3) }, Array.Empty<SketchDimension>(), reverse ? constraints.Reverse() : constraints);
        SketchGeometryDragService.TryApplyDrag(original, "circle|point|center|0|0", new(0, 0), new(0, 1), false,
            out var next, out var status).Should().BeTrue(status);
        next.Entities[0].Should().Be(original.Entities[0]);
        ((CircleEntity)next.Entities[1]).Radius.Should().Be(2);
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var diameter = new SketchDimension("diameter", SketchDimensionKind.Diameter, new[] { "circle" }, 6, isDriving: true);
        var restricted = new DrawingDocument(original.Entities, new[] { diameter }, original.Constraints);
        SketchGeometryDragService.TryApplyDrag(restricted, "circle|point|center|0|0", new(0, 0), new(0, 1), false,
            out var rejected, out _).Should().BeFalse();
        rejected.Should().BeSameAs(restricted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedMidpointEndpointRecoveryMovesOppositeEndUnlessThatEndIsFixed(bool reverse)
    {
        var constraints = new[] { C("mid", SketchConstraintKind.Midpoint, "line", "marker"), C("fix", SketchConstraintKind.Fix, "marker") };
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("line"), new(0, 0), new(10, 0)), new PointEntity(EntityId.Create("marker"), new(5, 0)) },
            Array.Empty<SketchDimension>(), reverse ? constraints.Reverse() : constraints);
        SketchGeometryDragService.TryApplyDrag(original, "line|point|end|10|0", new(10, 0), new(12, 4), false,
            out var moved, out var status).Should().BeTrue(status);
        ((LineEntity)moved.Entities[0]).Start.Should().Be(new Point2(-2, -4));
        ((LineEntity)moved.Entities[0]).End.Should().Be(new Point2(12, 4));
        moved.Entities[1].Should().Be(original.Entities[1]);
        moved.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var restricted = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(C("fix-start", SketchConstraintKind.Fix, "line:start")));
        SketchGeometryDragService.TryApplyDrag(restricted, "line|point|end|10|0", new(10, 0), new(12, 4), false,
            out var rejected, out status).Should().BeFalse();
        rejected.Should().BeSameAs(restricted);
        var free = new DrawingDocument(original.Entities, original.Dimensions, constraints.Take(1));
        SketchGeometryDragService.TryApplyDrag(free, "line|point|end|10|0", new(10, 0), new(12, 4), false,
            out var next, out status).Should().BeTrue(status);
        ((PointEntity)next.Entities[1]).Location.Should().Be(new Point2(6, 2));
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    private static SketchConstraint C(string id, SketchConstraintKind kind, params string[] keys) => new(id, kind, keys);

    [Theory]
    [InlineData(5, 0, false)]
    [InlineData(5.000000001, 0, false)]
    [InlineData(5.0001, 0.0001, true)]
    [InlineData(8, -2, true)]
    public void FixedMidpointNearCollapsePreservesGeometryAndRecovers(double x, double y, bool accepted)
    {
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("line"), new(0, 0), new(10, 0)), new PointEntity(EntityId.Create("marker"), new(5, 0)) },
            Array.Empty<SketchDimension>(), new[] { C("mid", SketchConstraintKind.Midpoint, "line", "marker"), C("fix", SketchConstraintKind.Fix, "marker") });
        SketchGeometryDragService.TryApplyDrag(original, "line|point|end|10|0", new(10, 0), new(x, y), false,
            out var next, out var status).Should().Be(accepted, status);
        if (!accepted) next.Should().BeSameAs(original);
        else
        {
            next.Entities[1].Should().Be(original.Entities[1]);
            var line = (LineEntity)next.Entities[0];
            ((line.Start.X + line.End.X) / 2).Should().BeApproximately(5, 1e-9);
            ((line.Start.Y + line.End.Y) / 2).Should().BeApproximately(0, 1e-9);
        }
        SketchGeometryDragService.TryApplyDrag(original, "line|point|start|0|0", new(0, 0), new(2, 2), false,
            out var recovered, out status).Should().BeTrue(status);
        ((LineEntity)recovered.Entities[0]).End.Should().Be(new Point2(8, -2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedMidpointAndEqualFixedLinePermitCompatibleRotation(bool reverse)
    {
        var constraints = new[] { C("mid", SketchConstraintKind.Midpoint, "line", "marker"), C("fix", SketchConstraintKind.Fix, "marker"),
            C("equal", SketchConstraintKind.Equal, "line", "peer"), C("fix-peer", SketchConstraintKind.Fix, "peer") };
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("line"), new(0, 0), new(10, 0)), new PointEntity(EntityId.Create("marker"), new(5, 0)),
            new LineEntity(EntityId.Create("peer"), new(0, 10), new(10, 10)) }, Array.Empty<SketchDimension>(), reverse ? constraints.Reverse() : constraints);
        SketchGeometryDragService.TryApplyDrag(original, "line|point|end|10|0", new(10, 0), new(5, 5), false,
            out var next, out var status).Should().BeTrue(status);
        ((LineEntity)next.Entities[0]).Start.Should().Be(new Point2(5, -5));
        next.Entities[1].Should().Be(original.Entities[1]);next.Entities[2].Should().Be(original.Entities[2]);
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}
