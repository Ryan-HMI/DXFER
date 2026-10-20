using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchDimensionAnchorServiceTests
{
    [Theory]
    [InlineData(false, 6.5)]
    [InlineData(true, 8)]
    public void InternalPretranslationIsRecomputedButExplicitAnchorEditsArePreserved(bool explicitEdit, double expectedX)
    {
        var baseline = new LineEntity(EntityId.Create("base"), new(0, 0), new(10, 0));
        var original = new DrawingDocument(
            [baseline, new LineEntity(EntityId.Create("offset"), new(0, 5), new(10, 5))],
            [new("gap", SketchDimensionKind.PointToLineDistance, ["base", "offset"], 5, new(5, 2.5), true)], []);
        var candidate = new DrawingDocument(
            [baseline, new LineEntity(EntityId.Create("offset"), new(3, 5), new(13, 5))],
            [new("gap", SketchDimensionKind.PointToLineDistance, ["base", "offset"], 5, new(8, 2.5), true)], []);

        var followed = SketchDimensionAnchorService.FollowDimensionAnchors(original, candidate,
            preserveExplicitAnchorEdits: explicitEdit);

        followed.Dimensions.Single().Anchor.Should().Be(new Point2(expectedX, 2.5));
        followed.Entities.Should().Equal(candidate.Entities);
        followed.Dimensions.Single().Value.Should().Be(5);
        followed.Dimensions.Single().IsDriving.Should().BeTrue();
        candidate.Dimensions.Single().Anchor.Should().Be(new Point2(8, 2.5));
        original.Dimensions.Single().Anchor.Should().Be(new Point2(5, 2.5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepositionedLabelFollowsSuccessiveGeometryDrags(bool planeGcs)
    {
        var original = Document();
        var repositioned = Reposition(original, new(7, -6));
        var runtime = new AcceptedDragRuntime();
        var adapter = new PlaneGcsSketchSolverAdapter(runtime);

        var first = Drag(repositioned, new(5, 0), new(8, 4));
        first.Dimensions.Single().Anchor.Should().Be(new Point2(10, -2));
        var secondPlacement = Reposition(first, new(20, 5));
        var second = Drag(secondPlacement, new(8, 4), new(9, 2));

        second.Dimensions.Single().Anchor.Should().Be(new Point2(21, 3));
        second.Dimensions.Single().Value.Should().Be(10);
        second.Dimensions.Single().IsDriving.Should().BeTrue();
        second.Constraints.Should().BeEmpty();
        original.Dimensions.Single().Anchor.Should().Be(new Point2(5, -2));
        repositioned.Dimensions.Single().Anchor.Should().Be(new Point2(7, -6));
        secondPlacement.Dimensions.Single().Anchor.Should().Be(new Point2(20, 5));
        runtime.Calls.Should().Be(planeGcs ? 2 : 0);

        DrawingDocument Drag(DrawingDocument document, Point2 start, Point2 end)
        {
            DrawingDocument next;
            string reason;
            var accepted = planeGcs
                ? adapter.TryApplyDrag(document, "edge", start, end, false,
                    out next, out reason, automaticSnapConstraints: false)
                : SketchGeometryDragService.TryApplyDrag(document, "edge", start, end, false,
                    out next, out reason, automaticSnapConstraints: false);
            accepted.Should().BeTrue(reason);
            return next;
        }
    }

    [Fact]
    public void PlaneGcsRotationPreservesRepositionedLabelOffsetInTheNewFrame()
    {
        var original = Reposition(Document(), new(7, -6));
        var runtime = new AcceptedDragRuntime(rotate: true);
        var adapter = new PlaneGcsSketchSolverAdapter(runtime);

        adapter.TryApplyDrag(original, "edge", new(0, 0), new(2, 3), false,
            out var next, out var reason).Should().BeTrue(reason);

        next.Entities.Single().Should().Be(new LineEntity(EntityId.Create("edge"), new(2, 3), new(2, 13)));
        next.Dimensions.Single().Anchor.Should().Be(new Point2(8, 10));
        next.Dimensions.Single().Value.Should().Be(10);
        runtime.Calls.Should().Be(1, "label following is presentation-only and must not solve again");
    }

    [Fact]
    public void FollowingAnchorsPreservesCandidateGeometryAndIntent()
    {
        var original = Reposition(Document(), new(7, -6));
        var line = new LineEntity(EntityId.Create("edge"), new(3, 4), new(23, 4));
        var intent = new SketchConstraint("horizontal", SketchConstraintKind.Horizontal,
            ["edge"], SketchConstraintState.Satisfied);
        var candidate = new DrawingDocument([line],
            [new SketchDimension("length", SketchDimensionKind.LinearDistance,
                ["edge:start", "edge:end"], 20, new(7, -6), isDriving: true)],
            [intent], original.Metadata);

        var followed = SketchDimensionAnchorService.FollowDimensionAnchors(original, candidate);

        followed.Entities.Single().Should().BeSameAs(line);
        followed.Constraints.Single().Should().BeSameAs(intent);
        followed.Metadata.Should().BeSameAs(candidate.Metadata);
        followed.Dimensions.Single().Value.Should().Be(20);
        followed.Dimensions.Single().IsDriving.Should().BeTrue();
        followed.Dimensions.Single().ReferenceKeys.Should().Equal("edge:start", "edge:end");
        followed.Dimensions.Single().Anchor.Should().Be(new Point2(15, -2));
        candidate.Dimensions.Single().Anchor.Should().Be(new Point2(7, -6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReferencesOrUnplacedLabelsKeepCandidatePlacement(bool unplaced)
    {
        var original = new DrawingDocument(Document().Entities,
            [new SketchDimension("length", SketchDimensionKind.LinearDistance,
                ["missing:start", "missing:end"], 10, unplaced ? null : new Point2(7, -6))], []);
        var label = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            ["missing:start", "missing:end"], 10, new(40, 50));
        var candidate = new DrawingDocument(original.Entities, [label], []);

        SketchDimensionAnchorService.FollowDimensionAnchors(original, candidate)
            .Dimensions.Single().Should().BeSameAs(label);
    }

    [Fact]
    public void RejectedPlaneGcsDragDoesNotMoveRepositionedLabel()
    {
        var original = Reposition(Document(), new(7, -6));
        var adapter = new PlaneGcsSketchSolverAdapter(new RejectedDragRuntime());

        adapter.TryApplyDrag(original, "edge", new(5, 0), new(8, 4), false,
            out var next, out _).Should().BeFalse();

        next.Should().BeSameAs(original);
        next.Dimensions.Single().Anchor.Should().Be(new Point2(7, -6));
    }

    private static DrawingDocument Document() => new(
        [new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0))],
        [new SketchDimension("length", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], 10, new(5, -2), isDriving: true)], []);

    private static DrawingDocument Reposition(DrawingDocument document, Point2 anchor)
    {
        var dimension = document.Dimensions.Single();
        return new DrawingDocument(document.Entities,
            [new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys,
                dimension.Value, anchor, dimension.IsDriving)], document.Constraints, document.Metadata);
    }

    private sealed class AcceptedDragRuntime(bool rotate = false) : IPlaneGcsRuntime
    {
        public int Calls { get; private set; }

        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Calls++;
            var drag = request.Drag!;
            var dx = drag.End.X - drag.Start.X;
            var dy = drag.End.Y - drag.Start.Y;
            var entities = request.Document.Entities.Select(entity => entity with
            {
                Points = entity.Points.Select(point => rotate
                    ? new PlaneGcsPoint(dx - point.Y, dy + point.X)
                    : new PlaneGcsPoint(point.X + dx, point.Y + dy)).ToArray()
            }).ToArray();
            return new PlaneGcsResponse("solved", entities, [], "PlaneGCS");
        }
    }

    private sealed class RejectedDragRuntime : IPlaneGcsRuntime
    {
        public PlaneGcsResponse Solve(PlaneGcsRequest request) =>
            new("failed", Diagnostics: ["Drag rejected."], Engine: "PlaneGCS");
    }
}
