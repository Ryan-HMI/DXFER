using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class LineIncidenceTests
{
    private static SketchConstraint C(SketchConstraintKind kind, params string[] refs) => new(kind + string.Join("/", refs), kind, refs, SketchConstraintState.Satisfied);
    private static DrawingDocument Document(bool lines, bool fixedPeer = true) => new(
        new DrawingEntity[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)),
            lines ? new LineEntity(EntityId.Create("moving"), new(12, 2), new(15, 6)) : new PointEntity(EntityId.Create("moving"), new(15, 3)) },
        Array.Empty<SketchDimension>(), fixedPeer ? new[] { C(SketchConstraintKind.Fix, "edge") } : Array.Empty<SketchConstraint>());

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CoincidentCommandBuildsPersistentPointOnLineOrCollinear(bool lines, bool reverse)
    {
        var original = Document(lines);
        var keys = reverse ? new[] { "moving", "edge" } : new[] { "edge", "moving" };
        SketchCommandFactory.TryBuildConstraint(original, keys, SketchConstraintKind.Coincident, "incidence", out var constraint, out var message)
            .Should().BeTrue(message);
        constraint.Kind.Should().Be(lines ? SketchConstraintKind.Collinear : SketchConstraintKind.PointOnLine);
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { constraint }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities[0].Should().Be(original.Entities[0]);
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        if (lines)
        {
            var moved = (LineEntity)result.Document.Entities[1];
            moved.Start.Y.Should().BeApproximately(0, 1e-8); moved.End.Y.Should().BeApproximately(0, 1e-8);
            (moved.End.X - moved.Start.X).Should().BeApproximately(5, 1e-8);
            moved.Start.X.Should().BeGreaterThan(10, "collinearity does not weld endpoints or require segment overlap");
        }
        else ((PointEntity)result.Document.Entities[1]).Location.Should().Be(new Point2(15, 0));
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result.Document, new(IncludeNativeDocument: true)));
        reopened.Constraints.Should().BeEquivalentTo(result.Document.Constraints);
        var from = lines ? new Point2(13.5, 0) : new Point2(15, 0);
        SketchGeometryDragService.TryApplyDrag(reopened, "moving", from, new(from.X + 2, from.Y + 1), false,
            out var dragged, out var status).Should().BeTrue(status);
        dragged.Entities[0].Should().Be(original.Entities[0]);
        dragged.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        if (lines) ((LineEntity)dragged.Entities[1]).Start.Y.Should().BeApproximately(0, 1e-8);
        else ((PointEntity)dragged.Entities[1]).Location.Should().Be(new Point2(17, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedConflictPreservesGeometryAndFailedIntent(bool lines)
    {
        var initial = Document(lines);
        var original = new DrawingDocument(initial.Entities, initial.Dimensions, initial.Constraints.Append(C(SketchConstraintKind.Fix, "moving")));
        var kind = lines ? SketchConstraintKind.Collinear : SketchConstraintKind.PointOnLine;
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { C(kind, "edge", "moving") }, Array.Empty<SketchDimension>()));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Constraints.Should().Contain(c => c.Kind == kind && c.State == SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void EndpointToEdgeUsesPointOnLineAndLeavesOtherEndpointUntouched()
    {
        var original = Document(true);
        SketchCommandFactory.TryBuildConstraint(original, new[] { "moving:start", "edge" }, SketchConstraintKind.Coincident,
            "endpoint-edge", out var constraint, out var message).Should().BeTrue(message);
        constraint.Kind.Should().Be(SketchConstraintKind.PointOnLine);
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { constraint }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities[1]).Start.Should().Be(new Point2(12, 0));
        ((LineEntity)result.Document.Entities[1]).End.Should().Be(new Point2(15, 6));
    }

    [Fact]
    public void OriginToEdgeTranslatesEdgeWithoutFixingItsEndpoints()
    {
        var line = new LineEntity(EntityId.Create("edge"), new(2, 3), new(12, 3));
        var original = new DrawingDocument(new DrawingEntity[] { line });
        SketchCommandFactory.TryBuildConstraint(original, new[] { "$origin:center", "edge" }, SketchConstraintKind.Coincident,
            "origin-edge", out var constraint, out _).Should().BeTrue();
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { constraint }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities[0]).Start.Should().Be(new Point2(2, 0));
        SketchGeometryDragService.TryApplyDrag(result.Document, "edge", new(7, 0), new(9, 0), false,
            out var moved, out var status).Should().BeTrue(status);
        ((LineEntity)moved.Entities[0]).Start.Should().Be(new Point2(4, 0));
    }

    [Fact]
    public void PointOnPolylineSegmentPreservesOtherSegments()
    {
        var polyline = new PolylineEntity(EntityId.Create("poly"), new Point2[] { new(0, 0), new(10, 0), new(10, 8) });
        var original = new DrawingDocument(new DrawingEntity[] { polyline, new PointEntity(EntityId.Create("p"), new(4, 3)) },
            Array.Empty<SketchDimension>(), new[] { C(SketchConstraintKind.Fix, "poly") });
        SketchCommandFactory.TryBuildConstraint(original, new[] { "p", "poly|segment|0" }, SketchConstraintKind.Coincident,
            "point-segment", out var constraint, out _).Should().BeTrue();
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { constraint }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities[0].Should().Be(polyline);
        ((PointEntity)result.Document.Entities[1]).Location.Should().Be(new Point2(4, 0));
    }

    [Fact]
    public void CollinearPreservesFixedEndpointAndDrivingLength()
    {
        var original = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("moving"), new(12, 0), new(15, 4)) },
            new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "moving:start", "moving:end" }, 5, null, true) },
            new[] { C(SketchConstraintKind.Fix, "edge"), C(SketchConstraintKind.Fix, "moving:start") });
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { C(SketchConstraintKind.Collinear, "edge", "moving") }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities[1]).Start.Should().Be(new Point2(12, 0));
        ((LineEntity)result.Document.Entities[1]).End.Should().Be(new Point2(17, 0));
        result.Document.Dimensions.Single().Value.Should().Be(5);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Collinear)]
    [InlineData(SketchConstraintKind.PointOnLine)]
    public void DegenerateSupportingLineRefusesWithoutInventingGeometry(SketchConstraintKind kind)
    {
        var initial = Document(kind == SketchConstraintKind.Collinear);
        var original = new DrawingDocument(new[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(0, 0)), initial.Entities[1] });
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { C(kind, "edge", "moving") }, Array.Empty<SketchDimension>()));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(original.Entities);
    }

    [Fact]
    public void CollinearDoesNotOverrideDrivingAngle()
    {
        var initial = Document(true);
        var original = new DrawingDocument(initial.Entities, new[] { new SketchDimension("angle", SketchDimensionKind.Angle,
            new[] { "edge", "moving" }, 53.13010235415598, null, true) }, initial.Constraints);
        var result = new LegacySketchSolverAdapter().Solve(new(original, new[] { C(SketchConstraintKind.Collinear, "edge", "moving") }, Array.Empty<SketchDimension>()));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Dimensions.Single().Value.Should().Be(53.13010235415598);
    }
}
