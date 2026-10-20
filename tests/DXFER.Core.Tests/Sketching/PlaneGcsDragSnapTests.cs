using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsDragSnapTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptedDragPersistsOnlyEnabledEndpointOrMidpointSnap(bool midpoint, bool automatic)
    {
        var original = Document(midpoint);
        var runtime = new AcceptedDragRuntime();
        var adapter = new PlaneGcsSketchSolverAdapter(runtime);

        adapter.TryApplyDrag(original, "moving|point|end|4|1", new(4, 1), new(10, 2), false,
            out var next, out var status, automaticSnapConstraints: automatic).Should().BeTrue(status);

        runtime.Calls.Should().Be(1, "persisting the satisfied relation must not invoke a second solve");
        ((LineEntity)next.Entities[0]).End.Should().Be(new Point2(10, 2));
        next.Entities[1].Should().BeSameAs(original.Entities[1]);
        next.Entities[2].Should().BeSameAs(original.Entities[2]);
        next.Metadata.Should().BeSameAs(original.Metadata);
        next.Dimensions.Should().Equal(original.Dimensions);
        original.Constraints.Should().BeEmpty();
        ((LineEntity)original.Entities[0]).End.Should().Be(new Point2(4, 1));
        if (!automatic)
        {
            next.Constraints.Should().BeEmpty();
            return;
        }
        var relation = next.Constraints.Should().ContainSingle().Which;
        relation.Kind.Should().Be(midpoint ? SketchConstraintKind.Midpoint : SketchConstraintKind.Coincident);
        relation.ReferenceKeys.Should().Equal(midpoint ? "anchor" : "anchor:start", "moving:end");
        relation.State.Should().Be(SketchConstraintState.Satisfied);
        SketchConstraintService.ValidateConstraint(next, relation).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenSourceCannotAcquireAnAutomaticRelation(bool midpoint)
    {
        var original = Document(midpoint, hidden: true);
        var adapter = new PlaneGcsSketchSolverAdapter(new AcceptedDragRuntime());
        adapter.TryApplyDrag(original, "moving:end", new(4, 1), new(10, 2), false,
            out var next, out var status, automaticSnapConstraints: true).Should().BeTrue(status);
        next.Constraints.Should().BeEmpty();
        next.Entities[1].Should().BeSameAs(original.Entities[1]);
    }

    [Fact]
    public void ChangedLineWithSameMidpointDoesNotAcquireASnap()
    {
        var original = Document(midpoint: true);
        var adapter = new PlaneGcsSketchSolverAdapter(new AcceptedDragRuntime(rotateAnchor: true));
        adapter.TryApplyDrag(original, "moving:end", new(4, 1), new(10, 2), false,
            out var next, out var status, automaticSnapConstraints: true).Should().BeTrue(status);
        next.Constraints.Should().BeEmpty();
        ((LineEntity)next.Entities[1]).Start.Should().Be(new Point2(10, 0));
        next.Entities[2].Should().BeSameAs(original.Entities[2]);
    }

    [Fact]
    public void AutoOnWithoutAnyMatchStillAcceptsDrag()
    {
        var original = Document(midpoint: false);
        var adapter = new PlaneGcsSketchSolverAdapter(new AcceptedDragRuntime());
        adapter.TryApplyDrag(original, "moving:end", new(4, 1), new(7, 7), false,
            out var next, out var status, automaticSnapConstraints: true).Should().BeTrue(status);
        next.Constraints.Should().BeEmpty();
        ((LineEntity)next.Entities[0]).End.Should().Be(new Point2(7, 7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPersistenceIsIdempotentAndNeverChangesAcceptedGeometry(bool midpoint)
    {
        var original = Document(midpoint);
        var accepted = new DrawingDocument(original.Entities.Select(e => e.Id.Value == "moving"
            ? ((LineEntity)e) with { End = new Point2(10, 2) } : e), [], [], original.Metadata);
        var status = "Moved.";
        var first = SketchDragSnapService.AddPersistentConstraint(original, accepted, "moving:end", ref status);
        var second = SketchDragSnapService.AddPersistentConstraint(original, first, "moving:end", ref status);
        second.Should().BeSameAs(first);
        first.Constraints.Should().ContainSingle();
        for (var index = 0; index < accepted.Entities.Count; index++)
            first.Entities[index].Should().BeSameAs(accepted.Entities[index]);
    }

    [Fact]
    public void AutoOnOriginSnapPersistsWithoutCreatingAnOriginEntity()
    {
        var original = Document(midpoint: false);
        var adapter = new PlaneGcsSketchSolverAdapter(new AcceptedDragRuntime());
        adapter.TryApplyDrag(original, "moving:end", new(4, 1), new(0, 0), false,
            out var next, out var status, automaticSnapConstraints: true).Should().BeTrue(status);
        next.Constraints.Should().ContainSingle().Which.ReferenceKeys.Should().Equal("$origin:center", "moving:end");
        next.Entities.Should().HaveCount(original.Entities.Count);
    }

    private static DrawingDocument Document(bool midpoint, bool hidden = false)
    {
        var metadata = hidden ? new DrawingDocumentMetadata
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["anchor"] = new("hidden") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["hidden"] = new("hidden", IsHidden: true) }
        } : DrawingDocumentMetadata.Empty;
        return new DrawingDocument([
            new LineEntity(EntityId.Create("moving"), new(2, 0), new(4, 1)),
            new LineEntity(EntityId.Create("anchor"), midpoint ? new(8, 2) : new(10, 2), midpoint ? new(12, 2) : new(14, 2)),
            new PointEntity(EntityId.Create("unrelated"), new(-7, 9))
        ], [], [], metadata);
    }

    // These tests isolate post-solve persistence from the independently tested runtime.
    private sealed class AcceptedDragRuntime(bool rotateAnchor = false) : IPlaneGcsRuntime
    {
        public int Calls { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Calls++;
            var entities = request.Document.Entities.Select(entity => entity.Id switch
            {
                "moving" => entity with { Points = new[] { entity.Points[0], request.Drag!.End } },
                "anchor" when rotateAnchor => entity with { Points = new[] { new PlaneGcsPoint(10, 0), new PlaneGcsPoint(10, 4) } },
                _ => entity
            }).ToArray();
            return new PlaneGcsResponse("solved", entities, [], "PlaneGCS");
        }
    }
}
