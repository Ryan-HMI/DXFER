using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class ClosedPolylineAliasTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void PartiallyDimensionedClosingCornerRetainsAllowedVerticalMovement(int vertex)
    {
        var free = Fixture(new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "poly|segment|0:end" }));
        var dimension = new SketchDimension("width", SketchDimensionKind.HorizontalDistance,
            new[] { "poly|segment|0:start", "poly|segment|0:end" }, 6, new(3, -1), true);
        var original = new DrawingDocument(free.Entities, new[] { dimension }, free.Constraints);
        SketchGeometryDragService.TryApplyDrag(original, $"poly|point|vertex-{vertex}|0|0", new(0, 0), new(-1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        ((PolylineEntity)next.Entities[0]).Vertices.Should().Equal(new Point2(0, 1), new(6, 0), new(4, 3), new(1, 4), new(0, 1));
        next.Dimensions.Single().Value.Should().Be(6);
        SketchDimensionSolverService.IsDimensionSatisfied(next, next.Dimensions.Single()).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintAndDimensionCannotMoveFixedClosingAlias(bool dimension)
    {
        var free = Fixture();
        var constraints = new[] {
            new SketchConstraint("fix-close", SketchConstraintKind.Fix, new[] { "poly|segment|3:end" }),
            new SketchConstraint("fix-anchor", SketchConstraintKind.Fix, new[] { "anchor" }) };
        var original = new DrawingDocument(free.Entities.Append(new PointEntity(EntityId.Create("anchor"), new(-2, 1))),
            Array.Empty<SketchDimension>(), constraints);
        var keys = new[] { "anchor", "poly|segment|0:start" };
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(original,
            dimension ? Array.Empty<SketchConstraint>() : new[] { new SketchConstraint("join", SketchConstraintKind.Coincident, keys) },
            dimension ? new[] { new SketchDimension("width", SketchDimensionKind.HorizontalDistance, keys, 3, isDriving: true) } : Array.Empty<SketchDimension>()));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(original.Entities);
    }

    [Fact]
    public void NearbyOpenEndsAreNotImplicitlyWelded()
    {
        var points = ((PolylineEntity)Fixture().Entities[0]).Vertices.ToArray();
        points[^1] = new(0, 0.00000001);
        var original = new DrawingDocument(new DrawingEntity[] { new PolylineEntity(EntityId.Create("poly"), points) });
        SketchGeometryDragService.TryApplyDrag(original, "poly|point|vertex-0|0|0", new(0, 0), new(-1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        ((PolylineEntity)next.Entities[0]).Vertices[^1].Should().Be(points[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeldedPeerMovesTheClosingAliasInEitherConstraintOrder(bool reverse)
    {
        var keys = new[] { "poly|segment|3:end", "peer" };
        var original = new DrawingDocument(Fixture().Entities.Append(new PointEntity(EntityId.Create("peer"), new(0, 0))),
            Array.Empty<SketchDimension>(), new[] { new SketchConstraint("weld", SketchConstraintKind.Coincident, reverse ? keys.Reverse() : keys) });
        SketchGeometryDragService.TryApplyDrag(original, "peer", new(0, 0), new(-1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        var points = ((PolylineEntity)next.Entities[0]).Vertices;
        points[0].Should().Be(new Point2(-1, 1));
        points[^1].Should().Be(points[0]);
        next.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ClosedVertexAliasesMoveTogether(int vertex)
    {
        var original = Fixture();
        SketchGeometryDragService.TryApplyDrag(original, $"poly|point|vertex-{vertex}|0|0", new(0, 0), new(-1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        ((PolylineEntity)next.Entities[0]).Vertices.Should().Equal(new Point2(-1, 1), new(6, 0), new(4, 3), new(1, 4), new(-1, 1));
    }

    [Theory]
    [InlineData(0, "poly|segment|3:end")]
    [InlineData(4, "poly|segment|0:start")]
    public void FixOnEitherAliasBlocksTheOther(int vertex, string fixedKey)
    {
        var original = Fixture(new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { fixedKey }));
        SketchGeometryDragService.TryApplyDrag(original, $"poly|point|vertex-{vertex}|0|0", new(0, 0), new(-1, 1), false,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void FirstAndLastSegmentTranslationKeepClosure(int segment)
    {
        var original = Fixture();
        SketchGeometryDragService.TryApplyDrag(original, $"poly|segment|{segment}", new(0, 0), new(-1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        var points = ((PolylineEntity)next.Entities[0]).Vertices;
        points[0].Should().Be(points[^1]);
        points[0].Should().Be(new Point2(-1, 1));
    }

    private static DrawingDocument Fixture(params SketchConstraint[] constraints) => new(
        new DrawingEntity[] { new PolylineEntity(EntityId.Create("poly"), new[] { new Point2(0, 0), new(6, 0), new(4, 3), new(1, 4), new(0, 0) }) },
        Array.Empty<SketchDimension>(), constraints);
}
